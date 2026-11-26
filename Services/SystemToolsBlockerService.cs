using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace StreamGuard.Services
{
    public sealed record SystemToolsBlockerStatus(
        bool IsEnabled,
        bool PoliciesApplied,
        int ClosedWindowsCount,
        string Message);

    public sealed class SystemToolsBlockerService : IDisposable
    {
        public const string SystemPoliciesSubKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
        public const string WindowsPoliciesSubKey = @"Software\Policies\Microsoft\Windows\System";
        public const string GpeditMmcSnapInSubKey = @"Software\Policies\Microsoft\MMC\{8FC0B734-A0E1-11D1-A7D3-0000F87571E3}";
        public const string ExplorerPoliciesSubKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";

        public const string DisableTaskMgrValueName = "DisableTaskMgr";
        public const string DisableRegistryToolsValueName = "DisableRegistryTools";
        public const string DisableCmdValueName = "DisableCMD";
        public const string RestrictRunValueName = "Restrict_Run";
        public const string EmergencyRestoreScriptFileName = "Emergency_Restore_Tools.bat";

        private const uint WM_CLOSE = 0x0010;
        private const int WatchIntervalMs = 450;

        private readonly object _gate = new();
        private readonly System.Timers.Timer _watchTimer;
        private readonly Func<bool, bool>? _customPolicyHandler;
        private volatile bool _isEnabled;
        private volatile bool _disposed;
        private bool _policiesAppliedByInstance;
        private int _totalClosedWindows;

        public bool IsEnabled => _isEnabled;
        public int TotalClosedWindows => _totalClosedWindows;

        public SystemToolsBlockerService(Func<bool, bool>? customPolicyHandler = null)
        {
            _customPolicyHandler = customPolicyHandler;
            _watchTimer = new System.Timers.Timer(WatchIntervalMs) { AutoReset = true };
            _watchTimer.Elapsed += (_, _) => EnforceActiveWindowBlocks();
        }

        public SystemToolsBlockerStatus SetEnabled(bool enabled)
        {
            lock (_gate)
            {
                if (_disposed)
                    return new SystemToolsBlockerStatus(false, false, _totalClosedWindows, "System tools blocker stopped");

                _isEnabled = enabled;
                if (enabled)
                {
                    var applied = ApplyPolicies(enable: true);
                    _policiesAppliedByInstance = true;
                    var closedNow = EnforceActiveWindowBlocksCore();
                    _watchTimer.Start();
                    return new SystemToolsBlockerStatus(
                        true,
                        applied,
                        _totalClosedWindows,
                        applied
                            ? "Blocked Task Manager, Registry Editor, Command Prompt, and Group Policy Editor"
                            : "Active window blocker enabled (some registry policies could not be written)");
                }
                else
                {
                    _watchTimer.Stop();
                    var restored = ApplyPolicies(enable: false);
                    _policiesAppliedByInstance = false;
                    return new SystemToolsBlockerStatus(
                        false,
                        restored,
                        _totalClosedWindows,
                        "Restored access to Task Manager, Registry Editor, Command Prompt, and Group Policy Editor");
                }
            }
        }

        public static bool ForceUnlockAllTools()
        {
            ForceUnlockToolsCore(
                Registry.CurrentUser,
                SystemPoliciesSubKey,
                WindowsPoliciesSubKey,
                GpeditMmcSnapInSubKey,
                ExplorerPoliciesSubKey,
                Registry.LocalMachine);

            return !IsAnyToolBlocked();
        }

        internal static bool ForceUnlockToolsCore(
            RegistryKey hkcuRoot,
            string sysKeyPath,
            string winKeyPath,
            string mmcKeyPath,
            string expKeyPath,
            RegistryKey? hklmRoot = null)
        {
            // 1. HKCU System Policies (DisableTaskMgr, DisableRegistryTools, DisableCMD)
            try
            {
                using var sysKey = hkcuRoot.OpenSubKey(sysKeyPath, writable: true);
                if (sysKey != null)
                {
                    sysKey.DeleteValue(DisableTaskMgrValueName, throwOnMissingValue: false);
                    sysKey.DeleteValue(DisableRegistryToolsValueName, throwOnMissingValue: false);
                    sysKey.DeleteValue(DisableCmdValueName, throwOnMissingValue: false);
                }
            }
            catch
            {
            }

            // 2. HKCU Windows System Policies (DisableCMD, DisableTaskMgr, DisableRegistryTools)
            try
            {
                using var winKey = hkcuRoot.OpenSubKey(winKeyPath, writable: true);
                if (winKey != null)
                {
                    winKey.DeleteValue(DisableCmdValueName, throwOnMissingValue: false);
                    winKey.DeleteValue(DisableTaskMgrValueName, throwOnMissingValue: false);
                    winKey.DeleteValue(DisableRegistryToolsValueName, throwOnMissingValue: false);
                }
            }
            catch
            {
            }

            // 3. HKCU MMC Policies (Gpedit MMC snap-in Restrict_Run & RestrictRun)
            try
            {
                using var mmcKey = hkcuRoot.OpenSubKey(mmcKeyPath, writable: true);
                if (mmcKey != null)
                {
                    mmcKey.DeleteValue(RestrictRunValueName, throwOnMissingValue: false);
                    mmcKey.DeleteValue("RestrictRun", throwOnMissingValue: false);
                }
            }
            catch
            {
            }

            // 4. HKCU Explorer Policies (DisableTaskMgr, RestrictRun, Restrict_Run)
            try
            {
                using var expKey = hkcuRoot.OpenSubKey(expKeyPath, writable: true);
                if (expKey != null)
                {
                    expKey.DeleteValue(DisableTaskMgrValueName, throwOnMissingValue: false);
                    expKey.DeleteValue(RestrictRunValueName, throwOnMissingValue: false);
                    expKey.DeleteValue("RestrictRun", throwOnMissingValue: false);
                }
            }
            catch
            {
            }

            // 5. Best-effort HKLM cleanup
            if (hklmRoot != null)
            {
                TryCleanHklmPolicies(sysKeyPath, winKeyPath, mmcKeyPath, expKeyPath);
            }

            return true;
        }

        private static void TryCleanHklmPolicies(
            string sysKeyPath = SystemPoliciesSubKey,
            string winKeyPath = WindowsPoliciesSubKey,
            string mmcKeyPath = GpeditMmcSnapInSubKey,
            string expKeyPath = ExplorerPoliciesSubKey)
        {
            var views = new[] { RegistryView.Default, RegistryView.Registry64 };
            foreach (var view in views)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);

                    try
                    {
                        using var sysKey = baseKey.OpenSubKey(sysKeyPath, writable: true);
                        if (sysKey != null)
                        {
                            sysKey.DeleteValue(DisableTaskMgrValueName, throwOnMissingValue: false);
                            sysKey.DeleteValue(DisableRegistryToolsValueName, throwOnMissingValue: false);
                            sysKey.DeleteValue(DisableCmdValueName, throwOnMissingValue: false);
                        }
                    }
                    catch { }

                    try
                    {
                        using var winKey = baseKey.OpenSubKey(winKeyPath, writable: true);
                        if (winKey != null)
                        {
                            winKey.DeleteValue(DisableCmdValueName, throwOnMissingValue: false);
                            winKey.DeleteValue(DisableTaskMgrValueName, throwOnMissingValue: false);
                            winKey.DeleteValue(DisableRegistryToolsValueName, throwOnMissingValue: false);
                        }
                    }
                    catch { }

                    try
                    {
                        using var mmcKey = baseKey.OpenSubKey(mmcKeyPath, writable: true);
                        if (mmcKey != null)
                        {
                            mmcKey.DeleteValue(RestrictRunValueName, throwOnMissingValue: false);
                            mmcKey.DeleteValue("RestrictRun", throwOnMissingValue: false);
                        }
                    }
                    catch { }

                    try
                    {
                        using var expKey = baseKey.OpenSubKey(expKeyPath, writable: true);
                        if (expKey != null)
                        {
                            expKey.DeleteValue(DisableTaskMgrValueName, throwOnMissingValue: false);
                            expKey.DeleteValue(RestrictRunValueName, throwOnMissingValue: false);
                            expKey.DeleteValue("RestrictRun", throwOnMissingValue: false);
                        }
                    }
                    catch { }
                }
                catch { }
            }
        }

        internal static bool IsBlockedValue(object? val)
        {
            if (val == null) return false;
            if (val is int i) return i != 0;
            if (val is long l) return l != 0;
            if (val is string s && int.TryParse(s, out var parsed)) return parsed != 0;
            if (val is byte[] bytes)
            {
                foreach (var b in bytes)
                    if (b != 0) return true;
                return false;
            }
            try
            {
                return Convert.ToInt64(val) != 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsAnyToolBlocked()
        {
            return IsAnyToolBlockedCore(
                Registry.CurrentUser,
                SystemPoliciesSubKey,
                WindowsPoliciesSubKey,
                GpeditMmcSnapInSubKey,
                ExplorerPoliciesSubKey);
        }

        internal static bool IsAnyToolBlockedCore(
            RegistryKey hkcuRoot,
            string sysKeyPath,
            string winKeyPath,
            string mmcKeyPath,
            string expKeyPath)
        {
            try
            {
                using var sysKey = hkcuRoot.OpenSubKey(sysKeyPath, writable: false);
                if (sysKey != null)
                {
                    if (IsBlockedValue(sysKey.GetValue(DisableTaskMgrValueName))) return true;
                    if (IsBlockedValue(sysKey.GetValue(DisableRegistryToolsValueName))) return true;
                    if (IsBlockedValue(sysKey.GetValue(DisableCmdValueName))) return true;
                }
            }
            catch { }

            try
            {
                using var winKey = hkcuRoot.OpenSubKey(winKeyPath, writable: false);
                if (winKey != null)
                {
                    if (IsBlockedValue(winKey.GetValue(DisableCmdValueName))) return true;
                    if (IsBlockedValue(winKey.GetValue(DisableTaskMgrValueName))) return true;
                    if (IsBlockedValue(winKey.GetValue(DisableRegistryToolsValueName))) return true;
                }
            }
            catch { }

            try
            {
                using var mmcKey = hkcuRoot.OpenSubKey(mmcKeyPath, writable: false);
                if (mmcKey != null)
                {
                    if (IsBlockedValue(mmcKey.GetValue(RestrictRunValueName))) return true;
                    if (IsBlockedValue(mmcKey.GetValue("RestrictRun"))) return true;
                }
            }
            catch { }

            try
            {
                using var expKey = hkcuRoot.OpenSubKey(expKeyPath, writable: false);
                if (expKey != null)
                {
                    if (IsBlockedValue(expKey.GetValue(DisableTaskMgrValueName))) return true;
                    if (IsBlockedValue(expKey.GetValue(RestrictRunValueName))) return true;
                    if (IsBlockedValue(expKey.GetValue("RestrictRun"))) return true;
                }
            }
            catch { }

            return false;
        }

        public static string GetEmergencyRestoreScriptContent()
        {
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 65001 >nul");
            sb.AppendLine("title Emergency Restore - Windows System Administration Tools");
            sb.AppendLine();
            sb.AppendLine(":: Attempt elevation if not running as administrator");
            sb.AppendLine("net session >nul 2>&1");
            sb.AppendLine("if %errorlevel% neq 0 (");
            sb.AppendLine("    echo [INFO] Requesting Administrator privileges to unlock system tools...");
            sb.AppendLine("    powershell -NoProfile -ExecutionPolicy Bypass -Command \"Start-Process -FilePath '%~f0' -Verb RunAs\" >nul 2>&1");
            sb.AppendLine("    if %errorlevel% equ 0 exit /b");
            sb.AppendLine(")");
            sb.AppendLine();
            sb.AppendLine("echo ======================================================================");
            sb.AppendLine("echo    DRIVER BOOSTER - EMERGENCY SYSTEM TOOLS UNLOCK SCRIPT");
            sb.AppendLine("echo    KHOI PHUC KHAN CAP CONG CU QUAN TRI HE THONG WINDOWS");
            sb.AppendLine("echo ======================================================================");
            sb.AppendLine("echo.");
            sb.AppendLine("echo [1/5] Unlocking Task Manager (DisableTaskMgr)...");
            sb.AppendLine("reg delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v DisableTaskMgr /f >nul 2>&1");
            sb.AppendLine("reg delete \"HKCU\\Software\\Policies\\Microsoft\\Windows\\System\" /v DisableTaskMgr /f >nul 2>&1");
            sb.AppendLine("reg delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer\" /v DisableTaskMgr /f >nul 2>&1");
            sb.AppendLine("reg delete \"HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v DisableTaskMgr /f >nul 2>&1");
            sb.AppendLine("reg delete \"HKLM\\Software\\Policies\\Microsoft\\Windows\\System\" /v DisableTaskMgr /f >nul 2>&1");
            sb.AppendLine("reg delete \"HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer\" /v DisableTaskMgr /f >nul 2>&1");
            sb.AppendLine("echo [2/5] Unlocking Registry Editor (DisableRegistryTools)...");
            sb.AppendLine("reg delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v DisableRegistryTools /f >nul 2>&1");
            sb.AppendLine("reg delete \"HKCU\\Software\\Policies\\Microsoft\\Windows\\System\" /v DisableRegistryTools /f >nul 2>&1");
            sb.AppendLine("reg delete \"HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v DisableRegistryTools /f >nul 2>&1");
            sb.AppendLine("reg delete \"HKLM\\Software\\Policies\\Microsoft\\Windows\\System\" /v DisableRegistryTools /f >nul 2>&1");
            sb.AppendLine("echo [3/5] Unlocking Command Prompt (DisableCMD)...");
            sb.AppendLine("reg delete \"HKCU\\Software\\Policies\\Microsoft\\Windows\\System\" /v DisableCMD /f >nul 2>&1");
            sb.AppendLine("reg delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v DisableCMD /f >nul 2>&1");
            sb.AppendLine("reg delete \"HKLM\\Software\\Policies\\Microsoft\\Windows\\System\" /v DisableCMD /f >nul 2>&1");
            sb.AppendLine("reg delete \"HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v DisableCMD /f >nul 2>&1");
            sb.AppendLine("echo [4/5] Unlocking Group Policy Editor and MMC (Restrict_Run / RestrictRun)...");
            sb.AppendLine("reg delete \"HKCU\\Software\\Policies\\Microsoft\\MMC\\{8FC0B734-A0E1-11D1-A7D3-0000F87571E3}\" /v Restrict_Run /f >nul 2>&1");
            sb.AppendLine("reg delete \"HKCU\\Software\\Policies\\Microsoft\\MMC\\{8FC0B734-A0E1-11D1-A7D3-0000F87571E3}\" /v RestrictRun /f >nul 2>&1");
            sb.AppendLine("reg delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer\" /v RestrictRun /f >nul 2>&1");
            sb.AppendLine("reg delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer\" /v Restrict_Run /f >nul 2>&1");
            sb.AppendLine("reg delete \"HKLM\\Software\\Policies\\Microsoft\\MMC\\{8FC0B734-A0E1-11D1-A7D3-0000F87571E3}\" /v Restrict_Run /f >nul 2>&1");
            sb.AppendLine("reg delete \"HKLM\\Software\\Policies\\Microsoft\\MMC\\{8FC0B734-A0E1-11D1-A7D3-0000F87571E3}\" /v RestrictRun /f >nul 2>&1");
            sb.AppendLine("reg delete \"HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer\" /v RestrictRun /f >nul 2>&1");
            sb.AppendLine("reg delete \"HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer\" /v Restrict_Run /f >nul 2>&1");
            sb.AppendLine("echo [5/5] Reloading Windows default system cursors...");
            sb.AppendLine("powershell -NoProfile -ExecutionPolicy Bypass -Command \"try { Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public class CursorHelper { [DllImport(\\\"user32.dll\\\")] public static extern bool SystemParametersInfo(uint a, uint b, IntPtr c, uint d); }'; if (![CursorHelper]::SystemParametersInfo(0x0057, 0, [IntPtr]::Zero, 2)) { [CursorHelper]::SystemParametersInfo(0x0057, 0, [IntPtr]::Zero, 0); } } catch {}\" >nul 2>&1");
            sb.AppendLine("echo.");
            sb.AppendLine("echo ======================================================================");
            sb.AppendLine("echo  [SUCCESS] All system administrative tools have been restored!");
            sb.AppendLine("echo  [THANH CONG] Toan bo cong cu he thong da duoc mo khoa thanh cong!");
            sb.AppendLine("echo ======================================================================");
            sb.AppendLine("echo.");
            sb.AppendLine("pause");
            return sb.ToString();
        }

        public static string EnsureEmergencyRestoreScript(string? targetDirectory = null)
        {
            var content = GetEmergencyRestoreScriptContent();
            var dir = string.IsNullOrWhiteSpace(targetDirectory) ? AppContext.BaseDirectory : targetDirectory;
            var primaryPath = Path.Combine(dir, EmergencyRestoreScriptFileName);

            try
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(primaryPath, content, new UTF8Encoding(false));
            }
            catch
            {
            }

            try
            {
                const string defaultReleaseDir = @"C:\Users\VirusBetaTester\Downloads\PalStatus\release";
                if (Directory.Exists(defaultReleaseDir))
                {
                    var fullDir = Path.GetFullPath(dir).TrimEnd('\\', '/');
                    var fullRelease = Path.GetFullPath(defaultReleaseDir).TrimEnd('\\', '/');
                    if (!string.Equals(fullDir, fullRelease, StringComparison.OrdinalIgnoreCase))
                    {
                        var releasePath = Path.Combine(defaultReleaseDir, EmergencyRestoreScriptFileName);
                        File.WriteAllText(releasePath, content, new UTF8Encoding(false));
                    }
                }
            }
            catch
            {
            }

            return primaryPath;
        }

        private bool ApplyPolicies(bool enable)
        {
            if (_customPolicyHandler != null)
                return _customPolicyHandler(enable);

            if (!enable)
            {
                return ForceUnlockAllTools();
            }

            var ok = true;
            try
            {
                using var sysKey = Registry.CurrentUser.CreateSubKey(SystemPoliciesSubKey, writable: true);
                if (sysKey != null)
                {
                    sysKey.SetValue(DisableTaskMgrValueName, 1, RegistryValueKind.DWord);
                    sysKey.SetValue(DisableRegistryToolsValueName, 1, RegistryValueKind.DWord);
                }
                else
                {
                    ok = false;
                }
            }
            catch
            {
                ok = false;
            }

            try
            {
                using var winKey = Registry.CurrentUser.CreateSubKey(WindowsPoliciesSubKey, writable: true);
                if (winKey != null)
                {
                    // Value 2 blocks interactive Command Prompt while allowing batch scripts to run normally.
                    winKey.SetValue(DisableCmdValueName, 2, RegistryValueKind.DWord);
                }
                else
                {
                    ok = false;
                }
            }
            catch
            {
                ok = false;
            }

            try
            {
                using var mmcKey = Registry.CurrentUser.CreateSubKey(GpeditMmcSnapInSubKey, writable: true);
                if (mmcKey != null)
                {
                    mmcKey.SetValue(RestrictRunValueName, 1, RegistryValueKind.DWord);
                }
                else
                {
                    ok = false;
                }
            }
            catch
            {
                ok = false;
            }

            return ok;
        }

        private void EnforceActiveWindowBlocks()
        {
            if (_disposed || !_isEnabled) return;
            lock (_gate)
            {
                if (_disposed || !_isEnabled) return;
                EnforceActiveWindowBlocksCore();
            }
        }

        private int EnforceActiveWindowBlocksCore()
        {
            var closed = 0;
            try
            {
                var currentPid = Environment.ProcessId;
                EnumWindows((hWnd, _) =>
                {
                    if (hWnd == IntPtr.Zero || !IsWindowVisible(hWnd))
                        return true;

                    GetWindowThreadProcessId(hWnd, out var pid);
                    if (pid == 0 || pid == currentPid)
                        return true;

                    var title = GetWindowTitle(hWnd);
                    var className = GetWindowClass(hWnd);
                    var procName = TryGetProcessName((int)pid);

                    if (ShouldCloseToolWindow(procName, title, className))
                    {
                        PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                        closed++;
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch
            {
            }

            if (closed > 0)
                _totalClosedWindows += closed;

            return closed;
        }

        internal static bool ShouldCloseToolWindow(string? processName, string? windowTitle, string? windowClass)
        {
            var proc = (processName ?? "").Trim();
            var title = (windowTitle ?? "").Trim();
            var cls = (windowClass ?? "").Trim();

            // 1. Task Manager (taskmgr.exe)
            if (proc.Equals("Taskmgr", StringComparison.OrdinalIgnoreCase) ||
                cls.Equals("TaskManagerWindow", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 2. Registry Editor (regedit.exe)
            if (proc.Equals("regedit", StringComparison.OrdinalIgnoreCase) ||
                cls.Equals("RegEdit_RegEdit", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 3. Interactive Command Prompt window (cmd.exe)
            if (proc.Equals("cmd", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if ((cls.Equals("ConsoleWindowClass", StringComparison.OrdinalIgnoreCase) ||
                 cls.Equals("CASCADIA_HOSTING_WINDOW_CLASS", StringComparison.OrdinalIgnoreCase)) &&
                (title.Contains("Command Prompt", StringComparison.OrdinalIgnoreCase) ||
                 title.EndsWith(@"\cmd.exe", StringComparison.OrdinalIgnoreCase) ||
                 title.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            // 4. Group Policy Editor (mmc.exe hosting gpedit.msc)
            if (proc.Equals("mmc", StringComparison.OrdinalIgnoreCase) &&
                (title.Contains("Group Policy", StringComparison.OrdinalIgnoreCase) ||
                 title.Contains("gpedit", StringComparison.OrdinalIgnoreCase) ||
                 title.Contains("Chính sách nhóm", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return false;
        }

        private static readonly ConcurrentDictionary<int, (string? Name, long ExpirationTicks)> _processNameCache = new();
        private static readonly long CacheTtlTicks = TimeSpan.FromSeconds(3).Ticks;

        private static string? TryGetProcessName(int pid)
        {
            var now = DateTime.UtcNow.Ticks;
            if (_processNameCache.TryGetValue(pid, out var cached) && now < cached.ExpirationTicks)
            {
                return cached.Name;
            }

            try
            {
                using var process = Process.GetProcessById(pid);
                var name = process.ProcessName;
                _processNameCache[pid] = (name, now + CacheTtlTicks);

                if (_processNameCache.Count > 256)
                {
                    foreach (var kvp in _processNameCache)
                    {
                        if (now >= kvp.Value.ExpirationTicks)
                            _processNameCache.TryRemove(kvp.Key, out _);
                    }
                }

                return name;
            }
            catch
            {
                _processNameCache[pid] = (null, now + CacheTtlTicks);
                return null;
            }
        }

        private static string GetWindowTitle(IntPtr hWnd)
        {
            var len = GetWindowTextLength(hWnd);
            if (len <= 0) return "";
            var sb = new StringBuilder(len + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string GetWindowClass(IntPtr hWnd)
        {
            var sb = new StringBuilder(128);
            var len = GetClassName(hWnd, sb, sb.Capacity);
            return len > 0 ? sb.ToString() : "";
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                var shouldRestore = _isEnabled || _policiesAppliedByInstance;
                _isEnabled = false;
                _policiesAppliedByInstance = false;
                try { _watchTimer.Stop(); } catch { }
                try { _watchTimer.Dispose(); } catch { }
                if (shouldRestore)
                {
                    try { ApplyPolicies(enable: false); } catch { }
                }
                else if (_customPolicyHandler == null)
                {
                    // Failsafe in production: ensure system registry policies are unlocked
                    try { ForceUnlockAllTools(); } catch { }
                }
            }
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }
}
