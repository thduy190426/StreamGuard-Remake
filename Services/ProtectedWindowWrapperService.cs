using StreamGuard.Helpers;
using StreamGuard.Models;
using System;
using StreamGuard.Localization;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StreamGuard.Services
{
    public static class ProtectedWindowWrapperService
    {
        private const int GwlStyle = -16;
        private const int GwlExStyle = -20;
        private const nint WsChild = 0x40000000;
        private const nint WsVisible = 0x10000000;
        private const nint WsClipSiblings = 0x04000000;
        private static readonly nint WsPopup = unchecked((nint)(uint)0x80000000);
        private static readonly Guid TaskbarListClsid = new("56FDF344-FD6D-11d0-958A-006097C9A090");
        private const nint WsExAppWindow = 0x00040000;
        private const int WsPopupStyle = unchecked((int)0x80000000);
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;
        private const uint SwpShowWindow = 0x0040;
        private const int SwShow = 5;
        private const uint WdaExcludeFromCapture = 0x00000011;
        private static readonly WindowCaptureProtectionService CaptureProtection = new();
        private static readonly IntPtr HwndTop = IntPtr.Zero;
        private const int AwBlend = 0x00080000;
        private const int AwHide = 0x00010000;
        private const int AwActivate = 0x00020000;
        private const int WmNcHitTest = 0x0084;
        private const int HtClient = 1;
        private const int HtCaption = 2;
        private const int HtLeft = 10;
        private const int HtRight = 11;
        private const int HtTop = 12;
        private const int HtTopLeft = 13;
        private const int HtTopRight = 14;
        private const int HtBottom = 15;
        private const int HtBottomLeft = 16;
        private const int HtBottomRight = 17;
        private const int WmExitSizeMove = 0x0232;
        private const int WmNcLButtonDblClk = 0x00A3;
        private const int BorderResizeMargin = 6;

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PointWin32
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowPlacement
        {
            public int Length;
            public int Flags;
            public int ShowCmd;
            public PointWin32 PtMinPosition;
            public PointWin32 PtMaxPosition;
            public Rect RcNormalPosition;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowPlacement(IntPtr hWnd, ref WindowPlacement lpwndpl);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetParent(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern nint GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern nint SetWindowLongPtr64(IntPtr hWnd, int nIndex, nint dwNewLong);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetActiveWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AnimateWindow(IntPtr hWnd, int dwTime, int dwFlags);

        public static bool TryWrap(IntPtr targetWindow, string title, out ProtectedWindowState state)
        {
            state = new ProtectedWindowState { Handle = targetWindow };
            if (targetWindow == IntPtr.Zero || !IsWindow(targetWindow) || !WindowEnumerator.IsValidCandidateWindow(targetWindow) ||
                !ProcessSafetyService.Default.CanManageWindow(targetWindow))
                return false;

            var wp = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
            var hasPlacement = GetWindowPlacement(targetWindow, ref wp);
            var isMinimized = IsIconic(targetWindow);

            if (!GetWindowRect(targetWindow, out var rect))
                return false;

            int left = rect.Left;
            int top = rect.Top;
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            // Xử lý đặc biệt khi cửa sổ đang Minimize hoặc bị đẩy ra tọa độ âm (-32000, -32000)
            if (isMinimized || left <= -10000 || top <= -10000 || width <= 1 || height <= 1)
            {
                if (hasPlacement && wp.RcNormalPosition.Right > wp.RcNormalPosition.Left && wp.RcNormalPosition.Bottom > wp.RcNormalPosition.Top)
                {
                    left = wp.RcNormalPosition.Left;
                    top = wp.RcNormalPosition.Top;
                    width = wp.RcNormalPosition.Right - wp.RcNormalPosition.Left;
                    height = wp.RcNormalPosition.Bottom - wp.RcNormalPosition.Top;
                }
                else
                {
                    var primary = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
                    width = Math.Min(1400, (int)(primary.Width * 0.85));
                    height = Math.Min(900, (int)(primary.Height * 0.85));
                    left = primary.Left + Math.Max(20, (primary.Width - width) / 2);
                    top = primary.Top + Math.Max(20, (primary.Height - height) / 2);
                }
            }

            width = Math.Max(320, width);
            height = Math.Max(220, height);

            // Đảm bảo tọa độ khởi tạo luôn nằm gọn trong màn hình hiển thị
            var targetScreen = Screen.FromPoint(new Point(left + 50, top + 50));
            var workArea = targetScreen.WorkingArea;
            if (left + width < workArea.Left + 160) left = workArea.Left + 40;
            if (left > workArea.Right - 160) left = workArea.Right - width - 40;
            if (top < workArea.Top) top = workArea.Top;
            if (top > workArea.Bottom - 100) top = Math.Max(workArea.Top, workArea.Bottom - height - 40);

            var host = new MinimalWrapHostForm(targetWindow, title, left, top, width, height, isMinimized);

            try
            {
                state.OriginalParent = GetParent(targetWindow);
                GetWindowThreadProcessId(targetWindow, out var processId);
                state.ProcessId = unchecked((int)processId);
                state.OriginalStyle = GetWindowLongPtr(targetWindow, GwlStyle);
                state.OriginalExStyle = GetWindowLongPtr(targetWindow, GwlExStyle);
                state.OriginalLeft = left;
                state.OriginalTop = top;
                state.OriginalWidth = width;
                state.OriginalHeight = height;

                host.ShowForProtection();
                if (host.Handle == IntPtr.Zero)
                {
                    host.Close();
                    return false;
                }

                var newStyle = (state.OriginalStyle | WsChild | WsVisible | WsClipSiblings) & ~WsPopup;
                var newExStyle = state.OriginalExStyle & ~WsExAppWindow;

                SetWindowLongPtr(targetWindow, GwlStyle, newStyle);
                SetWindowLongPtr(targetWindow, GwlExStyle, newExStyle);
                SetParent(targetWindow, host.Handle);
                if (GetParent(targetWindow) != host.Handle)
                {
                    SetWindowLongPtr(targetWindow, GwlStyle, state.OriginalStyle);
                    SetWindowLongPtr(targetWindow, GwlExStyle, state.OriginalExStyle);
                    host.Close();
                    return false;
                }
                state.WrapperForm = host;
                state.WrappedByStreamGuard = true;
                host.MarkChildAttached();
                ScrubProtectedShellEntries(host.Handle, targetWindow);
                ShowWindow(targetWindow, SwShow);
                ResizeChildToHost(host, targetWindow, activate: true);
                ScrubProtectedShellEntries(host.Handle, targetWindow);
                FocusChildWindow(targetWindow, host.Handle);
                ApplyCaptureShield(host.Handle);

                state.WrappedByStreamGuard = true;
                state.ShieldedByStreamGuard = false;
                state.WrapperForm = host;
                return true;
            }
            catch
            {
                if (host.IsHandleCreated && IsWindow(targetWindow) && GetParent(targetWindow) == host.Handle)
                {
                    // Keep recovery state rather than destroying a host with an attached application window.
                    state.WrapperForm = host;
                    state.WrappedByStreamGuard = true;
                    return true;
                }
                try { host.Close(); } catch { }
                return false;
            }
        }

        public static bool Restore(ProtectedWindowState state)
        {
            if (!state.WrappedByStreamGuard && !state.ShieldedByStreamGuard)
                return true;
            // Do not close a native parent around an unverified child: closing it can destroy the child window.
            if (state.Handle != IntPtr.Zero && IsWindow(state.Handle) &&
                !ProcessSafetyService.Default.CanManageWindow(state.Handle, state.ProcessId)) return false;

            try
            {
                if (state.Handle != IntPtr.Zero && IsWindow(state.Handle) &&
                    ProcessSafetyService.Default.CanManageWindow(state.Handle, state.ProcessId))
                {
                    SetParent(state.Handle, state.OriginalParent);
                    // Closing a host while its child is still attached can destroy the application window.
                    if (GetAncestor(state.Handle, 1) != (state.OriginalParent == IntPtr.Zero ? GetDesktopWindow() : state.OriginalParent)) return false;
                    SetWindowLongPtr(state.Handle, GwlStyle, state.OriginalStyle);
                    SetWindowLongPtr(state.Handle, GwlExStyle, state.OriginalExStyle);
                    if (GetWindowLongPtr(state.Handle, GwlStyle) != state.OriginalStyle ||
                        GetWindowLongPtr(state.Handle, GwlExStyle) != state.OriginalExStyle) return false;

                    var restoreLeft = state.OriginalLeft;
                    var restoreTop = state.OriginalTop;
                    var restoreWidth = state.OriginalWidth;
                    var restoreHeight = state.OriginalHeight;

                    if (restoreLeft <= -10000 || restoreTop <= -10000 || restoreWidth <= 1 || restoreHeight <= 1)
                    {
                        var primary = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
                        restoreWidth = Math.Min(1400, (int)(primary.Width * 0.85));
                        restoreHeight = Math.Min(900, (int)(primary.Height * 0.85));
                        restoreLeft = primary.Left + Math.Max(20, (primary.Width - restoreWidth) / 2);
                        restoreTop = primary.Top + Math.Max(20, (primary.Height - restoreHeight) / 2);
                    }

                    ShowWindow(state.Handle, 9); // SW_RESTORE
                    ShowWindow(state.Handle, SwShow);
                    int finalW = Math.Max(1, restoreWidth);
                    int finalH = Math.Max(1, restoreHeight);
                    if (!IsZoomed(state.Handle) && finalW > 2 && finalH > 2)
                    {
                        SetWindowPos(
                            state.Handle,
                            HwndTop,
                            restoreLeft,
                            restoreTop,
                            finalW - 1,
                            finalH,
                            SwpNoZOrder | SwpNoActivate | SwpFrameChanged | SwpShowWindow);
                    }
                    if (!SetWindowPos(
                        state.Handle,
                        HwndTop,
                        restoreLeft,
                        restoreTop,
                        finalW,
                        finalH,
                        SwpNoZOrder | SwpNoActivate | SwpFrameChanged | SwpShowWindow)) return false;
                    RedrawWindow(state.Handle, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0100 | 0x0400);
                }
                try { state.WrapperForm?.Close(); } catch { }
                try { state.WrapperWindow?.Close(); } catch { }
                state.WrapperForm = null;
                state.WrapperWindow = null;
                state.WrappedByStreamGuard = false;
                state.ShieldedByStreamGuard = false;
                return true;
            }
            catch { return false; }
        }

        public static bool TryBringProtectedToFront(ProtectedWindowState state)
        {
            if (!ProcessSafetyService.Default.CanManageWindow(state.Handle, state.ProcessId)) return false;
            try
            {
                if (!state.WrappedByStreamGuard || state.WrapperForm is not MinimalWrapHostForm host || host.IsDisposed)
                    return false;

                host.BringProtectedToFront();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void HideWrapper(ProtectedWindowState state)
        {
            if (!ProcessSafetyService.Default.CanManageWindow(state.Handle, state.ProcessId)) return;
            try
            {
                if (state.WrapperForm is MinimalWrapHostForm host && !host.IsDisposed)
                    host.UserHide();
                else
                    state.WrapperForm?.Hide();
            }
            catch
            {
            }
        }

        private static void ResizeChildToHost(MinimalWrapHostForm host, IntPtr child, bool activate = false)
        {
            try
            {
                if (!IsWindow(child) || host.IsDisposed || !host.CanManageChild || GetParent(child) != host.Handle)
                    return;

                if (IsIconic(child))
                    return;

                int margin = host.IsChildZoomed ? 0 : BorderResizeMargin;
                int header = MinimalWrapHostForm.HeaderHeight;
                int targetX = margin;
                int targetY = header;
                int targetWidth = Math.Max(1, host.ClientSize.Width - (margin * 2));
                int targetHeight = Math.Max(1, host.ClientSize.Height - header - (host.IsChildZoomed ? 0 : margin));

                if (GetWindowRect(child, out var childRect))
                {
                    var childClientPt = host.PointToClient(new Point(childRect.Left, childRect.Top));
                    var childWidth = childRect.Right - childRect.Left;
                    var childHeight = childRect.Bottom - childRect.Top;

                    if (childClientPt.X == targetX && childClientPt.Y == targetY &&
                        childWidth == targetWidth && childHeight == targetHeight && !activate)
                        return;
                }

                var flags = SwpFrameChanged | SwpShowWindow;
                if (!activate)
                    flags |= SwpNoActivate;

                SetWindowPos(
                    child,
                    HwndTop,
                    targetX,
                    targetY,
                    targetWidth,
                    targetHeight,
                    flags);
            }
            catch
            {
            }
        }

        private static void FocusChildWindow(IntPtr child, IntPtr host)
        {
            try
            {
                if (child == IntPtr.Zero || !IsWindow(child) || !ProcessSafetyService.Default.CanManageWindow(child))
                    return;

                if (host != IntPtr.Zero && IsWindow(host))
                    SetForegroundWindow(host);

                var currentThread = GetCurrentThreadId();
                var childThread = GetWindowThreadProcessId(child, out _);
                var attached = false;

                if (childThread != 0 && childThread != currentThread)
                    attached = AttachThreadInput(currentThread, childThread, true);

                try
                {
                    ShowWindow(child, SwShow);
                    BringWindowToTop(child);
                    SetActiveWindow(child);
                    SetFocus(child);
                }
                finally
                {
                    if (attached)
                        AttachThreadInput(currentThread, childThread, false);
                }
            }
            catch
            {
            }
        }

        private static nint GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8
                ? GetWindowLongPtr64(hWnd, nIndex)
                : GetWindowLong32(hWnd, nIndex);
        }

        private static nint SetWindowLongPtr(IntPtr hWnd, int nIndex, nint value)
        {
            return IntPtr.Size == 8
                ? SetWindowLongPtr64(hWnd, nIndex, value)
                : SetWindowLong32(hWnd, nIndex, unchecked((int)value));
        }

        private static ITaskbarList? _cachedTaskbar;
        private static readonly object _taskbarLock = new();

        private static void RemoveFromTaskbar(IntPtr hWnd)
        {
            try
            {
                if (hWnd == IntPtr.Zero || !IsWindow(hWnd))
                    return;

                if (_cachedTaskbar == null)
                {
                    lock (_taskbarLock)
                    {
                        if (_cachedTaskbar == null)
                        {
                            var type = Type.GetTypeFromCLSID(TaskbarListClsid);
                            if (type != null && Activator.CreateInstance(type) is ITaskbarList tb)
                            {
                                tb.HrInit();
                                _cachedTaskbar = tb;
                            }
                        }
                    }
                }

                _cachedTaskbar?.DeleteTab(hWnd);
            }
            catch
            {
            }
        }

        private static void ScrubProtectedShellEntries(IntPtr host, IntPtr child)
        {
            if (!ProcessSafetyService.Default.CanManageWindow(child)) return;
            RemoveAppWindowStyle(child);
            RemoveFromTaskbar(child);
            RemoveFromTaskbar(host);
        }

        private static void ApplyCaptureShield(IntPtr host)
        {
            try
            {
                if (host != IntPtr.Zero && IsWindow(host))
                    CaptureProtection.ApplyAndVerify(host);
            }
            catch
            {
            }
        }

        private static void RemoveAppWindowStyle(IntPtr hWnd)
        {
            try
            {
                if (hWnd == IntPtr.Zero || !IsWindow(hWnd))
                    return;

                var exStyle = GetWindowLongPtr(hWnd, GwlExStyle);
                if ((exStyle & WsExAppWindow) == 0)
                {
                    SetWindowPos(
                        hWnd,
                        HwndTop,
                        0,
                        0,
                        0,
                        0,
                        SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
                    return;
                }

                SetWindowLongPtr(hWnd, GwlExStyle, exStyle & ~WsExAppWindow);
                SetWindowPos(
                    hWnd,
                    HwndTop,
                    0,
                    0,
                    0,
                    0,
                    SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
            }
            catch
            {
            }
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("56FDF342-FD6D-11d0-958A-006097C9A090")]
        private interface ITaskbarList
        {
            void HrInit();
            void AddTab(IntPtr hwnd);
            void DeleteTab(IntPtr hwnd);
            void ActivateTab(IntPtr hwnd);
            void SetActiveAlt(IntPtr hwnd);
        }

        private sealed class HiddenShellOwnerWindow : NativeWindow, IWin32Window, IDisposable
        {
            public HiddenShellOwnerWindow()
            {
                var cp = new CreateParams
                {
                    Caption = "Driver Booster Hidden Shell Owner",
                    X = -32000,
                    Y = -32000,
                    Width = 1,
                    Height = 1,
                    Style = WsPopupStyle,
                    ExStyle = WsExToolWindow | WsExNoActivate
                };
                CreateHandle(cp);
            }

            public void Dispose()
            {
                try { DestroyHandle(); } catch { }
            }
        }

        private sealed class MinimalWrapHostForm : Form
        {
            public const int HeaderHeight = 28;
            private readonly IntPtr _child;
            private readonly int _childProcessId;
            public bool CanManageChild => ProcessSafetyService.Default.CanManageWindow(_child, _childProcessId);
            private readonly string _appTitle;
            private readonly HiddenShellOwnerWindow _shellOwner;
            private readonly System.Windows.Forms.Timer _timer;
            private int _normalLeft;
            private int _normalTop;
            private int _normalWidth;
            private int _normalHeight;
            private readonly bool _startMinimized;
            private bool _userHidden = false;
            private int _tickCounter = 0;
            private bool _isHostMaximized = false;
            private bool _isChildZoomed = false;
            private bool _childWasAttached;
            public void MarkChildAttached() => _childWasAttached = true;

            private enum HeaderButton { None, Minimize, Maximize, Close }
            private HeaderButton _hoverButton = HeaderButton.None;

            private Rectangle MinButtonRect => new(Width - 105, 0, 35, HeaderHeight);
            private Rectangle MaxButtonRect => new(Width - 70, 0, 35, HeaderHeight);
            private Rectangle CloseButtonRect => new(Width - 35, 0, 35, HeaderHeight);

            public bool IsChildZoomed => _isHostMaximized || _isChildZoomed;

            public MinimalWrapHostForm(IntPtr child, string title, int left, int top, int width, int height, bool isMinimized = false)
            {
                _child = child;
                GetWindowThreadProcessId(child, out var childProcessId);
                _childProcessId = unchecked((int)childProcessId);
                _appTitle = string.IsNullOrWhiteSpace(title) ? "Driver Booster Shielded App" : title;
                _normalLeft = left;
                _normalTop = top;
                _normalWidth = width;
                _normalHeight = height;
                _startMinimized = isMinimized;

                _shellOwner = new HiddenShellOwnerWindow();
                Text = LocalizationService.Current.Translate($"Driver Booster Protected - {_appTitle}");
                StartPosition = FormStartPosition.Manual;
                FormBorderStyle = FormBorderStyle.None;
                Location = new Point(left, top);
                Size = new Size(width, height);
                MinimumSize = new Size(320, 220);
                BackColor = Color.FromArgb(0x11, 0x16, 0x24);
                ShowInTaskbar = false;
                KeyPreview = false;

                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                DoubleBuffered = true;

                _timer = new System.Windows.Forms.Timer { Interval = 50 };
                _timer.Tick += (_, _) =>
                {
                    _tickCounter++;

                    if (!IsWindow(_child))
                    {
                        ProcessSafetyService.Default.InvalidateCache(_childProcessId);
                        Close();
                        return;
                    }
                    // A recovery journal can detach the child even when the original in-memory link was lost.
                    if (_childWasAttached && GetParent(_child) != Handle)
                    {
                        ProcessSafetyService.Default.InvalidateCache(_childProcessId);
                        Close();
                        return;
                    }
                    if (!CanManageChild)
                    {
                        ProcessSafetyService.Default.InvalidateCache(_childProcessId);
                        return;
                    }

                    // Khi ứng dụng con bị thu nhỏ (Minimize), ẩn ngay khung bọc để không để lại nền đen
                    if (IsIconic(_child))
                    {
                        if (Visible)
                        {
                            Hide();
                        }
                        return;
                    }

                    // Tự động hiện lại khung bọc nếu ứng dụng con không còn thu nhỏ và chưa bị người dùng ẩn thủ công
                    if (!Visible && !_userHidden && !_startMinimized)
                    {
                        Show(_shellOwner);
                        EnsureVisibleOnScreen();
                    }

                    // Đồng bộ khi người dùng bấm Maximize/Restore bên trong ứng dụng con (Antigravity IDE)
                    var childZoomed = IsZoomed(_child);
                    if (childZoomed && !_isChildZoomed)
                    {
                        _isChildZoomed = true;
                        if (!_isHostMaximized)
                        {
                            if (Width >= 320 && Height >= 220)
                            {
                                _normalLeft = Location.X;
                                _normalTop = Location.Y;
                                _normalWidth = Width;
                                _normalHeight = Height;
                            }
                            var screen = Screen.FromHandle(Handle);
                            Bounds = screen.WorkingArea;
                            Invalidate();
                        }
                    }
                    else if (!childZoomed && _isChildZoomed)
                    {
                        _isChildZoomed = false;
                        if (!_isHostMaximized)
                        {
                            Bounds = new Rectangle(_normalLeft, _normalTop, _normalWidth, _normalHeight);
                            EnsureVisibleOnScreen();
                            Invalidate();
                        }
                    }

                    // Đảm bảo cửa sổ không bao giờ bị trôi ra ngoài màn hình (chỉ kiểm tra khi không phóng to)
                    if (!IsChildZoomed && (Location.X <= -10000 || Location.Y <= -10000 || Location.X > 30000 || Location.Y > 30000))
                    {
                        EnsureVisibleOnScreen();
                    }

                    ResizeChildToHost(this, _child);

                    // Chỉ dọn dẹp shell entries và áp dụng shield mỗi 40 ticks (~2 giây/lần) thay vì spam 20 lần/giây
                    if (_tickCounter % 40 == 0)
                    {
                        ScrubProtectedShellEntries(Handle, _child);
                        ApplyCaptureShield(Handle);
                    }
                };
            }

            public void EnsureVisibleOnScreen()
            {
                try
                {
                    if (WindowState == FormWindowState.Minimized || IsChildZoomed)
                        return;

                    var screen = Screen.FromRectangle(new Rectangle(Location, Size));
                    var workArea = screen.WorkingArea;

                    int newX = Location.X;
                    int newY = Location.Y;
                    int newW = Math.Max(320, Width);
                    int newH = Math.Max(220, Height);

                    // Ngang: Giữ ít nhất 160px chiều rộng nằm trong màn hình
                    if (newX + newW < workArea.Left + 160)
                        newX = workArea.Left + 160 - newW;
                    else if (newX > workArea.Right - 160)
                        newX = workArea.Right - 160;

                    // Dọc: Header không được thụt lên quá mép trên, và ít nhất phải thấy được Header trên Taskbar
                    if (newY < workArea.Top)
                        newY = workArea.Top;
                    else if (newY > workArea.Bottom - HeaderHeight)
                        newY = workArea.Bottom - HeaderHeight;

                    if (newX != Location.X || newY != Location.Y || newW != Width || newH != Height)
                    {
                        Location = new Point(newX, newY);
                        Size = new Size(newW, newH);
                        if (!IsChildZoomed)
                        {
                            _normalLeft = newX;
                            _normalTop = newY;
                            _normalWidth = newW;
                            _normalHeight = newH;
                        }
                    }
                }
                catch { }
            }

            public void ToggleZoomed()
            {
                if (!CanManageChild) return;
                if (IsChildZoomed)
                {
                    _isHostMaximized = false;
                    _isChildZoomed = false;
                    if (IsZoomed(_child))
                    {
                        ShowWindow(_child, 9); // SW_RESTORE
                    }

                    if (_normalWidth < 320 || _normalHeight < 220)
                    {
                        var screen = Screen.FromHandle(Handle);
                        _normalWidth = (int)(screen.WorkingArea.Width * 0.85);
                        _normalHeight = (int)(screen.WorkingArea.Height * 0.85);
                        _normalLeft = screen.WorkingArea.Left + (screen.WorkingArea.Width - _normalWidth) / 2;
                        _normalTop = screen.WorkingArea.Top + (screen.WorkingArea.Height - _normalHeight) / 2;
                    }

                    Bounds = new Rectangle(_normalLeft, _normalTop, _normalWidth, _normalHeight);
                    EnsureVisibleOnScreen();
                }
                else
                {
                    if (Width >= 320 && Height >= 220)
                    {
                        _normalLeft = Location.X;
                        _normalTop = Location.Y;
                        _normalWidth = Width;
                        _normalHeight = Height;
                    }

                    _isHostMaximized = true;
                    var screen = Screen.FromHandle(Handle);
                    Bounds = screen.WorkingArea;
                }

                ResizeChildToHost(this, _child);
                Invalidate();
            }

            public void UserHide()
            {
                _userHidden = true;
                try
                {
                    AnimateWindow(Handle, 160, AwBlend | AwHide);
                }
                catch { }
                Hide();
            }

            public void ShowForProtection()
            {
                EnsureVisibleOnScreen();
                if (!_startMinimized)
                {
                    Show(_shellOwner);
                    ScrubProtectedShellEntries(Handle, _child);
                    ApplyCaptureShield(Handle);
                }
                else
                {
                    // Khi app bắt đầu ở trạng thái minimized, giữ wrapper ẩn ban đầu
                    Hide();
                }
            }

            public void BringProtectedToFront()
            {
                if (!CanManageChild) return;
                _userHidden = false;

                // Đưa cửa sổ về vị trí hiển thị hợp lệ trên màn hình
                EnsureVisibleOnScreen();

                if (!Visible)
                {
                    if (!IsHandleCreated)
                        CreateControl();

                    if (_shellOwner.Handle != IntPtr.Zero)
                    {
                        SetWindowLongPtr(Handle, -8 /* GWLP_HWNDPARENT */, _shellOwner.Handle);
                    }

                    // Kích hoạt AnimateWindow khi cửa sổ ĐANG ẨN để hiệu ứng Blend Fade hiển thị trơn tru
                    bool animated = false;
                    try
                    {
                        animated = AnimateWindow(Handle, 180, AwBlend | AwActivate);
                    }
                    catch { }

                    if (!animated)
                    {
                        Show(_shellOwner);
                    }
                    else
                    {
                        Visible = true;
                    }
                }

                if (WindowState == FormWindowState.Minimized)
                    WindowState = FormWindowState.Normal;

                ShowWindow(_child, 9); // SW_RESTORE
                ShowWindow(_child, SwShow);

                EnsureVisibleOnScreen();
                ScrubProtectedShellEntries(Handle, _child);
                ResizeChildToHost(this, _child, activate: true);
                BringToFront();
                Activate();
                ApplyCaptureShield(Handle);
                FocusChildWindow(_child, Handle);
                ScrubProtectedShellEntries(Handle, _child);
                ApplyCaptureShield(Handle);
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                // Tránh flicker do WinForms tự vẽ nền mặc định trước khi OnPaint chạy
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                // 1. Nền tổng thể
                using (var bgBrush = new SolidBrush(BackColor))
                {
                    g.FillRectangle(bgBrush, ClientRectangle);
                }

                // 2. Nền Header Bar
                using (var headerBrush = new SolidBrush(Color.FromArgb(0x0C, 0x10, 0x19)))
                {
                    g.FillRectangle(headerBrush, 0, 0, Width, HeaderHeight);
                }

                // 3. Đường phân cách dưới Header
                using (var linePen = new Pen(Color.FromArgb(0x1E, 0x29, 0x3B), 1f))
                {
                    g.DrawLine(linePen, 0, HeaderHeight - 1, Width, HeaderHeight - 1);
                }

                // 4. Viền cửa sổ nếu không Zoom
                if (!IsChildZoomed)
                {
                    using var borderPen = new Pen(Color.FromArgb(0x33, 0x41, 0x55), 1f);
                    g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
                }

                // 5. Shield Icon
                int iconX = 10;
                int iconY = 7;
                using (var shieldBrush = new SolidBrush(Color.FromArgb(0x10, 0xB9, 0x81)))
                {
                    Point[] shieldPoints =
                    {
                        new Point(iconX + 6, iconY),
                        new Point(iconX + 12, iconY + 3),
                        new Point(iconX + 12, iconY + 8),
                        new Point(iconX + 6, iconY + 14),
                        new Point(iconX, iconY + 8),
                        new Point(iconX, iconY + 3)
                    };
                    g.FillPolygon(shieldBrush, shieldPoints);
                }
                using (var innerPen = new Pen(Color.FromArgb(0x04, 0x2F, 0x2E), 1.2f))
                {
                    g.DrawLine(innerPen, iconX + 3, iconY + 7, iconX + 5, iconY + 10);
                    g.DrawLine(innerPen, iconX + 5, iconY + 10, iconX + 9, iconY + 4);
                }

                // 6. Badge [CLOAKED]
                int badgeX = iconX + 18;
                int badgeY = 5;
                int badgeW = 62;
                int badgeH = 17;
                using (var badgeBg = new SolidBrush(Color.FromArgb(0x06, 0x4E, 0x3B)))
                using (var badgePen = new Pen(Color.FromArgb(0x05, 0x96, 0x69), 1f))
                using (var badgeFont = new Font("Segoe UI", 7.2f, FontStyle.Bold))
                using (var badgeTextBrush = new SolidBrush(Color.FromArgb(0x34, 0xD3, 0x99)))
                using (var path = GetRoundedRect(new Rectangle(badgeX, badgeY, badgeW, badgeH), 3))
                {
                    g.FillPath(badgeBg, path);
                    g.DrawPath(badgePen, path);
                    using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("CLOAKED", badgeFont, badgeTextBrush, new RectangleF(badgeX, badgeY, badgeW, badgeH), sf);
                }

                // 7. Tên ứng dụng
                int titleX = badgeX + badgeW + 8;
                int titleMaxW = Width - 110 - titleX;
                if (titleMaxW > 30)
                {
                    using var titleFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);
                    using var titleBrush = new SolidBrush(Color.FromArgb(0x94, 0xA3, 0xB8));
                    using var sf = new StringFormat
                    {
                        Trimming = StringTrimming.EllipsisCharacter,
                        FormatFlags = StringFormatFlags.NoWrap,
                        LineAlignment = StringAlignment.Center
                    };
                    g.DrawString(_appTitle, titleFont, titleBrush, new RectangleF(titleX, 0, titleMaxW, HeaderHeight), sf);
                }

                // 8. 3 Nút bấm điều khiển (Minimize, Maximize/Restore, Close)
                DrawHeaderButton(g, MinButtonRect, HeaderButton.Minimize, _hoverButton == HeaderButton.Minimize);
                DrawHeaderButton(g, MaxButtonRect, HeaderButton.Maximize, _hoverButton == HeaderButton.Maximize);
                DrawHeaderButton(g, CloseButtonRect, HeaderButton.Close, _hoverButton == HeaderButton.Close);
            }

            private static System.Drawing.Drawing2D.GraphicsPath GetRoundedRect(Rectangle bounds, int radius)
            {
                int diameter = radius * 2;
                var path = new System.Drawing.Drawing2D.GraphicsPath();
                path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
                path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
                path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
                path.CloseFigure();
                return path;
            }

            private void DrawHeaderButton(Graphics g, Rectangle rect, HeaderButton btnType, bool isHovered)
            {
                if (isHovered)
                {
                    Color hoverColor = (btnType == HeaderButton.Close)
                        ? Color.FromArgb(0xE1, 0x1D, 0x48)
                        : Color.FromArgb(0x1E, 0x29, 0x3B);
                    using var b = new SolidBrush(hoverColor);
                    g.FillRectangle(b, rect);
                }

                Color iconColor = isHovered && (btnType == HeaderButton.Close)
                    ? Color.White
                    : Color.FromArgb(0x94, 0xA3, 0xB8);

                using var iconPen = new Pen(iconColor, 1.2f);
                int cx = rect.X + rect.Width / 2;
                int cy = rect.Y + rect.Height / 2;

                switch (btnType)
                {
                    case HeaderButton.Minimize:
                        g.DrawLine(iconPen, cx - 5, cy, cx + 5, cy);
                        break;

                    case HeaderButton.Maximize:
                        if (IsChildZoomed)
                        {
                            // Biểu tượng Restore: 2 ô vuông lồng nhau sắc nét
                            g.DrawLine(iconPen, cx - 2, cy - 5, cx + 5, cy - 5);
                            g.DrawLine(iconPen, cx + 5, cy - 5, cx + 5, cy + 2);
                            g.DrawRectangle(iconPen, cx - 5, cy - 2, 7, 7);
                        }
                        else
                        {
                            // Biểu tượng Maximize: 1 ô vuông đơn
                            g.DrawRectangle(iconPen, cx - 5, cy - 5, 9, 9);
                        }
                        break;

                    case HeaderButton.Close:
                        g.DrawLine(iconPen, cx - 4, cy - 4, cx + 4, cy + 4);
                        g.DrawLine(iconPen, cx + 4, cy - 4, cx - 4, cy + 4);
                        break;
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);

                var oldHover = _hoverButton;
                if (CloseButtonRect.Contains(e.Location))
                    _hoverButton = HeaderButton.Close;
                else if (MaxButtonRect.Contains(e.Location))
                    _hoverButton = HeaderButton.Maximize;
                else if (MinButtonRect.Contains(e.Location))
                    _hoverButton = HeaderButton.Minimize;
                else
                    _hoverButton = HeaderButton.None;

                if (oldHover != _hoverButton)
                {
                    Invalidate(new Rectangle(Width - 110, 0, 110, HeaderHeight));
                }
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                if (_hoverButton != HeaderButton.None)
                {
                    _hoverButton = HeaderButton.None;
                    Invalidate(new Rectangle(Width - 110, 0, 110, HeaderHeight));
                }
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (!CanManageChild) return;
                if (e.Button == MouseButtons.Left)
                {
                    if (CloseButtonRect.Contains(e.Location))
                    {
                        UserHide();
                        return;
                    }
                    if (MaxButtonRect.Contains(e.Location))
                    {
                        ToggleZoomed();
                        return;
                    }
                    if (MinButtonRect.Contains(e.Location))
                    {
                        ShowWindow(_child, 6 /* SW_MINIMIZE */);
                        Hide();
                        return;
                    }
                }
                base.OnMouseDown(e);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WmNcHitTest)
                {
                    short screenX = unchecked((short)(long)m.LParam);
                    short screenY = unchecked((short)((long)m.LParam >> 16));
                    Point clientPt = PointToClient(new Point(screenX, screenY));

                    int w = ClientSize.Width;
                    int h = ClientSize.Height;
                    int margin = BorderResizeMargin;

                    // 1. Ưu tiên hàng đầu: Vùng 3 nút bấm Header Bar nhận HTCLIENT để click chính xác tuyệt đối
                    if (clientPt.Y >= 0 && clientPt.Y < HeaderHeight && clientPt.X >= w - 105)
                    {
                        m.Result = (IntPtr)HtClient;
                        return;
                    }

                    // 2. Nếu chưa phóng to: Xử lý 4 góc và 4 cạnh để co giãn
                    if (!IsChildZoomed)
                    {
                        // 4 Góc
                        if (clientPt.X < margin * 2 && clientPt.Y < margin * 2)
                        {
                            m.Result = (IntPtr)HtTopLeft;
                            return;
                        }
                        if (clientPt.X > w - margin * 2 && clientPt.Y < margin * 2)
                        {
                            m.Result = (IntPtr)HtTopRight;
                            return;
                        }
                        if (clientPt.X < margin * 2 && clientPt.Y > h - margin * 2)
                        {
                            m.Result = (IntPtr)HtBottomLeft;
                            return;
                        }
                        if (clientPt.X > w - margin * 2 && clientPt.Y > h - margin * 2)
                        {
                            m.Result = (IntPtr)HtBottomRight;
                            return;
                        }

                        // 3 Cạnh viền dưới và hai bên
                        if (clientPt.Y > h - margin)
                        {
                            m.Result = (IntPtr)HtBottom;
                            return;
                        }
                        if (clientPt.X < margin)
                        {
                            m.Result = (IntPtr)HtLeft;
                            return;
                        }
                        if (clientPt.X > w - margin)
                        {
                            m.Result = (IntPtr)HtRight;
                            return;
                        }

                        // Cạnh viền trên cùng (chỉ áp dụng ở phần không chứa nút bấm)
                        if (clientPt.Y < margin)
                        {
                            m.Result = (IntPtr)HtTop;
                            return;
                        }
                    }

                    // 3. Toàn bộ phần còn lại của Header Bar là vùng kéo di chuyển cửa sổ (HTCAPTION)
                    if (clientPt.Y >= 0 && clientPt.Y < HeaderHeight)
                    {
                        m.Result = (IntPtr)HtCaption;
                        return;
                    }
                }
                else if (m.Msg == WmNcLButtonDblClk)
                {
                    short screenX = unchecked((short)(long)m.LParam);
                    short screenY = unchecked((short)((long)m.LParam >> 16));
                    Point clientPt = PointToClient(new Point(screenX, screenY));

                    if (clientPt.Y < HeaderHeight && clientPt.X < ClientSize.Width - 105)
                    {
                        ToggleZoomed();
                        m.Result = IntPtr.Zero;
                        return;
                    }
                }
                else if (m.Msg == WmExitSizeMove)
                {
                    EnsureVisibleOnScreen();
                }

                base.WndProc(ref m);
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                ScrubProtectedShellEntries(Handle, _child);
                ApplyCaptureShield(Handle);
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                EnsureVisibleOnScreen();
                ScrubProtectedShellEntries(Handle, _child);
                ResizeChildToHost(this, _child, activate: true);
                ApplyCaptureShield(Handle);
                FocusChildWindow(_child, Handle);
                _timer.Start();
            }

            protected override void OnActivated(EventArgs e)
            {
                base.OnActivated(e);
                ScrubProtectedShellEntries(Handle, _child);
                ApplyCaptureShield(Handle);
                FocusChildWindow(_child, Handle);
            }

            protected override void OnMove(EventArgs e)
            {
                base.OnMove(e);
                if (!IsChildZoomed && WindowState == FormWindowState.Normal && Location.X > -10000 && Location.Y > -10000)
                {
                    _normalLeft = Location.X;
                    _normalTop = Location.Y;
                }
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                if (!IsChildZoomed && WindowState == FormWindowState.Normal && Width >= 320 && Height >= 220)
                {
                    _normalWidth = Width;
                    _normalHeight = Height;
                }
                ScrubProtectedShellEntries(Handle, _child);
                ResizeChildToHost(this, _child);
                ApplyCaptureShield(Handle);
                Invalidate();
            }

            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                ProcessSafetyService.Default.InvalidateCache(_childProcessId);
                _timer.Stop();
                _timer.Dispose();
                _shellOwner.Dispose();
                base.OnFormClosed(e);
            }
        }
    }
}
