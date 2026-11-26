using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Application = System.Windows.Application;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace StreamGuard.Services
{
    public enum VisualCursorType
    {
        Arrow,
        IBeam,
        Hand
    }

    public sealed class CursorCaptureProtectionService : IDisposable
    {
        public const uint SPI_SETCURSORS = 0x0057;
        public const uint SPIF_UPDATEINIFILE = 0x01;
        public const uint SPIF_SENDCHANGE = 0x02;

        public static readonly uint[] SystemCursorIds =
        {
            32512, // OCR_NORMAL (arrow)
            32513, // OCR_IBEAM (i-beam)
            32514, // OCR_WAIT (wait)
            32515, // OCR_CROSS (cross)
            32516, // OCR_UP (up arrow)
            32640, // OCR_SIZE (size all/diagonal)
            32642, // OCR_SIZENWSE (size nw-se)
            32643, // OCR_SIZENESW (size ne-sw)
            32644, // OCR_SIZEWE (size w-e)
            32645, // OCR_SIZENS (size n-s)
            32646, // OCR_SIZEALL (size all)
            32648, // OCR_NO (slashed circle / prohibited)
            32649, // OCR_HAND (link pointer / hand)
            32650, // OCR_APPSTARTING (working in background)
            32671, // OCR_PIN
            32672  // OCR_PERSON
        };

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int VK_ESCAPE = 0x1B;
        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CreateCursor(IntPtr hInst, int xHotSpot, int yHotSpot, int nWidth, int nHeight, byte[] pvANDPlane, byte[] pvXORPlane);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyCursor(IntPtr hCursor);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetCursor(IntPtr hCursor);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetSystemCursor(IntPtr hcur, uint id);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hmod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        private static CursorCaptureProtectionService? _instance;
        public static CursorCaptureProtectionService Instance => _instance ??= new CursorCaptureProtectionService();

        private bool _isInAppCloakEnabled = true;
        private bool _isGlobalCloakActive;
        private readonly List<GlobalCursorOverlayWindow> _overlayWindows = new();
        private IntPtr _emergencyEscapeHook = IntPtr.Zero;
        private LowLevelKeyboardProc? _emergencyEscapeProc;
        private long _lastEscapeTimestamp;
        private bool _disposed;
        private bool _displaySettingsHooked;

        private readonly ConditionalWeakTable<Window, WindowCloakBinding> _attachedBindings = new();
        private readonly List<WeakReference<Window>> _knownWindows = new();

        public bool IsInAppCloakEnabled
        {
            get => _isInAppCloakEnabled;
            set
            {
                if (_isInAppCloakEnabled != value)
                {
                    _isInAppCloakEnabled = value;
                    NotifyInAppCloakPreferenceChanged();
                }
            }
        }

        public bool IsGlobalCloakActive => _isGlobalCloakActive;

        public event Action<bool>? GlobalCloakStateChanged;
        public event Action? EmergencyEscapeTriggered;

        public IReadOnlyList<GlobalCursorOverlayWindow> OverlayWindows => _overlayWindows;

        static CursorCaptureProtectionService()
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) => RestoreSystemCursors();
            AppDomain.CurrentDomain.UnhandledException += (_, _) => RestoreSystemCursors();
        }

        public CursorCaptureProtectionService()
        {
            _instance = this;
            HookDisplaySettings();
        }

        private void HookDisplaySettings()
        {
            if (!_displaySettingsHooked)
            {
                _displaySettingsHooked = true;
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            }
        }

        private void UnhookDisplaySettings()
        {
            if (_displaySettingsHooked)
            {
                _displaySettingsHooked = false;
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            }
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            if (_disposed) return;
            if (_isGlobalCloakActive)
            {
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    if (_isGlobalCloakActive && !_disposed)
                    {
                        EnsureOverlayWindows();
                        ShowOverlaysSafe();
                    }
                });
            }
        }

        public static IntPtr CreateTransparentCursor()
        {
            // 32x32 1-bit monochrome mask: 32 * 32 / 8 = 128 bytes
            // AND = 1 (0xFF), XOR = 0 (0x00) -> (Screen & 1) ^ 0 = Screen (invisible)
            byte[] andPlane = new byte[128];
            for (int i = 0; i < andPlane.Length; i++) andPlane[i] = 0xFF;
            byte[] xorPlane = new byte[128];
            return CreateCursor(IntPtr.Zero, 0, 0, 32, 32, andPlane, xorPlane);
        }

        public bool StartGlobalCursorCloak()
        {
            if (_disposed) return false;
            if (_isGlobalCloakActive)
            {
                ReassertOverlayTopmost();
                return true;
            }

            var replacedAny = false;
            foreach (var id in SystemCursorIds)
            {
                var hCur = CreateTransparentCursor();
                if (hCur != IntPtr.Zero)
                {
                    if (SetSystemCursor(hCur, id))
                    {
                        replacedAny = true;
                    }
                    else
                    {
                        DestroyCursor(hCur);
                    }
                }
            }

            if (!replacedAny)
            {
                RestoreSystemCursors();
                return false;
            }

            EnsureOverlayWindows();
            ShowOverlaysSafe();

            _isGlobalCloakActive = true;
            InstallEmergencyEscapeHook();
            GlobalCloakStateChanged?.Invoke(true);
            return true;
        }

        public void StopGlobalCursorCloak()
        {
            RemoveEmergencyEscapeHook();

            RestoreSystemCursors();

            HideOverlaysSafe();

            if (_isGlobalCloakActive)
            {
                _isGlobalCloakActive = false;
                GlobalCloakStateChanged?.Invoke(false);
            }
        }

        public static bool RestoreSystemCursors()
        {
            try
            {
                var ok = SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_SENDCHANGE | SPIF_UPDATEINIFILE);
                if (!ok) ok = SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_SENDCHANGE);
                if (!ok) ok = SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0);
                return ok;
            }
            catch
            {
                return false;
            }
        }

        private void EnsureOverlayWindows()
        {
            var screens = Screen.AllScreens;
            if (screens.Length == 0) return;

            bool needRecreate = _overlayWindows.Count != screens.Length;
            if (!needRecreate)
            {
                for (int i = 0; i < screens.Length; i++)
                {
                    if (_overlayWindows[i].TargetScreen?.DeviceName != screens[i].DeviceName)
                    {
                        needRecreate = true;
                        break;
                    }
                }
            }

            if (needRecreate)
            {
                foreach (var win in _overlayWindows)
                {
                    try { win.Close(); } catch { }
                }
                _overlayWindows.Clear();

                foreach (var screen in screens)
                {
                    var overlay = new GlobalCursorOverlayWindow(screen);
                    _overlayWindows.Add(overlay);
                }
            }
            else
            {
                foreach (var win in _overlayWindows)
                {
                    win.UpdateBounds();
                }
            }
        }

        private void ShowOverlaysSafe()
        {
            foreach (var win in _overlayWindows)
            {
                if (win.Dispatcher.CheckAccess())
                {
                    try
                    {
                        win.Show();
                        win.ReassertTopmost();
                    }
                    catch { }
                }
                else
                {
                    try
                    {
                        win.Dispatcher.Invoke(() =>
                        {
                            try
                            {
                                win.Show();
                                win.ReassertTopmost();
                            }
                            catch { }
                        });
                    }
                    catch { }
                }
            }
        }

        private void HideOverlaysSafe()
        {
            // HWND Reuse: Do not close overlay windows on stop, simply hide them
            foreach (var win in _overlayWindows)
            {
                if (win.Dispatcher.CheckAccess())
                {
                    try { win.Hide(); } catch { }
                }
                else
                {
                    try { win.Dispatcher.Invoke(() => { try { win.Hide(); } catch { } }); } catch { }
                }
            }
        }

        public void ReassertOverlayTopmost()
        {
            if (!_isGlobalCloakActive) return;
            foreach (var win in _overlayWindows)
            {
                if (win.Dispatcher.CheckAccess())
                {
                    try { win.ReassertTopmost(); } catch { }
                }
                else
                {
                    try { win.Dispatcher.InvokeAsync(() => { try { win.ReassertTopmost(); } catch { } }); } catch { }
                }
            }
        }

        private void InstallEmergencyEscapeHook()
        {
            if (_emergencyEscapeHook != IntPtr.Zero) return;
            try
            {
                _emergencyEscapeProc = EmergencyEscapeHookCallback;
                using var process = Process.GetCurrentProcess();
                using var module = process.MainModule;
                var hMod = module?.ModuleName != null ? GetModuleHandle(module.ModuleName) : IntPtr.Zero;
                _emergencyEscapeHook = SetWindowsHookEx(WH_KEYBOARD_LL, _emergencyEscapeProc, hMod, 0);
            }
            catch
            {
                // Fallback graceful: global hotkey and local escape keys still protect
            }
        }

        private void RemoveEmergencyEscapeHook()
        {
            if (_emergencyEscapeHook != IntPtr.Zero)
            {
                try { UnhookWindowsHookEx(_emergencyEscapeHook); } catch { }
                _emergencyEscapeHook = IntPtr.Zero;
                _emergencyEscapeProc = null;
            }
        }

        private IntPtr EmergencyEscapeHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            var hook = _emergencyEscapeHook;
            if (nCode >= 0 && _isGlobalCloakActive)
            {
                try
                {
                    var msg = wParam.ToInt32();
                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                    {
                        var vk = Marshal.ReadInt32(lParam);
                        if (vk == VK_ESCAPE)
                        {
                            bool isCtrl = (GetKeyState(VK_CONTROL) & 0x8000) != 0;
                            bool isShift = (GetKeyState(VK_SHIFT) & 0x8000) != 0;
                            long now = Environment.TickCount64;

                            // Require intentional double Escape within 500ms OR Ctrl+Shift+Escape panic hotkey
                            if ((isCtrl && isShift) || (now - _lastEscapeTimestamp <= 500))
                            {
                                _lastEscapeTimestamp = 0;
                                StopGlobalCursorCloak();
                                EmergencyEscapeTriggered?.Invoke();
                            }
                            else
                            {
                                _lastEscapeTimestamp = now;
                            }
                        }
                    }
                }
                catch { }
            }
            return CallNextHookEx(hook, nCode, wParam, lParam);
        }

        public void AttachInAppCloak(Window window)
        {
            if (window == null) return;

            lock (_knownWindows)
            {
                if (_attachedBindings.TryGetValue(window, out _))
                {
                    return; // Prevent duplicate attachment to the same window
                }
            }

            var binding = new WindowCloakBinding(this, window);
            lock (_knownWindows)
            {
                _attachedBindings.Add(window, binding);
                _knownWindows.Add(new WeakReference<Window>(window));
            }
        }

        private void NotifyInAppCloakPreferenceChanged()
        {
            lock (_knownWindows)
            {
                _knownWindows.RemoveAll(wr => !wr.TryGetTarget(out _));
                foreach (var wr in _knownWindows)
                {
                    if (wr.TryGetTarget(out var win) && _attachedBindings.TryGetValue(win, out var binding))
                    {
                        binding.RefreshCursorPreference(_isInAppCloakEnabled, _isGlobalCloakActive);
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            UnhookDisplaySettings();
            StopGlobalCursorCloak();
            foreach (var win in _overlayWindows)
            {
                try { win.Close(); } catch { }
            }
            _overlayWindows.Clear();
        }

        private sealed class WindowCloakBinding
        {
            private readonly CursorCaptureProtectionService _service;
            private readonly Window _window;
            private readonly HwndSourceHook _hook;
            private VisualCursorAdorner? _adorner;
            private HwndSource? _source;

            public WindowCloakBinding(CursorCaptureProtectionService service, Window window)
            {
                _service = service;
                _window = window;

                _hook = (IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                {
                    const int WM_SETCURSOR = 0x0020;
                    if (_service._isInAppCloakEnabled && !_service._isGlobalCloakActive && msg == WM_SETCURSOR)
                    {
                        SetCursor(IntPtr.Zero);
                        handled = true;
                        return (IntPtr)1;
                    }
                    return IntPtr.Zero;
                };

                void AttachHwnd()
                {
                    var hwnd = new WindowInteropHelper(_window).Handle;
                    if (hwnd != IntPtr.Zero)
                    {
                        _source = HwndSource.FromHwnd(hwnd);
                        _source?.AddHook(_hook);
                    }
                }

                if (_window.IsLoaded) AttachHwnd();
                else _window.SourceInitialized += (_, _) => AttachHwnd();

                if (_window.IsLoaded) SetupAdorner();
                else _window.Loaded += (_, _) => SetupAdorner();

                _window.PreviewMouseMove += OnPreviewMouseMove;
                _window.MouseEnter += OnMouseEnter;
                _window.MouseLeave += OnMouseLeave;
                _window.Closed += OnClosed;
            }

            private void SetupAdorner()
            {
                if (_adorner != null) return;
                var content = _window.Content as UIElement ?? _window;
                var layer = AdornerLayer.GetAdornerLayer(content);
                if (layer != null)
                {
                    _adorner = new VisualCursorAdorner(content);
                    layer.Add(_adorner);
                }
            }

            private void OnPreviewMouseMove(object sender, MouseEventArgs e)
            {
                if (_service._isInAppCloakEnabled && !_service._isGlobalCloakActive)
                {
                    var content = _window.Content as UIElement ?? _window;
                    var pos = e.GetPosition(content);
                    if (_adorner == null) SetupAdorner();
                    var hit = content.InputHitTest(pos) as DependencyObject;
                    var cursorType = CursorVisualHelper.DetectCursorType(hit);
                    _adorner?.UpdatePosition(pos, cursorType);
                    _adorner?.SetCursorVisibility(true);
                }
                else if (_adorner != null)
                {
                    _adorner.SetCursorVisibility(false);
                }
            }

            private void OnMouseEnter(object sender, MouseEventArgs e)
            {
                if (_service._isInAppCloakEnabled && !_service._isGlobalCloakActive)
                {
                    if (_adorner == null) SetupAdorner();
                    var content = _window.Content as UIElement ?? _window;
                    var pos = e.GetPosition(content);
                    var hit = content.InputHitTest(pos) as DependencyObject;
                    var cursorType = CursorVisualHelper.DetectCursorType(hit);
                    _adorner?.UpdatePosition(pos, cursorType);
                    _adorner?.SetCursorVisibility(true);
                    _window.Cursor = Cursors.None;
                }
            }

            private void OnMouseLeave(object sender, MouseEventArgs e)
            {
                _adorner?.SetCursorVisibility(false);
                _window.Cursor = null;
            }

            public void RefreshCursorPreference(bool inAppEnabled, bool isGlobalActive)
            {
                if (!_window.Dispatcher.CheckAccess())
                {
                    try { _window.Dispatcher.InvokeAsync(() => RefreshCursorPreference(inAppEnabled, isGlobalActive)); } catch { }
                    return;
                }

                if (!inAppEnabled || isGlobalActive)
                {
                    _adorner?.SetCursorVisibility(false);
                    _window.Cursor = null;
                    try { Mouse.UpdateCursor(); } catch { }
                }
                else if (_window.IsMouseOver)
                {
                    if (_adorner == null) SetupAdorner();
                    var content = _window.Content as UIElement ?? _window;
                    var pos = Mouse.GetPosition(content);
                    var hit = content.InputHitTest(pos) as DependencyObject;
                    var cursorType = CursorVisualHelper.DetectCursorType(hit);
                    _adorner?.UpdatePosition(pos, cursorType);
                    _adorner?.SetCursorVisibility(true);
                    _window.Cursor = Cursors.None;
                    SetCursor(IntPtr.Zero);
                }
            }

            private void OnClosed(object? sender, EventArgs e)
            {
                _source?.RemoveHook(_hook);
                _source = null;

                if (_adorner != null)
                {
                    var content = _window.Content as UIElement ?? _window;
                    var layer = AdornerLayer.GetAdornerLayer(content);
                    layer?.Remove(_adorner);
                    _adorner = null;
                }
            }
        }
    }

    internal sealed class VisualCursorAdorner : Adorner
    {
        private readonly VisualCollection _visuals;
        private readonly Grid _container;
        private readonly FrameworkElement _arrowVisual;
        private readonly FrameworkElement _ibeamVisual;
        private readonly FrameworkElement _handVisual;
        private readonly TranslateTransform _transform;
        private VisualCursorType _currentType = VisualCursorType.Arrow;
        private bool _isVisible;

        public VisualCursorAdorner(UIElement adornedElement) : base(adornedElement)
        {
            _transform = new TranslateTransform();
            _container = new Grid { Width = 32, Height = 32, IsHitTestVisible = false };

            _arrowVisual = CursorVisualHelper.CreateCursorElement(VisualCursorType.Arrow);
            _ibeamVisual = CursorVisualHelper.CreateCursorElement(VisualCursorType.IBeam);
            _handVisual = CursorVisualHelper.CreateCursorElement(VisualCursorType.Hand);

            _ibeamVisual.Visibility = Visibility.Collapsed;
            _handVisual.Visibility = Visibility.Collapsed;

            _container.Children.Add(_arrowVisual);
            _container.Children.Add(_ibeamVisual);
            _container.Children.Add(_handVisual);
            _container.RenderTransform = _transform;
            _container.Visibility = Visibility.Hidden;

            _visuals = new VisualCollection(this) { _container };
            IsHitTestVisible = false;
        }

        public void UpdatePosition(Point pt, VisualCursorType type = VisualCursorType.Arrow)
        {
            SetCursorType(type);
            var hotspot = CursorVisualHelper.GetHotspot(_currentType);
            _transform.X = pt.X - hotspot.X;
            _transform.Y = pt.Y - hotspot.Y;
        }

        public void SetCursorType(VisualCursorType type)
        {
            if (_currentType != type)
            {
                _currentType = type;
                _arrowVisual.Visibility = type == VisualCursorType.Arrow ? Visibility.Visible : Visibility.Collapsed;
                _ibeamVisual.Visibility = type == VisualCursorType.IBeam ? Visibility.Visible : Visibility.Collapsed;
                _handVisual.Visibility = type == VisualCursorType.Hand ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public void SetCursorVisibility(bool visible)
        {
            if (_isVisible != visible)
            {
                _isVisible = visible;
                _container.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
            }
        }

        protected override int VisualChildrenCount => _visuals?.Count ?? 0;
        protected override Visual GetVisualChild(int index) => _visuals![index];

        protected override Size MeasureOverride(Size constraint)
        {
            _container.Measure(new Size(32, 32));
            return base.MeasureOverride(constraint);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _container.Arrange(new Rect(0, 0, 32, 32));
            return finalSize;
        }
    }

    public static class CursorVisualHelper
    {
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT rcCaret;
        }

        [DllImport("user32.dll")]
        private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

        public static Point GetHotspot(VisualCursorType type)
        {
            return type switch
            {
                VisualCursorType.Arrow => new Point(0, 0),
                VisualCursorType.IBeam => new Point(16, 16), // Exactly width/2, height/2
                VisualCursorType.Hand => new Point(10, 2),  // Tip of index finger
                _ => new Point(0, 0)
            };
        }

        public static FrameworkElement CreateCursorElement()
        {
            return CreateCursorElement(VisualCursorType.Arrow);
        }

        public static FrameworkElement CreateCursorElement(VisualCursorType type)
        {
            var grid = new Grid
            {
                Width = 32,
                Height = 32,
                IsHitTestVisible = false
            };

            Geometry pathData;
            switch (type)
            {
                case VisualCursorType.IBeam:
                    // Crisp Windows-style I-Beam geometry centered in 32x32 (Hotspot at 16, 16)
                    pathData = Geometry.Parse("M 12,5 L 20,5 L 20,7 L 17,7 L 17,25 L 20,25 L 20,27 L 12,27 L 12,25 L 15,25 L 15,7 L 12,7 Z");
                    break;

                case VisualCursorType.Hand:
                    // Crisp Windows-style Hand pointer geometry (Hotspot at 10, 2)
                    pathData = Geometry.Parse("M 10,2 C 9,2 8,3 8,4.5 L 8,14.5 C 7.3,13.8 6.5,13.2 5.5,13.2 C 4.2,13.2 3.2,14.2 3.2,15.5 C 3.2,16.8 4.2,18.5 6.2,21.2 L 8.2,24 C 9.5,25.8 11.5,27 14,27 L 18.5,27 C 21.5,27 23.5,25 23.5,22 L 23.5,15 C 23.5,13.8 22.5,13 21.2,13 C 20.8,13 20.3,13.2 19.9,13.5 C 19.5,12.5 18.6,11.8 17.5,11.8 C 17,11.8 16.5,12 16.1,12.4 C 15.7,11.5 14.8,10.8 13.8,10.8 C 13.5,10.8 13.2,10.9 13,11 L 13,4.5 C 13,3 12,2 11,2 Z");
                    break;

                case VisualCursorType.Arrow:
                default:
                    // Modern, crisp Windows-style arrow cursor geometry (Hotspot at 0, 0)
                    pathData = Geometry.Parse("M 0,0 L 0,17 L 4.5,13 L 7.8,20.2 L 10.5,19 L 7.2,12 L 12.5,12 Z");
                    break;
            }

            pathData.Freeze();

            // 1. Soft drop shadow offset
            var shadow = new System.Windows.Shapes.Path
            {
                Data = pathData,
                Fill = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)),
                RenderTransform = new TranslateTransform(1.5, 1.5),
                IsHitTestVisible = false
            };

            // 2. Crisp dark border
            var border = new System.Windows.Shapes.Path
            {
                Data = pathData,
                Fill = Brushes.Black,
                Stroke = Brushes.Black,
                StrokeThickness = 1.5,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false
            };

            // 3. Crisp white body
            var body = new System.Windows.Shapes.Path
            {
                Data = pathData,
                Fill = Brushes.White,
                IsHitTestVisible = false
            };

            grid.Children.Add(shadow);
            grid.Children.Add(border);
            grid.Children.Add(body);
            return grid;
        }

        public static VisualCursorType DetectCursorType(DependencyObject? hit)
        {
            if (hit == null) return VisualCursorType.Arrow;

            DependencyObject? curr = hit;
            while (curr != null)
            {
                // 1. ScrollBars, tracks, thumbs, and scrollbar repeat buttons must always use standard Arrow cursor
                if (curr is System.Windows.Controls.Primitives.ScrollBar
                    || curr is System.Windows.Controls.Primitives.Track
                    || curr is System.Windows.Controls.Primitives.Thumb)
                {
                    return VisualCursorType.Arrow;
                }

                if (curr is System.Windows.Controls.Primitives.RepeatButton)
                {
                    // Check if RepeatButton is an internal child of a ScrollBar
                    var parent = VisualTreeHelper.GetParent(curr);
                    while (parent != null)
                    {
                        if (parent is System.Windows.Controls.Primitives.ScrollBar)
                            return VisualCursorType.Arrow;
                        parent = VisualTreeHelper.GetParent(parent);
                    }
                }

                // 2. Explicit cursor property
                if (curr is FrameworkElement fe && fe.Cursor != null)
                {
                    if (fe.Cursor == Cursors.IBeam) return VisualCursorType.IBeam;
                    if (fe.Cursor == Cursors.Hand) return VisualCursorType.Hand;
                    if (fe.Cursor == Cursors.Arrow) return VisualCursorType.Arrow;
                }

                // 3. Text inputs (only if enabled)
                if (curr is System.Windows.Controls.Primitives.TextBoxBase tb)
                {
                    return tb.IsEnabled ? VisualCursorType.IBeam : VisualCursorType.Arrow;
                }

                if (curr is System.Windows.Controls.PasswordBox pb)
                {
                    return pb.IsEnabled ? VisualCursorType.IBeam : VisualCursorType.Arrow;
                }

                // 4. Buttons and links (only if enabled)
                if (curr is System.Windows.Controls.Primitives.ButtonBase bb)
                {
                    return bb.IsEnabled ? VisualCursorType.Hand : VisualCursorType.Arrow;
                }

                if (curr is System.Windows.Documents.Hyperlink hl)
                {
                    return hl.IsEnabled ? VisualCursorType.Hand : VisualCursorType.Arrow;
                }

                if (curr is Visual || curr is Visual3D)
                {
                    curr = VisualTreeHelper.GetParent(curr);
                }
                else
                {
                    curr = LogicalTreeHelper.GetParent(curr);
                }
            }

            return VisualCursorType.Arrow;
        }

        public static VisualCursorType DetectCursorTypeFromWin32(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return VisualCursorType.Arrow;

            try
            {
                var sb = new System.Text.StringBuilder(256);
                GetClassName(hwnd, sb, sb.Capacity);
                var className = sb.ToString();

                if (className.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
                    className.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase) ||
                    className.IndexOf("TextBox", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    className.IndexOf("Scintilla", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    className.Equals("ConsoleWindowClass", StringComparison.OrdinalIgnoreCase) ||
                    className.StartsWith("CASCADIA_HOSTING_WINDOW_CLASS", StringComparison.OrdinalIgnoreCase))
                {
                    return VisualCursorType.IBeam;
                }

                if (className.Equals("Button", StringComparison.OrdinalIgnoreCase) ||
                    className.IndexOf("Link", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    className.IndexOf("Hyperlink", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return VisualCursorType.Hand;
                }

                // Check active caret state for the window thread; only return IBeam if hwnd is the actual caret owner or container
                var threadId = GetWindowThreadProcessId(hwnd, out _);
                if (threadId != 0)
                {
                    var guiInfo = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
                    if (GetGUIThreadInfo(threadId, ref guiInfo) && guiInfo.hwndCaret != IntPtr.Zero)
                    {
                        if (hwnd == guiInfo.hwndCaret || IsChild(hwnd, guiInfo.hwndCaret))
                        {
                            return VisualCursorType.IBeam;
                        }
                    }
                }
            }
            catch { }

            return VisualCursorType.Arrow;
        }
    }
}
