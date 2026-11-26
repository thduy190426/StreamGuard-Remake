using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using StreamGuard.Services;
using Application = System.Windows.Application;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace StreamGuard
{
    public sealed class GlobalCursorOverlayWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const long WS_EX_TRANSPARENT = 0x00000020L;
        private const long WS_EX_TOOLWINDOW = 0x00000080L;
        private const long WS_EX_LAYERED = 0x00080000L;
        private const long WS_EX_NOACTIVATE = 0x08000000L;

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private static readonly IntPtr HWND_TOPMOST = new(-1);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern nint GetLongPtr(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern nint SetLongPtr(IntPtr window, int index, nint value);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
        private const uint GW_HWNDNEXT = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        public Screen? TargetScreen { get; }
        private System.Drawing.Rectangle _screenBounds;
        private readonly Grid _cursorGrid;
        private readonly FrameworkElement _arrowVisual;
        private readonly FrameworkElement _ibeamVisual;
        private readonly FrameworkElement _handVisual;
        private readonly TranslateTransform _transform;
        private VisualCursorType _currentType = VisualCursorType.Arrow;
        private int _lastX = int.MinValue;
        private int _lastY = int.MinValue;
        private long _lastTopmostReassertTick;
        private IntPtr _hwnd;
        private bool _isRendering;
        private bool _forceRedraw;
        private bool _displaySettingsHooked;

        public CaptureAffinityResult? CaptureResult { get; private set; }
        public VisualCursorType CurrentCursorType => _currentType;

        public GlobalCursorOverlayWindow() : this(null) { }

        public GlobalCursorOverlayWindow(Screen? targetScreen)
        {
            TargetScreen = targetScreen;
            Title = "Driver Booster — Global Cursor Cloak Overlay";
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            Topmost = true;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            IsHitTestVisible = false;

            UpdateBounds();

            var canvas = new Canvas { IsHitTestVisible = false };
            _cursorGrid = new Grid
            {
                Width = 32,
                Height = 32,
                IsHitTestVisible = false
            };

            _arrowVisual = CursorVisualHelper.CreateCursorElement(VisualCursorType.Arrow);
            _ibeamVisual = CursorVisualHelper.CreateCursorElement(VisualCursorType.IBeam);
            _handVisual = CursorVisualHelper.CreateCursorElement(VisualCursorType.Hand);

            _ibeamVisual.Visibility = Visibility.Collapsed;
            _handVisual.Visibility = Visibility.Collapsed;

            _cursorGrid.Children.Add(_arrowVisual);
            _cursorGrid.Children.Add(_ibeamVisual);
            _cursorGrid.Children.Add(_handVisual);

            _transform = new TranslateTransform();
            _cursorGrid.RenderTransform = _transform;
            canvas.Children.Add(_cursorGrid);
            Content = canvas;

            SourceInitialized += OnSourceInitialized;
            Loaded += (_, _) => StartTracking();
            IsVisibleChanged += (_, _) =>
            {
                if (IsVisible)
                {
                    ReassertTopmost();
                    StartTracking();
                }
                else
                {
                    StopTracking();
                }
            };
            Closed += (_, _) =>
            {
                StopTracking();
                UnhookDisplaySettings();
            };

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

        public void UpdateBounds()
        {
            var screen = TargetScreen ?? Screen.PrimaryScreen ?? Screen.AllScreens.FirstOrDefault();
            if (screen != null)
            {
                _screenBounds = screen.Bounds;
                Left = screen.Bounds.Left;
                Top = screen.Bounds.Top;
                Width = Math.Max(100, screen.Bounds.Width);
                Height = Math.Max(100, screen.Bounds.Height);
            }
            else
            {
                _screenBounds = new System.Drawing.Rectangle(
                    (int)SystemParameters.VirtualScreenLeft,
                    (int)SystemParameters.VirtualScreenTop,
                    (int)Math.Max(100, SystemParameters.VirtualScreenWidth),
                    (int)Math.Max(100, SystemParameters.VirtualScreenHeight));
                Left = _screenBounds.Left;
                Top = _screenBounds.Top;
                Width = _screenBounds.Width;
                Height = _screenBounds.Height;
            }

            var hwnd = _hwnd != IntPtr.Zero ? _hwnd : new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetWindowPos(hwnd, HWND_TOPMOST,
                    _screenBounds.Left, _screenBounds.Top, _screenBounds.Width, _screenBounds.Height,
                    SWP_NOACTIVATE | SWP_FRAMECHANGED);
            }
        }

        public void ReassertTopmost()
        {
            var hwnd = _hwnd != IntPtr.Zero ? _hwnd : new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            UpdateBounds();
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            Dispatcher.InvokeAsync(UpdateBounds);
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            var hwnd = _hwnd != IntPtr.Zero ? _hwnd : new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            _hwnd = hwnd;

            var style = GetLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            style |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE;
            SetLongPtr(hwnd, GWL_EXSTYLE, (nint)style);

            // Set exact physical bounds and apply non-activating layered frame
            SetWindowPos(hwnd, HWND_TOPMOST,
                _screenBounds.Left, _screenBounds.Top, _screenBounds.Width, _screenBounds.Height,
                SWP_NOACTIVATE | SWP_FRAMECHANGED);

            // Apply capture exclusion: Exclude from capture!
            CaptureResult = new WindowCaptureProtectionService().ApplyAndVerify(hwnd);
        }

        public void SetCursorType(VisualCursorType type)
        {
            if (_currentType != type)
            {
                _currentType = type;
                _arrowVisual.Visibility = type == VisualCursorType.Arrow ? Visibility.Visible : Visibility.Collapsed;
                _ibeamVisual.Visibility = type == VisualCursorType.IBeam ? Visibility.Visible : Visibility.Collapsed;
                _handVisual.Visibility = type == VisualCursorType.Hand ? Visibility.Visible : Visibility.Collapsed;
                _forceRedraw = true;
            }
        }

        private void StartTracking()
        {
            if (_isRendering) return;
            _isRendering = true;
            CompositionTarget.Rendering += OnRendering;
        }

        private void StopTracking()
        {
            if (!_isRendering) return;
            _isRendering = false;
            CompositionTarget.Rendering -= OnRendering;
        }

        private void UpdateCursorShapeAtPoint(POINT pt)
        {
            var detected = VisualCursorType.Arrow;

            var hwndAtPt = WindowFromPoint(pt);
            if (hwndAtPt == IntPtr.Zero)
            {
                SetCursorType(detected);
                return;
            }

            var myHwnd = _hwnd != IntPtr.Zero ? _hwnd : new WindowInteropHelper(this).Handle;
            if (hwndAtPt == myHwnd)
            {
                hwndAtPt = GetWindow(hwndAtPt, GW_HWNDNEXT);
                if (hwndAtPt == IntPtr.Zero)
                {
                    SetCursorType(detected);
                    return;
                }
            }

            GetWindowThreadProcessId(hwndAtPt, out var pid);
            var currentPid = (uint)Environment.ProcessId;

            // 1. If cursor is over an active window belonging to our current WPF process, do WPF tree hit testing
            if (pid == currentPid && Application.Current != null)
            {
                Window? targetWin = null;
                foreach (Window win in Application.Current.Windows)
                {
                    if (win is GlobalCursorOverlayWindow || !win.IsVisible || win.WindowState == WindowState.Minimized)
                        continue;

                    var hwnd = new WindowInteropHelper(win).Handle;
                    if (hwnd == hwndAtPt || IsChild(hwnd, hwndAtPt))
                    {
                        targetWin = win;
                        break;
                    }
                }

                if (targetWin != null)
                {
                    try
                    {
                        var content = targetWin.Content as UIElement ?? targetWin;
                        var localPos = targetWin.PointFromScreen(new Point(pt.X, pt.Y));
                        var hit = content.InputHitTest(localPos) as DependencyObject;
                        detected = CursorVisualHelper.DetectCursorType(hit);
                        SetCursorType(detected);
                        return;
                    }
                    catch { }
                }
            }

            // 2. Fallback to Win32 hit testing for external applications
            detected = CursorVisualHelper.DetectCursorTypeFromWin32(hwndAtPt);
            SetCursorType(detected);
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            if (!IsVisible) return;
            if (!GetCursorPos(out var pt)) return;

            // Check if cursor is currently within this screen's physical bounds
            if (pt.X < _screenBounds.Left || pt.X >= _screenBounds.Right ||
                pt.Y < _screenBounds.Top || pt.Y >= _screenBounds.Bottom)
            {
                if (_cursorGrid.Visibility != Visibility.Collapsed)
                {
                    _cursorGrid.Visibility = Visibility.Collapsed;
                }
                return;
            }

            if (_cursorGrid.Visibility != Visibility.Visible)
            {
                _cursorGrid.Visibility = Visibility.Visible;
            }

            var nowTick = Environment.TickCount64;
            if (nowTick - _lastTopmostReassertTick >= 500)
            {
                _lastTopmostReassertTick = nowTick;
                ReassertTopmost();
            }

            if (pt.X == _lastX && pt.Y == _lastY && !_forceRedraw) return;
            _lastX = pt.X;
            _lastY = pt.Y;
            _forceRedraw = false;

            UpdateCursorShapeAtPoint(pt);
            var hotspot = CursorVisualHelper.GetHotspot(_currentType);

            // Primary coordinate conversion: Exact physical pixel conversion via ScreenToClient + TransformFromDevice
            // This is 100% accurate across all displays with per-monitor DPI scaling factors
            var hwnd = _hwnd != IntPtr.Zero ? _hwnd : new WindowInteropHelper(this).Handle;
            var clientPt = new POINT { X = pt.X, Y = pt.Y };
            if (hwnd != IntPtr.Zero && ScreenToClient(hwnd, ref clientPt))
            {
                var source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget != null)
                {
                    var dipPt = source.CompositionTarget.TransformFromDevice.Transform(new Point(clientPt.X, clientPt.Y));
                    _transform.X = dipPt.X - hotspot.X;
                    _transform.Y = dipPt.Y - hotspot.Y;
                }
                else
                {
                    _transform.X = clientPt.X - hotspot.X;
                    _transform.Y = clientPt.Y - hotspot.Y;
                }
            }
            else
            {
                try
                {
                    var localPt = PointFromScreen(new Point(pt.X, pt.Y));
                    _transform.X = localPt.X - hotspot.X;
                    _transform.Y = localPt.Y - hotspot.Y;
                }
                catch
                {
                    _transform.X = (pt.X - _screenBounds.Left) - hotspot.X;
                    _transform.Y = (pt.Y - _screenBounds.Top) - hotspot.Y;
                }
            }
        }
    }
}
