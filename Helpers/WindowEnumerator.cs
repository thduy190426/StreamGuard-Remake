using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;
using StreamGuard.Services;

namespace StreamGuard.Helpers
{
    public class WindowInfo : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isInShieldList;
        public IntPtr Handle { get; set; }
        public string Title { get; set; } = "";
        public string ProcessName { get; set; } = "";
        public int ProcessId { get; set; }
        public string ExecutablePath { get; set; } = "";
        public bool IsInShieldList
        {
            get => _isInShieldList;
            set
            {
                if (_isInShieldList == value) return;
                _isInShieldList = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsInShieldList)));
            }
        }
        public System.Windows.Media.ImageSource? Icon => IconHelper.GetIconForApp(ExecutablePath, ProcessName, Handle);
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    public static class WindowEnumerator
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
        [DllImport("user32.dll")] static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        static extern nint GetWindowLongPtr64(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        static extern int GetWindowLong32(IntPtr hWnd, int nIndex);
        [DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const nint WS_CHILD = 0x40000000;
        private const nint WS_EX_TOOLWINDOW = 0x00000080;
        private const nint WS_EX_APPWINDOW = 0x00040000;
        private const int DWMWA_CLOAKED = 14;
        private const uint GW_OWNER = 4;

        private static readonly HashSet<string> ExcludedClasses = new(StringComparer.OrdinalIgnoreCase)
        {
            "tooltips_class32",
            "MicrosoftWindowsTooltip",
            "System.Windows.Controls.ToolTip",
            "SysShadow",
            "DropShadow",
            "#32768",
            "ComboLBox",
            "PopUpHost",
            "Popup",
            "IME",
            "MSCTFIME UI",
            "Chrome_RenderWidgetHostHWND",
            "Intermediate D3D Window"
        };

        private static readonly HashSet<string> ExcludedProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "TextInputHost", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost",
            "LockApp", "SystemSettings"
        };

        private static nint GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
        }

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        public static bool IsValidCandidateWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || !IsWindow(hWnd)) return false;
            if (!ProcessSafetyService.Default.CanManageWindow(hWnd)) return false;
            if (hWnd == GetShellWindow()) return false;

            // 1. Minimized windows are allowed even if not visible in standard way, but otherwise must be visible
            var isIconic = IsIconic(hWnd);
            if (!IsWindowVisible(hWnd) && !isIconic) return false;

            // 2. Length check: Must have a title
            int len = GetWindowTextLength(hWnd);
            if (len == 0) return false;

            // 3. Class name exclusion check (tooltips, shadows, menus, auxiliary widgets)
            var sbClass = new StringBuilder(256);
            GetClassName(hWnd, sbClass, sbClass.Capacity);
            string className = sbClass.ToString();
            if (ExcludedClasses.Contains(className)) return false;
            if (className.StartsWith("tooltips_class", StringComparison.OrdinalIgnoreCase) ||
                className.IndexOf("Tooltip", StringComparison.OrdinalIgnoreCase) >= 0 ||
                className.IndexOf("DropShadow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                className.IndexOf("SysShadow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                className.Equals("Chrome_RenderWidgetHostHWND", StringComparison.OrdinalIgnoreCase) ||
                className.Equals("Intermediate D3D Window", StringComparison.OrdinalIgnoreCase))
                return false;

            // 4. Style & ExStyle checks
            nint style = GetWindowLongPtr(hWnd, GWL_STYLE);
            nint exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE);

            // Skip child windows
            if ((style & WS_CHILD) != 0) return false;

            // Skip tool windows unless explicitly marked as app window
            if ((exStyle & WS_EX_TOOLWINDOW) != 0 && (exStyle & WS_EX_APPWINDOW) == 0)
                return false;

            // Skip owned windows (popups, dialogs, dropdowns, tooltips) unless marked as app window
            IntPtr owner = GetWindow(hWnd, GW_OWNER);
            if (owner != IntPtr.Zero && (exStyle & WS_EX_APPWINDOW) == 0)
                return false;

            // 5. Dimension check (ignore tiny windows < 80x40 unless minimized)
            if (!isIconic && GetWindowRect(hWnd, out var rect))
            {
                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;
                if (width < 80 || height < 40) return false;
            }

            // 6. Cloaked check (modern Windows app shells)
            if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return false;

            return true;
        }

        public static List<WindowInfo> GetOpenWindows()
        {
            var result = new List<WindowInfo>();

            EnumWindows((hWnd, _) =>
            {
                if (!IsValidCandidateWindow(hWnd)) return true;

                int len = GetWindowTextLength(hWnd);
                var sb = new StringBuilder(len + 1);
                GetWindowText(hWnd, sb, sb.Capacity);
                string title = sb.ToString();
                if (string.IsNullOrWhiteSpace(title)) return true;

                GetWindowThreadProcessId(hWnd, out uint pid);
                string procName = "";
                string exePath = "";
                try
                {
                    using var proc = Process.GetProcessById((int)pid);
                    procName = proc.ProcessName;
                    if (ExcludedProcesses.Contains(procName)) return true;
                    try { exePath = proc.MainModule?.FileName ?? ""; } catch { }
                }
                catch { }

                if (string.IsNullOrWhiteSpace(procName)) return true;

                result.Add(new WindowInfo
                {
                    Handle = hWnd,
                    Title = title,
                    ProcessName = procName,
                    ProcessId = (int)pid,
                    ExecutablePath = exePath
                });
                return true;
            }, IntPtr.Zero);

            return result;
        }

        /// <summary>
        /// Tìm tất cả cửa sổ của process kể cả hidden/background.
        /// </summary>
        public static List<WindowInfo> FindAllIncludingHiddenByProcessName(string processName)
        {
            var result = new List<WindowInfo>();
            if (ProcessSafetyService.IsProtectedIdentity(processName)) return result;
            var seenHandles = new HashSet<IntPtr>();

            // 1. Visible windows trước (qua EnumWindows)
            foreach (var w in GetOpenWindows())
            {
                if (w.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase)
                    && seenHandles.Add(w.Handle))
                {
                    result.Add(w);
                }
            }

            // 2. Fallback: Process.MainWindowHandle — bắt được hidden/tray apps
            try
            {
                foreach (var proc in Process.GetProcessesByName(processName))
                {
                    using (proc)
                    {
                        try
                        {
                            var hwnd = proc.MainWindowHandle;
                            if (hwnd != IntPtr.Zero && IsWindow(hwnd) && IsValidCandidateWindow(hwnd) && seenHandles.Add(hwnd))
                            {
                                var title = proc.MainWindowTitle;
                                var exePath = "";
                                try { exePath = proc.MainModule?.FileName ?? ""; } catch { }
                                result.Add(new WindowInfo
                                {
                                    Handle = hwnd,
                                    Title = string.IsNullOrWhiteSpace(title) ? processName : title,
                                    ProcessName = processName,
                                    ProcessId = proc.Id,
                                    ExecutablePath = exePath
                                });
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return result;
        }

        public static WindowInfo? FindByProcessName(string processName)
        {
            foreach (var w in GetOpenWindows())
                if (w.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                    return w;
            return null;
        }

        public static List<WindowInfo> FindAllByProcessName(string processName)
        {
            var result = new List<WindowInfo>();
            foreach (var w in GetOpenWindows())
            {
                if (w.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                    result.Add(w);
            }

            return result;
        }
    }
}
