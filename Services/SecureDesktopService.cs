using System;
using StreamGuard.Localization;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace StreamGuard.Services
{
    public sealed class SecureDesktopService : IDisposable
    {
        private const uint DesktopReadObjects = 0x0001;
        private const uint DesktopCreateWindow = 0x0002;
        private const uint DesktopCreateMenu = 0x0004;
        private const uint DesktopHookControl = 0x0008;
        private const uint DesktopJournalRecord = 0x0010;
        private const uint DesktopJournalPlayback = 0x0020;
        private const uint DesktopEnumerate = 0x0040;
        private const uint DesktopWriteObjects = 0x0080;
        private const uint DesktopSwitchDesktop = 0x0100;
        private const uint DesktopAllAccess =
            DesktopReadObjects |
            DesktopCreateWindow |
            DesktopCreateMenu |
            DesktopHookControl |
            DesktopJournalRecord |
            DesktopJournalPlayback |
            DesktopEnumerate |
            DesktopWriteObjects |
            DesktopSwitchDesktop;
        private const uint CreateUnicodeEnvironment = 0x00000400;

        private readonly object _gate = new();
        private IntPtr _defaultDesktop = IntPtr.Zero;
        private IntPtr _secureDesktop = IntPtr.Zero;
        private Thread? _monitorThread;
        private volatile bool _active;
        private volatile bool _disposed;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public uint cb;
            public string? lpReserved;
            public string? lpDesktop;
            public string? lpTitle;
            public uint dwX;
            public uint dwY;
            public uint dwXSize;
            public uint dwYSize;
            public uint dwXCountChars;
            public uint dwYCountChars;
            public uint dwFillAttribute;
            public uint dwFlags;
            public ushort wShowWindow;
            public ushort cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public uint dwProcessId;
            public uint dwThreadId;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateDesktop(
            string lpszDesktop,
            IntPtr lpszDevice,
            IntPtr pDevmode,
            uint dwFlags,
            uint dwDesiredAccess,
            IntPtr lpsa);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenDesktop(
            string lpszDesktop,
            uint dwFlags,
            bool fInherit,
            uint dwDesiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SwitchDesktop(IntPtr hDesktop);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseDesktop(IntPtr hDesktop);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcess(
            string? lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string? lpCurrentDirectory,
            ref StartupInfo lpStartupInfo,
            out ProcessInformation lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        public bool IsActive => _active;

        public bool Launch(string executablePath, out string message)
        {
            lock (_gate)
            {
                message = "";
                if (_disposed)
                {
                    message = "Secure desktop service is disposed";
                    return false;
                }

                if (_active)
                {
                    message = "Secure desktop is already active";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                {
                    message = "Executable path is missing. Drag a .exe into Driver Booster first.";
                    return false;
                }

                if (ProcessSafetyService.IsProtectedIdentity(Path.GetFileNameWithoutExtension(executablePath), executablePath))
                {
                    message = "System or unverified processes cannot be managed.";
                    return false;
                }

                var desktopName = $"StreamGuardSecure-{Environment.ProcessId}-{DateTime.UtcNow.Ticks}";
                _defaultDesktop = OpenDesktop("Default", 0, false, DesktopSwitchDesktop | DesktopReadObjects | DesktopWriteObjects);
                if (_defaultDesktop == IntPtr.Zero)
                {
                    message = $"OpenDesktop(Default) failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}";
                    return false;
                }

                _secureDesktop = CreateDesktop(desktopName, IntPtr.Zero, IntPtr.Zero, 0, DesktopAllAccess, IntPtr.Zero);
                if (_secureDesktop == IntPtr.Zero)
                {
                    message = $"CreateDesktop failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}";
                    CleanupHandles();
                    return false;
                }

                if (!StartProcessOnDesktop(executablePath, "", desktopName, Path.GetDirectoryName(executablePath), out var protectedPid, out message))
                {
                    CleanupHandles();
                    return false;
                }

                _active = true;
                _monitorThread = new Thread(() => MonitorSecureDesktop(protectedPid))
                {
                    IsBackground = true,
                    Name = "Driver Booster secure desktop monitor"
                };
                _monitorThread.Start();

                if (!SwitchDesktop(_secureDesktop))
                {
                    message = $"SwitchDesktop failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}";
                    TryKillProcess(protectedPid);
                    ReturnToDefault();
                    return false;
                }

                message = "Secure desktop active. Close the protected app to return to normal desktop.";
                return true;
            }
        }

        public void ReturnToDefault()
        {
            lock (_gate)
            {
                if (_defaultDesktop != IntPtr.Zero)
                    SwitchDesktop(_defaultDesktop);

                _active = false;
                CleanupHandles();
            }
        }

        public void Dispose()
        {
            _disposed = true;
            ReturnToDefault();
        }

        public static bool TryRunReturnHelper(string[] args)
        {
            if (args.Length < 2 || !args[0].Equals("--secure-return", StringComparison.OrdinalIgnoreCase))
                return false;

            RunReturnHelper(args[1]);
            return true;
        }

        private static void RunReturnHelper(string returnEventName)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var form = new Form
            {
                Text = LocalizationService.Current.Translate("Driver Booster Secure Desktop"),
                StartPosition = FormStartPosition.Manual,
                TopMost = true,
                Width = 360,
                Height = 150,
                Left = 24,
                Top = 24,
                BackColor = System.Drawing.Color.FromArgb(8, 8, 10),
                ForeColor = System.Drawing.Color.White,
                FormBorderStyle = FormBorderStyle.FixedToolWindow
            };

            var label = new Label
            {
                Text = LocalizationService.Current.Translate("Secure Desktop is active.\r\nClose your protected app or click Return."),
                AutoSize = false,
                Left = 16,
                Top = 14,
                Width = 320,
                Height = 46,
                ForeColor = System.Drawing.Color.White
            };
            var button = new Button
            {
                Text = LocalizationService.Current.Translate("Return to normal desktop"),
                Left = 16,
                Top = 72,
                Width = 220,
                Height = 34
            };
            button.Click += (_, _) =>
            {
                SignalReturn(returnEventName);
                SwitchToDefaultDesktopFromHelper();
                form.Close();
            };

            form.Controls.Add(label);
            form.Controls.Add(button);
            form.FormClosed += (_, _) =>
            {
                SignalReturn(returnEventName);
                SwitchToDefaultDesktopFromHelper();
            };
            Application.Run(form);
        }

        private static void SignalReturn(string returnEventName)
        {
            try
            {
                using var ev = EventWaitHandle.OpenExisting(returnEventName);
                ev.Set();
            }
            catch
            {
            }
        }

        private static void SwitchToDefaultDesktopFromHelper()
        {
            var desktop = IntPtr.Zero;
            try
            {
                desktop = OpenDesktop("Default", 0, false, DesktopSwitchDesktop);
                if (desktop != IntPtr.Zero)
                    SwitchDesktop(desktop);
            }
            catch
            {
            }
            finally
            {
                if (desktop != IntPtr.Zero)
                    CloseDesktop(desktop);
            }
        }

        private static bool StartProcessOnDesktop(
            string executablePath,
            string arguments,
            string desktopName,
            string? workingDirectory,
            out int processId,
            out string message)
        {
            processId = 0;
            message = "";
            var si = new StartupInfo
            {
                cb = (uint)Marshal.SizeOf<StartupInfo>(),
                lpDesktop = desktopName,
                lpTitle = Path.GetFileName(executablePath)
            };

            var commandLine = string.IsNullOrWhiteSpace(arguments)
                ? Quote(executablePath)
                : $"{Quote(executablePath)} {arguments}";

            if (!CreateProcess(
                    null,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    CreateUnicodeEnvironment,
                    IntPtr.Zero,
                    workingDirectory,
                    ref si,
                    out var pi))
            {
                message = $"CreateProcess failed for {Path.GetFileName(executablePath)}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}";
                return false;
            }

            processId = unchecked((int)pi.dwProcessId);
            if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
            if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
            return true;
        }

        private void MonitorSecureDesktop(int protectedPid)
        {
            try
            {
                using var process = Process.GetProcessById(protectedPid);
                while (_active && !_disposed)
                {
                    if (process.HasExited)
                        break;

                    Thread.Sleep(250);
                }
            }
            catch
            {
            }

            ReturnToDefault();
        }

        private void CleanupHandles()
        {
            if (_secureDesktop != IntPtr.Zero)
            {
                try { CloseDesktop(_secureDesktop); } catch { }
                _secureDesktop = IntPtr.Zero;
            }

            if (_defaultDesktop != IntPtr.Zero)
            {
                try { CloseDesktop(_defaultDesktop); } catch { }
                _defaultDesktop = IntPtr.Zero;
            }
        }

        private static void TryKillProcess(int processId)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (!ProcessSafetyService.Default.CanManageProcess(processId)) return;
                process.Kill(entireProcessTree: false);
            }
            catch
            {
            }
        }

        private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
    }
}
