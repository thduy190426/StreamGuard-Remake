using System;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StreamGuard.Localization;
using StreamGuard.Services;
using Color = System.Windows.Media.Color;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace StreamGuard.Views
{
    public class SnippingOverlayWindow : Window
    {
        private readonly Bitmap? _background;
        private readonly BitmapSource? _snapshotSource;
        private readonly int _vLeft;
        private readonly int _vTop;
        private readonly int _vWidth;
        private readonly int _vHeight;
        private readonly bool _excludeFromCapture;

        private bool _isDragging;
        private bool _isCompleted;
        private bool _swallowEscapeKeyUp;
        private Point _startPoint;
        private Point _currentPoint;
        private IntPtr _escapeHook = IntPtr.Zero;
        private LowLevelKeyboardProc? _escapeHookProc;

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int VK_ESCAPE = 0x1B;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        public event Action<System.Drawing.Rectangle>? RegionSelected;
        public event Action? Canceled;

        public BitmapSource? SnapshotSource => _snapshotSource;
        public Bitmap? BackgroundBitmap => _background;
        public int VirtualLeft => _vLeft;
        public int VirtualTop => _vTop;
        public int VirtualWidth => _vWidth;
        public int VirtualHeight => _vHeight;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern nint GetLong(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern nint SetLong(IntPtr window, int index, nint value);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hmod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        private static readonly System.Windows.Media.Color DarkMaskColor = System.Windows.Media.Color.FromArgb(115, 0, 0, 0); // rgba(0,0,0,0.45)
        private static readonly SolidColorBrush DarkMaskBrush;
        private static readonly Pen SelectionBorderPen;
        private static readonly SolidColorBrush GuidePillBgBrush;
        private static readonly Pen GuidePillBorderPen;
        private static readonly SolidColorBrush DimBadgeBgBrush;
        private static readonly Pen DimBadgeBorderPen;
        private static readonly Typeface GuideTypeface;
        private static readonly Typeface DimBadgeTypeface;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        static SnippingOverlayWindow()
        {
            DarkMaskBrush = new SolidColorBrush(DarkMaskColor);
            DarkMaskBrush.Freeze();

            var borderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(59, 130, 246));
            borderBrush.Freeze();
            SelectionBorderPen = new Pen(borderBrush, 2.0);
            SelectionBorderPen.Freeze();

            GuidePillBgBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 20, 27, 43));
            GuidePillBgBrush.Freeze();
            var guideBorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 59, 130, 246));
            guideBorderBrush.Freeze();
            GuidePillBorderPen = new Pen(guideBorderBrush, 1.0);
            GuidePillBorderPen.Freeze();

            DimBadgeBgBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(230, 15, 23, 42));
            DimBadgeBgBrush.Freeze();
            var dimBorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(140, 59, 130, 246));
            dimBorderBrush.Freeze();
            DimBadgeBorderPen = new Pen(dimBorderBrush, 1.0);
            DimBadgeBorderPen.Freeze();

            var segoe = new System.Windows.Media.FontFamily("Segoe UI");
            GuideTypeface = new Typeface(segoe, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
            DimBadgeTypeface = new Typeface(segoe, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        }

        public SnippingOverlayWindow(
            Bitmap? background,
            int vLeft,
            int vTop,
            int vWidth,
            int vHeight,
            bool excludeFromCapture)
        {
            _background = background;
            _vLeft = vLeft;
            _vTop = vTop;
            _vWidth = vWidth;
            _vHeight = vHeight;
            _excludeFromCapture = excludeFromCapture;

            if (background != null)
            {
                _snapshotSource = SnippingToolService.ConvertBitmapToBitmapSource(background);
            }

            Title = "StreamGuard Snipping Overlay";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            Cursor = System.Windows.Input.Cursors.Cross;
            WindowStartupLocation = WindowStartupLocation.Manual;

            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);

            ApplyWindowBounds();

            SourceInitialized += OnSourceInitialized;
            Loaded += (_, _) => ReassertTopmost();

            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += OnMouseUp;
            PreviewKeyDown += OnPreviewKeyDown;
            MouseRightButtonDown += (s, e) => { Cancel(); };
            LostMouseCapture += (s, e) => { if (_isDragging) Cancel(); };
            Closed += (s, e) =>
            {
                RemoveEscapeHook();
                if (!_isCompleted) { _isCompleted = true; Canceled?.Invoke(); }
            };
        }

        public void ApplyWindowBounds()
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var bounds = SnippingCoordinateHelper.CalculateOverlayBounds(
                _vLeft, _vTop, _vWidth, _vHeight, dpi.DpiScaleX, dpi.DpiScaleY);

            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            var bounds = SnippingCoordinateHelper.CalculateOverlayBounds(
                _vLeft, _vTop, _vWidth, _vHeight, newDpi.DpiScaleX, newDpi.DpiScaleY);

            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
            InvalidateVisual();
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;

            const int GWL_EXSTYLE = -20;
            const long WS_EX_TOOLWINDOW = 0x00000080L;
            const long WS_EX_TOPMOST = 0x00000008L;
            const long WS_EX_NOACTIVATE = 0x08000000L;
            const uint SWP_NOSIZE = 0x0001;
            const uint SWP_NOMOVE = 0x0002;
            const uint SWP_NOACTIVATE = 0x0010;
            const uint SWP_FRAMECHANGED = 0x0020;

            var source = HwndSource.FromHwnd(handle);
            source?.AddHook((IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
            {
                if (msg == 0x0021 /* WM_MOUSEACTIVATE */)
                {
                    handled = true;
                    return (IntPtr)3 /* MA_NOACTIVATE */;
                }
                return IntPtr.Zero;
            });

            // Apply ToolWindow and NoActivate exstyle so it doesn't show in Alt+Tab, never takes focus and enforces Topmost
            var exStyle = GetLong(handle, GWL_EXSTYLE).ToInt64();
            exStyle |= WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE;
            SetLong(handle, GWL_EXSTYLE, (nint)exStyle);
            SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);

            ApplyWindowBounds();

            // Apply capture protection
            if (_excludeFromCapture)
            {
                new WindowCaptureProtectionService().ApplyAndVerify(handle);
            }

            InstallEscapeHook();
        }

        private void ReassertTopmost()
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;

            const uint SWP_NOSIZE = 0x0001;
            const uint SWP_NOMOVE = 0x0002;
            const uint SWP_NOACTIVATE = 0x0010;
            const uint SWP_SHOWWINDOW = 0x0040;
            SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        private void InstallEscapeHook()
        {
            if (_escapeHook != IntPtr.Zero || _isCompleted) return;
            _escapeHookProc = EscapeHookCallback;
            using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            var hMod = GetModuleHandle(curModule?.ModuleName);
            _escapeHook = SetWindowsHookEx(WH_KEYBOARD_LL, _escapeHookProc, hMod, 0);
        }

        private void RemoveEscapeHook()
        {
            if (_escapeHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_escapeHook);
                _escapeHook = IntPtr.Zero;
            }
            _escapeHookProc = null;
            _swallowEscapeKeyUp = false;
        }

        private IntPtr EscapeHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var vkCode = Marshal.ReadInt32(lParam);
                if (HandleLowLevelEscapeKey(wParam.ToInt32(), vkCode))
                {
                    return new IntPtr(1);
                }
            }
            return CallNextHookEx(_escapeHook, nCode, wParam, lParam);
        }

        internal bool HandleLowLevelEscapeKey(int msg, int vkCode)
        {
            if (_isCompleted || vkCode != VK_ESCAPE)
                return false;

            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                _swallowEscapeKeyUp = true;
                if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                {
                    Dispatcher.BeginInvoke(new Action(Cancel));
                }
                return true;
            }

            if ((msg == WM_KEYUP || msg == WM_SYSKEYUP) && _swallowEscapeKeyUp)
            {
                _swallowEscapeKeyUp = false;
                return true;
            }

            return false;
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _isDragging = true;
                _startPoint = e.GetPosition(this);
                _currentPoint = _startPoint;
                CaptureMouse();
                InvalidateVisual();
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                _currentPoint = e.GetPosition(this);
                InvalidateVisual();
            }
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                ReleaseMouseCapture();

                var endPoint = e.GetPosition(this);
                var startX = Math.Clamp(_startPoint.X, 0, ActualWidth);
                var startY = Math.Clamp(_startPoint.Y, 0, ActualHeight);
                var endX = Math.Clamp(endPoint.X, 0, ActualWidth);
                var endY = Math.Clamp(endPoint.Y, 0, ActualHeight);

                var selRectInDips = new Rect(
                    Math.Min(startX, endX),
                    Math.Min(startY, endY),
                    Math.Abs(endX - startX),
                    Math.Abs(endY - startY));

                var dpi = VisualTreeHelper.GetDpi(this);
                var cropRect = SnippingCoordinateHelper.ConvertDipsToPhysicalPixels(
                    selRectInDips, dpi.DpiScaleX, dpi.DpiScaleY);

                if (cropRect.Width < 4 || cropRect.Height < 4)
                {
                    Cancel();
                    return;
                }

                _isCompleted = true;
                RemoveEscapeHook();
                RegionSelected?.Invoke(cropRect);
                Close();
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Cancel();
                e.Handled = true;
            }
        }

        private void Cancel()
        {
            if (_isCompleted) return;
            _isCompleted = true;
            RemoveEscapeHook();
            if (_isDragging)
            {
                _isDragging = false;
                ReleaseMouseCapture();
            }
            Canceled?.Invoke();
            Close();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            var fullRect = new Rect(0, 0, ActualWidth, ActualHeight);
            if (fullRect.Width <= 0 || fullRect.Height <= 0) return;

            // 1. Draw static desktop snapshot background if available (eliminates Ghost Snapshot, 100% WYSIWYG)
            if (_snapshotSource != null)
            {
                dc.DrawImage(_snapshotSource, fullRect);
            }

            if (!_isDragging)
            {
                // Full mask
                dc.DrawRectangle(DarkMaskBrush, null, fullRect);

                // Guide pill at top center
                DrawGuideBadge(dc, fullRect);
                return;
            }

            var startX = Math.Clamp(_startPoint.X, 0, ActualWidth);
            var startY = Math.Clamp(_startPoint.Y, 0, ActualHeight);
            var currX = Math.Clamp(_currentPoint.X, 0, ActualWidth);
            var currY = Math.Clamp(_currentPoint.Y, 0, ActualHeight);

            var selRect = new Rect(
                Math.Min(startX, currX),
                Math.Min(startY, currY),
                Math.Abs(currX - startX),
                Math.Abs(currY - startY));

            if (selRect.Width < 2 || selRect.Height < 2)
            {
                dc.DrawRectangle(DarkMaskBrush, null, fullRect);
                DrawGuideBadge(dc, fullRect);
                return;
            }

            // Exclude selection from darkened area by drawing 4 surrounding rectangles.
            // This leaves the static snapshot underneath with 100% clarity, zero GPU CSG polygon tessellation stutter.
            if (selRect.Top > 0)
            {
                dc.DrawRectangle(DarkMaskBrush, null, new Rect(0, 0, fullRect.Width, selRect.Top));
            }
            if (selRect.Bottom < fullRect.Height)
            {
                dc.DrawRectangle(DarkMaskBrush, null, new Rect(0, selRect.Bottom, fullRect.Width, fullRect.Height - selRect.Bottom));
            }
            if (selRect.Left > 0)
            {
                dc.DrawRectangle(DarkMaskBrush, null, new Rect(0, selRect.Top, selRect.Left, selRect.Height));
            }
            if (selRect.Right < fullRect.Width)
            {
                dc.DrawRectangle(DarkMaskBrush, null, new Rect(selRect.Right, selRect.Top, fullRect.Width - selRect.Right, selRect.Height));
            }

            // Selection border (accent blue with sharp stroke)
            dc.DrawRectangle(null, SelectionBorderPen, selRect);

            // Draw dimension badge
            DrawDimensionBadge(dc, selRect);
        }

        private void DrawGuideBadge(DrawingContext dc, Rect fullRect)
        {
            var guideText = LocalizationService.GetString("Snipping.OverlayGuide", "Kéo thả chuột để chụp vùng chọn • Esc hoặc chuột phải để hủy");
            var formattedText = new FormattedText(
                guideText,
                CultureInfo.CurrentUICulture,
                System.Windows.FlowDirection.LeftToRight,
                GuideTypeface,
                13,
                System.Windows.Media.Brushes.White,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            var pillWidth = formattedText.Width + 28;
            var pillHeight = formattedText.Height + 14;
            var pillX = (fullRect.Width - pillWidth) / 2;
            var pillY = 32;

            var pillRect = new Rect(pillX, pillY, pillWidth, pillHeight);
            dc.DrawRoundedRectangle(GuidePillBgBrush, GuidePillBorderPen, pillRect, 8, 8);
            dc.DrawText(formattedText, new Point(pillX + 14, pillY + 7));
        }

        private void DrawDimensionBadge(DrawingContext dc, Rect selRect)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var cropRect = SnippingCoordinateHelper.ConvertDipsToPhysicalPixels(
                selRect, dpi.DpiScaleX, dpi.DpiScaleY);

            int pxW = cropRect.Width;
            int pxH = cropRect.Height;

            var dimText = $"{pxW} × {pxH} px";
            var formattedText = new FormattedText(
                dimText,
                CultureInfo.CurrentUICulture,
                System.Windows.FlowDirection.LeftToRight,
                DimBadgeTypeface,
                11,
                System.Windows.Media.Brushes.White,
                dpi.PixelsPerDip);

            var pillW = formattedText.Width + 16;
            var pillH = formattedText.Height + 8;

            // Position below selection, or above if close to bottom
            var badgeX = selRect.Left + (selRect.Width - pillW) / 2;
            var badgeY = selRect.Bottom + 6;
            if (badgeY + pillH > ActualHeight - 10)
            {
                badgeY = selRect.Top - pillH - 6;
            }

            if (badgeX < 4) badgeX = 4;
            if (badgeX + pillW > ActualWidth - 4) badgeX = Math.Max(4, ActualWidth - pillW - 4);
            if (badgeY < 4) badgeY = 4;

            var pillRect = new Rect(badgeX, badgeY, pillW, pillH);
            dc.DrawRoundedRectangle(DimBadgeBgBrush, DimBadgeBorderPen, pillRect, 6, 6);
            dc.DrawText(formattedText, new Point(badgeX + 8, badgeY + 4));
        }
    }
}
