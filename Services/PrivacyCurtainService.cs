using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace StreamGuard.Services
{
    public interface IPrivacyCurtain
    {
        bool IsVisible { get; }
        bool CoversAllTargetScreens { get; }
        void Show();
        void Hide();
    }

    public sealed class PrivacyCurtainService : IPrivacyCurtain
    {
        private static readonly IntPtr HwndTopmost = new(-1);
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpShowWindow = 0x0040;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern nint GetLong64(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetLong32(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern nint SetLong64(IntPtr hWnd, int nIndex, nint dwNewLong);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        private static nint GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
            IntPtr.Size == 8 ? GetLong64(hWnd, nIndex) : GetLong32(hWnd, nIndex);
        private static nint SetWindowLongPtr(IntPtr hWnd, int nIndex, nint value) =>
            IntPtr.Size == 8 ? SetLong64(hWnd, nIndex, value) : SetLong32(hWnd, nIndex, (int)value);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint uFlags);

        private readonly List<Window> _curtains = new();
        private readonly Dictionary<Window, System.Drawing.Rectangle> _curtainBounds = new();
        private string _scopeKey = "";
        public bool IsVisible => _curtains.Count > 0;
        private int _requestedScreenCount;
        private bool _coversAllTargetScreens;
        public bool CoversAllTargetScreens => _coversAllTargetScreens;
        public event Action? DismissRequested;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);

        internal static bool PositionCurtain(IntPtr window, System.Drawing.Rectangle bounds) =>
            window != IntPtr.Zero && SetWindowPos(window, HwndTopmost, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                SwpNoActivate | SwpShowWindow) && GetWindowRect(window, out var actual) &&
            actual.Left == bounds.Left && actual.Top == bounds.Top && actual.Right == bounds.Right && actual.Bottom == bounds.Bottom;

        public PrivacyCurtainService()
        {
        }

        public void Show()
        {
            ShowOnScreens(Screen.AllScreens, "all");
        }

        public void ShowExternalOnly(bool includePrimaryIfNoSeparateScreen)
        {
            var targets = Screen.AllScreens
                .Where(screen => !screen.Primary)
                .ToList();

            if (targets.Count == 0 && includePrimaryIfNoSeparateScreen)
                targets = Screen.AllScreens.ToList();

            ShowOnScreens(targets, includePrimaryIfNoSeparateScreen ? "external-or-all" : "external");
        }

        private void ShowOnScreens(IReadOnlyCollection<Screen> screens, string scopeKey)
        {
            if (screens.Count == 0)
            {
                Hide();
                return;
            }

            var targetScopeKey = scopeKey + ":" + string.Join(
                "|",
                screens.Select(screen =>
                    $"{screen.DeviceName}:{screen.Bounds.Left},{screen.Bounds.Top},{screen.Bounds.Width},{screen.Bounds.Height}"));

            if (IsVisible && _scopeKey == targetScopeKey && _curtains.Count == screens.Count)
            {
                RaiseCurtains();
                return;
            }

            Hide();
            _scopeKey = targetScopeKey;
            _requestedScreenCount = screens.Count;

            foreach (var screen in screens)
            {
                try
                {
                    var curtain = new Window
                    {
                        WindowStyle = WindowStyle.None,
                        ResizeMode = ResizeMode.NoResize,
                        ShowInTaskbar = false,
                        ShowActivated = false,
                        Focusable = false,
                        Topmost = true,
                        Background = System.Windows.Media.Brushes.Black,
                        Left = screen.Bounds.Left,
                        Top = screen.Bounds.Top,
                        Width = Math.Max(1, screen.Bounds.Width),
                        Height = Math.Max(1, screen.Bounds.Height),
                        Content = BuildContent()
                    };

                    curtain.SourceInitialized += (_, _) =>
                    {
                        var hwnd = new WindowInteropHelper(curtain).Handle;
                        if (hwnd != IntPtr.Zero)
                        {
                            var source = HwndSource.FromHwnd(hwnd);
                            source?.AddHook((IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
                            {
                                if (msg == 0x0021 /* WM_MOUSEACTIVATE */)
                                {
                                    handled = true;
                                    return (IntPtr)3 /* MA_NOACTIVATE */;
                                }
                                return IntPtr.Zero;
                            });

                            var style = GetWindowLongPtr(hwnd, -20).ToInt64();
                            style |= 0x08000000L | 0x00000080L; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
                            SetWindowLongPtr(hwnd, -20, (nint)style);
                        }
                    };

                    curtain.Loaded += (_, _) =>
                    {
                        curtain.Topmost = true;
                        RaiseCurtain(curtain, screen.Bounds);
                        try { CursorCaptureProtectionService.Instance.ReassertOverlayTopmost(); } catch { }
                    };
                    curtain.Show();
                    _curtains.Add(curtain);
                    _curtainBounds[curtain] = screen.Bounds;
                }
                catch
                {
                }
            }

            RaiseCurtains();
        }

        public void Hide()
        {
            foreach (var curtain in _curtains)
            {
                try { curtain.Close(); } catch { }
            }
            _curtains.Clear();
            _curtainBounds.Clear();
            _coversAllTargetScreens = false;
            _scopeKey = "";
            _requestedScreenCount = 0;
        }

        public void Toggle()
        {
            if (IsVisible) Hide();
            else Show();
        }

        private void RaiseCurtains()
        {
            _coversAllTargetScreens = _requestedScreenCount > 0 && _curtains.Count == _requestedScreenCount;
            foreach (var curtain in _curtains)
                _coversAllTargetScreens &= RaiseCurtain(curtain, _curtainBounds[curtain]);
            try { CursorCaptureProtectionService.Instance.ReassertOverlayTopmost(); } catch { }
        }

        private static bool RaiseCurtain(Window curtain, System.Drawing.Rectangle bounds)
        {
            try
            {
                curtain.Topmost = true;
                var hwnd = new WindowInteropHelper(curtain).Handle;
                // Screen.Bounds and SetWindowPos use physical pixels; WPF Left/Width use DIPs.
                // Reusing WPF dimensions here progressively shrinks curtains on scaled monitors.
                return PositionCurtain(hwnd, bounds);
            }
            catch
            { return false; }
        }

        private UIElement BuildContent()
        {
            var root = new Grid
            {
                Background = System.Windows.Media.Brushes.Black
            };
            root.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount >= 2)
                {
                    RequestDismiss();
                    e.Handled = true;
                }
            };

            var card = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xEE, 0x11, 0x16, 0x25)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x41, 0x55)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(24, 20, 24, 20),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 440
            };

            var stack = new StackPanel
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };

            stack.Children.Add(new TextBlock
            {
                Text = "Driver Booster — Màn che bảo mật đang bật",
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xf8, 0xfa, 0xfc)),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            });

            stack.Children.Add(new TextBlock
            {
                Text = "Màn hình đang được che tạm thời để tránh phần mềm quay/chụp màn hình.\nNhấn phím Esc, nhấp đúp chuột, hoặc bấm nút bên dưới để tắt màn che.",
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x94, 0xa3, 0xb8)),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16)
            });

            var dismissBtn = new System.Windows.Controls.Button
            {
                Content = "Tắt màn che (Esc) / Dismiss Curtain",
                Padding = new Thickness(16, 8, 16, 8),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.White,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x25, 0x63, 0xeb)),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Focusable = false,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            dismissBtn.Click += (_, _) => RequestDismiss();
            stack.Children.Add(dismissBtn);

            card.Child = stack;
            root.Children.Add(card);
            return root;
        }

        private void RequestDismiss()
        {
            if (DismissRequested != null)
            {
                DismissRequested.Invoke();
            }
            else
            {
                Hide();
            }
        }
    }
}
