using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace StreamGuard.Services;

public static class CleanWipeService
{
    public static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StreamGuard");

    public static readonly string LocalAppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamGuard");

    /// <summary>
    /// Reverts all system interventions performed by StreamGuard / Driver Booster:
    /// - Unlocks Task Manager, Registry Editor, Command Prompt, and Group Policy Editor
    /// - Removes RAIA block entries from Windows hosts file and flushes DNS
    /// - Removes all StreamGuard firewall rules
    /// - Restores default Windows system cursors
    /// - Restores camera and microphone privacy settings
    /// - Closes hidden virtual desktop if service is provided
    /// </summary>
    public static bool RevertAllSystemModifications(out List<string> logs) =>
        RevertAllSystemModifications(out logs, vdService: null);

    public static bool RevertAllSystemModifications(out List<string> logs, VirtualDesktopService? vdService)
    {
        logs = new List<string>();
        var overallSuccess = true;

        // 0. Remove Virtual Desktop if provided
        if (vdService != null)
        {
            try
            {
                vdService.RemoveHiddenDesktop();
                logs.Add("Closed hidden virtual desktop");
            }
            catch (Exception ex)
            {
                logs.Add($"Error closing virtual desktop: {ex.Message}");
            }
        }

        // 1. Force unlock system tools (DisableTaskMgr, DisableRegistryTools, DisableCMD, Restrict_Run)
        try
        {
            if (SystemToolsBlockerService.ForceUnlockAllTools())
            {
                logs.Add("Unlocked Task Manager, Registry Editor, and Command Prompt");
            }
            else
            {
                logs.Add("System tools already unlocked or failed to unlock");
            }
        }
        catch (Exception ex)
        {
            logs.Add($"Error unlocking system tools: {ex.Message}");
            overallSuccess = false;
        }

        // 2. Remove RAIA hosts block and flush DNS
        try
        {
            if (ThreatBlockerService.RemoveRaiaHostsBlock())
            {
                logs.Add("Removed RAIA hosts block and flushed DNS");
            }
            else
            {
                logs.Add("Hosts file clean or no RAIA block present");
            }
        }
        catch (Exception ex)
        {
            logs.Add($"Error restoring hosts file: {ex.Message}");
            overallSuccess = false;
        }

        // 3. Remove firewall rules
        try
        {
            if (RemoveAllFirewallRules())
            {
                logs.Add("Removed StreamGuard firewall rules");
            }
            else
            {
                logs.Add("Firewall rules clean");
            }
        }
        catch (Exception ex)
        {
            logs.Add($"Error removing firewall rules: {ex.Message}");
            overallSuccess = false;
        }

        // 4. Restore system cursors
        try
        {
            if (CursorShieldService.RestoreAllCursors())
            {
                logs.Add("Restored default system cursors");
            }
            else
            {
                logs.Add("System cursors restored");
            }
        }
        catch (Exception ex)
        {
            logs.Add($"Error restoring cursors: {ex.Message}");
            overallSuccess = false;
        }

        // 5. Restore camera & microphone privacy settings
        try
        {
            RestoreDevicePrivacy();
            logs.Add("Restored camera and microphone privacy settings");
        }
        catch (Exception ex)
        {
            logs.Add($"Error restoring device privacy: {ex.Message}");
        }

        return overallSuccess;
    }

    /// <summary>
    /// Deletes all firewall rules matching 'StreamGuard' in DisplayName or Name.
    /// </summary>
    public static bool RemoveAllFirewallRules()
    {
        try
        {
            var psScript = @"
$ErrorActionPreference = 'SilentlyContinue'
Get-NetFirewallRule -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -match 'StreamGuard' -or $_.Name -match 'StreamGuard' } | Remove-NetFirewallRule
";
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
            return true;
        }
        catch
        {
            // Fallback via netsh if PowerShell fails
            try
            {
                var netshPsi = new ProcessStartInfo("netsh.exe", "advfirewall firewall delete rule name=\"StreamGuard Block RAIA Outbound\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var netshProc = Process.Start(netshPsi);
                netshProc?.WaitForExit(3000);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Restores camera and microphone device access in Windows capability consent store to "Allow".
    /// </summary>
    public static void RestoreDevicePrivacy()
    {
        try
        {
            Registry.SetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam",
                "Value", "Allow", RegistryValueKind.String);
            Registry.SetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone",
                "Value", "Allow", RegistryValueKind.String);
        }
        catch { }

        try
        {
            var psScript = @"
$ErrorActionPreference = 'SilentlyContinue'
$devices = @(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object {
    $_.Class -eq 'Camera' -or ($_.Class -eq 'Image' -and $_.FriendlyName -match 'camera|webcam|integrated')
})
foreach ($device in $devices) {
    if ($device.InstanceId) {
        Enable-PnpDevice -InstanceId $device.InstanceId -Confirm:$false -ErrorAction SilentlyContinue | Out-Null
    }
}
";
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
        }
        catch { }
    }

    /// <summary>
    /// Safely terminates WebView2, Captions AI worker, and secondary Driver Booster instances.
    /// Excludes the current process and any optionally specified excludePid.
    /// </summary>
    public static int TerminateBackgroundProcesses(int excludePid = 0)
    {
        var terminatedCount = 0;
        var currentPid = Process.GetCurrentProcess().Id;

        // 1. Terminate WebView2 instances belonging to StreamGuard
        try
        {
            var webViewProcs = Process.GetProcessesByName("msedgewebview2");
            foreach (var proc in webViewProcs)
            {
                using (proc)
                {
                    if (proc.Id == currentPid || proc.Id == excludePid) continue;
                    try
                    {
                        if (IsStreamGuardChildOrRelatedProcess(proc.Id, currentPid))
                        {
                            proc.Kill(entireProcessTree: true);
                            proc.WaitForExit(1000);
                            terminatedCount++;
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        // 2. Terminate python worker instances belonging to CaptionsRuntime
        try
        {
            foreach (var name in new[] { "python", "pythonw" })
            {
                var pyProcs = Process.GetProcessesByName(name);
                foreach (var proc in pyProcs)
                {
                    using (proc)
                    {
                        if (proc.Id == currentPid || proc.Id == excludePid) continue;
                        try
                        {
                            if (IsCaptionsWorkerProcess(proc.Id, currentPid))
                            {
                                proc.Kill(entireProcessTree: true);
                                proc.WaitForExit(1000);
                                terminatedCount++;
                            }
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }

        // 3. Terminate other instances of Driver Booster or StreamGuard
        try
        {
            foreach (var name in new[] { "Driver Booster", "StreamGuard" })
            {
                var appProcs = Process.GetProcessesByName(name);
                foreach (var proc in appProcs)
                {
                    using (proc)
                    {
                        if (proc.Id == currentPid || proc.Id == excludePid) continue;
                        try
                        {
                            proc.Kill(entireProcessTree: true);
                            proc.WaitForExit(1000);
                            terminatedCount++;
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }

        return terminatedCount;
    }

    private static bool IsStreamGuardChildOrRelatedProcess(int pid, int currentPid)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT ParentProcessId, CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
            {
                var parentId = Convert.ToInt32(obj["ParentProcessId"]);
                if (parentId == currentPid) return true;

                var cmd = obj["CommandLine"] as string;
                if (!string.IsNullOrEmpty(cmd) && (cmd.Contains("StreamGuard", StringComparison.OrdinalIgnoreCase) || cmd.Contains("Driver Booster", StringComparison.OrdinalIgnoreCase) || cmd.Contains("DriverBooster", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static bool IsCaptionsWorkerProcess(int pid, int currentPid)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT ParentProcessId, CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
            {
                var parentId = Convert.ToInt32(obj["ParentProcessId"]);
                if (parentId == currentPid) return true;

                var cmd = obj["CommandLine"] as string;
                if (!string.IsNullOrEmpty(cmd) && (cmd.Contains("CaptionsRuntime", StringComparison.OrdinalIgnoreCase) || cmd.Contains("worker.py", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }
        catch { }

        // Non-WMI fallback
        try
        {
            using var proc = Process.GetProcessById(pid);
            var exePath = proc.MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath) && (exePath.Contains("CaptionsRuntime", StringComparison.OrdinalIgnoreCase) || exePath.Contains("StreamGuard", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// Purges all application data: %APPDATA%\StreamGuard, %LOCALAPPDATA%\StreamGuard,
    /// temp cache files, and optionally the screenshots directory.
    /// Overrides are supported for isolated automated unit tests.
    /// </summary>
    public static (int dirsRemoved, int filesRemoved) PurgeApplicationData(
        bool includeScreenshots = false,
        string? customScreenshotDir = null,
        string? appDataOverride = null,
        string? localAppDataOverride = null,
        string? tempOverride = null,
        string? screenshotsOverride = null)
    {
        var totalDirs = 0;
        var totalFiles = 0;

        var appData = appDataOverride ?? AppDataDir;
        var localAppData = localAppDataOverride ?? LocalAppDataDir;
        var tempDir = tempOverride ?? Path.GetTempPath();

        // 1. Purge AppData directory (%APPDATA%\StreamGuard)
        if (Directory.Exists(appData))
        {
            var (d, f) = SafeDeleteDirectory(appData);
            totalDirs += d;
            totalFiles += f;
        }

        // 2. Purge LocalAppData directory (%LOCALAPPDATA%\StreamGuard)
        if (Directory.Exists(localAppData))
        {
            var (d, f) = SafeDeleteDirectory(localAppData);
            totalDirs += d;
            totalFiles += f;
        }

        // 3. Purge temp files and directories
        try
        {
            if (Directory.Exists(tempDir))
            {
                var tempSubDirs = Directory.GetDirectories(tempDir, "*StreamGuard*", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.GetDirectories(tempDir, "*DriverBooster*", SearchOption.TopDirectoryOnly))
                    .Where(d => !d.Contains("StreamGuard-localization-tests-", StringComparison.OrdinalIgnoreCase));
                foreach (var dir in tempSubDirs)
                {
                    var (d, f) = SafeDeleteDirectory(dir);
                    totalDirs += d;
                    totalFiles += f;
                }

                var tempFiles = Directory.GetFiles(tempDir, "*StreamGuard*", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.GetFiles(tempDir, "*DriverBooster*", SearchOption.TopDirectoryOnly))
                    .Concat(Directory.GetFiles(tempDir, "*config.json.corrupt*", SearchOption.TopDirectoryOnly));
                foreach (var file in tempFiles)
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                        totalFiles++;
                    }
                    catch { }
                }
            }
        }
        catch { }

        // 4. Purge screenshots directory if requested (Option B)
        if (includeScreenshots)
        {
            var defaultScreenshots = screenshotsOverride ?? SnippingToolService.DefaultScreenshotDirectory;
            if (Directory.Exists(defaultScreenshots))
            {
                var (d, f) = SafeDeleteDirectory(defaultScreenshots);
                totalDirs += d;
                totalFiles += f;
            }

            if (!string.IsNullOrWhiteSpace(customScreenshotDir) && Directory.Exists(customScreenshotDir))
            {
                var normCustom = Path.GetFullPath(customScreenshotDir);
                var normDefault = Path.GetFullPath(defaultScreenshots);
                if (!string.Equals(normCustom, normDefault, StringComparison.OrdinalIgnoreCase))
                {
                    if (IsDedicatedScreenshotDirectory(normCustom))
                    {
                        var (d, f) = SafeDeleteDirectory(normCustom);
                        totalDirs += d;
                        totalFiles += f;
                    }
                    else
                    {
                        // Shared directory (e.g. MyPictures or Desktop): ONLY delete screenshot files!
                        var cleaned = CleanScreenshotFilesOnly(normCustom);
                        totalFiles += cleaned;
                    }
                }
            }
        }

        return (totalDirs, totalFiles);
    }

    /// <summary>
    /// Checks whether a folder path is safe to delete as a dedicated screenshot folder,
    /// preventing accidental deletion of user root folders like Pictures, Desktop, Downloads, or Drive root.
    /// </summary>
    public static bool IsDedicatedScreenshotDirectory(string dirPath)
    {
        if (string.IsNullOrWhiteSpace(dirPath)) return false;
        try
        {
            var full = Path.GetFullPath(dirPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var root = Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase)) return false;

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var myPics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var downloads = Path.Combine(userProfile, "Downloads");
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            var protectedPaths = new[] { userProfile, myPics, desktop, downloads, docs }
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            if (protectedPaths.Any(p => string.Equals(full, p, StringComparison.OrdinalIgnoreCase)))
                return false;

            var dirName = Path.GetFileName(full);
            return Regex.IsMatch(dirName, @"(?i)(driver[\s_]?booster|stream[\s_]?guard|screensho+t)");
        }
        catch
        {
            return false;
        }
    }

    private static int CleanScreenshotFilesOnly(string dirPath)
    {
        var cleaned = 0;
        try
        {
            var dir = new DirectoryInfo(dirPath);
            foreach (var file in dir.EnumerateFiles())
            {
                if (Regex.IsMatch(file.Name, @"(?i)^(screenshot_|driverbooster_).*\.png$"))
                {
                    try
                    {
                        file.Attributes = FileAttributes.Normal;
                        file.Delete();
                        cleaned++;
                    }
                    catch { }
                }
            }
        }
        catch { }
        return cleaned;
    }

    /// <summary>
    /// Safely deletes a directory and its entire subtree, stripping read-only/hidden attributes,
    /// avoiding following reparse points (symlinks/junctions), and retrying on locked files.
    /// </summary>
    public static (int dirs, int files) SafeDeleteDirectory(string targetDir)
    {
        var dirsRemoved = 0;
        var filesRemoved = 0;
        if (!Directory.Exists(targetDir)) return (0, 0);

        try
        {
            var dirInfo = new DirectoryInfo(targetDir);
            DeleteFileSystemInfoRecursive(dirInfo, isRoot: true, ref dirsRemoved, ref filesRemoved);
        }
        catch { }

        return (dirsRemoved, filesRemoved);
    }

    private static void DeleteFileSystemInfoRecursive(DirectoryInfo dir, bool isRoot, ref int dirsRemoved, ref int filesRemoved)
    {
        // Don't follow reparse points (symlinks/junctions) into targets!
        if ((dir.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            try
            {
                dir.Attributes = FileAttributes.Normal;
                dir.Delete(false);
                dirsRemoved++;
            }
            catch { }
            return;
        }

        try
        {
            foreach (var file in dir.EnumerateFiles())
            {
                var deleted = false;
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        file.Attributes = FileAttributes.Normal;
                        file.Delete();
                        filesRemoved++;
                        deleted = true;
                        break;
                    }
                    catch
                    {
                        Thread.Sleep(50);
                    }
                }
                if (!deleted)
                {
                    try
                    {
                        file.Attributes = FileAttributes.Normal;
                        file.Delete();
                        filesRemoved++;
                    }
                    catch { }
                }
            }
        }
        catch { }

        try
        {
            foreach (var subDir in dir.EnumerateDirectories())
            {
                DeleteFileSystemInfoRecursive(subDir, isRoot: false, ref dirsRemoved, ref filesRemoved);
            }
        }
        catch { }

        try
        {
            dir.Attributes = FileAttributes.Normal;
            dir.Delete(true);
            dirsRemoved++;
        }
        catch
        {
            try
            {
                Thread.Sleep(100);
                dir.Attributes = FileAttributes.Normal;
                dir.Delete(true);
                dirsRemoved++;
            }
            catch { }
        }
    }

    /// <summary>
    /// Strictly inspects whether an executable path is inside a development source repository.
    /// Checks the directory, up to 4 parent levels, and sibling subdirectories for *.csproj, *.cs, *.sln, or .git.
    /// Excludes system roots and well-known user folders from sibling scans.
    /// If true, the uninstaller MUST NEVER delete the directory!
    /// </summary>
    public static bool IsSourceRepository(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return false;
        try
        {
            var fullPath = Path.GetFullPath(exePath);
            var currentDir = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(currentDir)) return false;

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var downloads = Path.Combine(userProfile, "Downloads");
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            var systemRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                userProfile, desktop, downloads, docs,
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            };

            var dirInfo = new DirectoryInfo(currentDir);
            for (var level = 0; level < 4 && dirInfo != null; level++)
            {
                var dirFull = dirInfo.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var isDriveRoot = dirInfo.Parent == null || string.Equals(dirFull, Path.GetPathRoot(dirFull)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

                if (dirInfo.GetFiles("*.csproj").Length > 0 ||
                    dirInfo.GetFiles("*.cs").Length > 0 ||
                    dirInfo.GetFiles("*.sln").Length > 0 ||
                    dirInfo.GetDirectories(".git").Length > 0)
                {
                    return true;
                }

                // Only check sibling subdirectories if this is NOT a root or well-known user container folder
                if (!isDriveRoot && !systemRoots.Contains(dirFull))
                {
                    try
                    {
                        foreach (var sub in dirInfo.GetDirectories())
                        {
                            if (sub.FullName.Equals(dirInfo.FullName, StringComparison.OrdinalIgnoreCase)) continue;
                            if (sub.GetFiles("*.csproj").Length > 0 || sub.GetDirectories(".git").Length > 0)
                            {
                                return true;
                            }
                        }
                    }
                    catch { }
                }

                dirInfo = dirInfo.Parent;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Generates the PowerShell script content used for detached self-deletion and cleanup.
    /// </summary>
    public static string GenerateUninstallScriptContent(
        string exePath,
        int currentPid,
        bool isSourceRepo,
        bool includeScreenshots = true,
        string? customScreenshotDir = null)
    {
        var psTargetExe = "'" + (exePath ?? "").Replace("'", "''") + "'";
        var psScreenshotDir = "'" + (customScreenshotDir ?? "").Replace("'", "''") + "'";

        var sb = new StringBuilder();
        sb.AppendLine("# Driver Booster / StreamGuard Detached Uninstaller");
        sb.AppendLine("$ErrorActionPreference = 'SilentlyContinue'");
        sb.AppendLine($"$TargetPid = {currentPid}");
        sb.AppendLine($"$TargetExe = {psTargetExe}");
        sb.AppendLine($"$IsSourceRepo = ${isSourceRepo.ToString().ToLower()}");
        sb.AppendLine($"$IncludeScreenshots = ${includeScreenshots.ToString().ToLower()}");
        sb.AppendLine($"$ScreenshotDir = {psScreenshotDir}");
        sb.AppendLine();

        sb.AppendLine(@"# 1. Wait for target process to exit
if ($TargetPid -gt 0) {
    for ($w = 0; $w -lt 30; $w++) {
        try {
            $p = Get-Process -Id $TargetPid -ErrorAction SilentlyContinue
            if (-not $p -or $p.HasExited) { break }
            Start-Sleep -Milliseconds 500
        } catch { break }
    }
    try {
        $p = Get-Process -Id $TargetPid -ErrorAction SilentlyContinue
        if ($p -and -not $p.HasExited) {
            Stop-Process -Id $TargetPid -Force -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds 500
        }
    } catch {}
}

# 2. Wait for file locks to release
Start-Sleep -Milliseconds 500

# 3. Terminate remaining background worker processes
try {
    Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -eq 'msedgewebview2.exe' -and ($_.CommandLine -match 'StreamGuard' -or $_.CommandLine -match 'Driver\s?Booster')
    } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
} catch {}

try {
    Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
        $_.CommandLine -match 'CaptionsRuntime' -or $_.CommandLine -match 'worker.py'
    } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
} catch {}

# 4. Revert system registry policies, hosts file, and firewall rules
try {
    Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\System' -Name 'DisableTaskMgr' -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\System' -Name 'DisableRegistryTools' -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path 'HKCU:\Software\Policies\Microsoft\Windows\System' -Name 'DisableCMD' -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer' -Name 'Restrict_Run' -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer' -Name 'RestrictRun' -ErrorAction SilentlyContinue
    Get-NetFirewallRule -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -match 'StreamGuard' -or $_.Name -match 'StreamGuard' } | Remove-NetFirewallRule -ErrorAction SilentlyContinue
} catch {}

$hostsPath = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
if (Test-Path -LiteralPath $hostsPath) {
    try {
        $content = [System.IO.File]::ReadAllText($hostsPath)
        if ($content -match '# StreamGuard RAIA block START') {
            $cleaned = [System.Text.RegularExpressions.Regex]::Replace($content, '(?ms)\r?\n?# StreamGuard RAIA block START.*?# StreamGuard RAIA block END\r?\n?', ""`r`n"").TrimEnd() + ""`r`n""
            [System.IO.File]::WriteAllText($hostsPath, $cleaned)
            Clear-DnsClientCache -ErrorAction SilentlyContinue
        }
    } catch {}
}

# 5. Purge AppData directory
$appData = Join-Path $env:APPDATA 'StreamGuard'
if (Test-Path -LiteralPath $appData) {
    for ($i = 0; $i -lt 5; $i++) {
        try {
            Get-ChildItem -LiteralPath $appData -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object {
                if (-not $_.PSIsContainer) { $_.Attributes = 'Normal' }
            }
            Remove-Item -LiteralPath $appData -Recurse -Force -ErrorAction Stop
            break
        } catch { Start-Sleep -Milliseconds 300 }
    }
}

# 6. Purge LocalAppData directory
$localAppData = Join-Path $env:LOCALAPPDATA 'StreamGuard'
if (Test-Path -LiteralPath $localAppData) {
    for ($i = 0; $i -lt 5; $i++) {
        try {
            Get-ChildItem -LiteralPath $localAppData -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object {
                if (-not $_.PSIsContainer) { $_.Attributes = 'Normal' }
            }
            Remove-Item -LiteralPath $localAppData -Recurse -Force -ErrorAction Stop
            break
        } catch { Start-Sleep -Milliseconds 300 }
    }
}

# 7. Purge Temp cache files
try {
    Get-ChildItem -Path $env:TEMP -Filter '*StreamGuard*' -Recurse -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    Get-ChildItem -Path $env:TEMP -Filter '*DriverBooster*' -Recurse -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    Get-ChildItem -Path $env:TEMP -Filter '*config.json.corrupt*' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
} catch {}

# 8. Purge Screenshots if requested (Option B)
if ($IncludeScreenshots) {
    $myPics = [Environment]::GetFolderPath('MyPictures')
    if ($myPics) {
        $defaultPics = Join-Path $myPics 'DriverBooster_Screenshots'
        if (Test-Path -LiteralPath $defaultPics) {
            Remove-Item -LiteralPath $defaultPics -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    if ($ScreenshotDir -and (Test-Path -LiteralPath $ScreenshotDir)) {
        $normScreenshotDir = [System.IO.Path]::GetFullPath($ScreenshotDir).TrimEnd('\')
        $normPics = if ($myPics) { [System.IO.Path]::GetFullPath($myPics).TrimEnd('\') } else { '' }
        $dirLeaf = Split-Path -Path $normScreenshotDir -Leaf
        if ($normScreenshotDir -ine $normPics -and ($dirLeaf -match '(?i)driver[\s_]?booster|stream[\s_]?guard|screenshot')) {
            Remove-Item -LiteralPath $normScreenshotDir -Recurse -Force -ErrorAction SilentlyContinue
        } else {
            Get-ChildItem -LiteralPath $normScreenshotDir -File -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match '(?i)^(screenshot_|driverbooster_).*\.png$' } |
                Remove-Item -Force -ErrorAction SilentlyContinue
        }
    }
}

# 9. Delete target executable and companion files
if ($TargetExe -and (Test-Path -LiteralPath $TargetExe)) {
    $parentDir = Split-Path -Path $TargetExe -Parent
    $exeName = [System.IO.Path]::GetFileNameWithoutExtension($TargetExe)

    $filesToDelete = @($TargetExe)
    if (-not $IsSourceRepo -and $parentDir) {
        $filesToDelete += (Join-Path $parentDir ""$exeName.dll"")
        $filesToDelete += (Join-Path $parentDir ""$exeName.deps.json"")
        $filesToDelete += (Join-Path $parentDir ""$exeName.runtimeconfig.json"")
        $filesToDelete += (Join-Path $parentDir ""$exeName.pdb"")
        $filesToDelete += (Join-Path $parentDir 'Emergency_Restore_Tools.bat')
    }

    foreach ($file in $filesToDelete) {
        if (Test-Path -LiteralPath $file) {
            for ($i = 0; $i -lt 5; $i++) {
                try {
                    $item = Get-Item -LiteralPath $file -ErrorAction SilentlyContinue
                    if ($item) { $item.Attributes = 'Normal' }
                    Remove-Item -LiteralPath $file -Force -ErrorAction Stop
                    break
                } catch { Start-Sleep -Milliseconds 300 }
            }
        }
    }
}

# 10. Delete application directory ONLY if dedicated and safe
if (-not $IsSourceRepo -and $TargetExe) {
    $appDir = Split-Path -Path $TargetExe -Parent
    if ($appDir -and (Test-Path -LiteralPath $appDir)) {
        $protectedPaths = @(
            $env:SystemDrive + '\',
            $env:SystemRoot,
            (Join-Path $env:SystemRoot 'System32'),
            $env:ProgramFiles,
            ${env:ProgramFiles(x86)},
            $env:ProgramData,
            $env:USERPROFILE,
            [Environment]::GetFolderPath('Desktop'),
            [Environment]::GetFolderPath('CommonDesktopDirectory'),
            [Environment]::GetFolderPath('MyDocuments'),
            [Environment]::GetFolderPath('CommonDocuments'),
            [Environment]::GetFolderPath('MyPictures'),
            [Environment]::GetFolderPath('MyMusic'),
            [Environment]::GetFolderPath('MyVideos'),
            (Join-Path $env:USERPROFILE 'Downloads'),
            $env:APPDATA,
            $env:LOCALAPPDATA,
            $env:TEMP
        ) | Where-Object { $_ } | ForEach-Object { [System.IO.Path]::GetFullPath($_).TrimEnd('\') }

        $normAppDir = [System.IO.Path]::GetFullPath($appDir).TrimEnd('\')
        $isProtected = $false
        foreach ($p in $protectedPaths) {
            if ($normAppDir -ieq $p) {
                $isProtected = $true
                break
            }
        }
        if ($normAppDir -match '^[a-zA-Z]:$') { $isProtected = $true }

        $hasRepo = $false
        $checkDir = $normAppDir
        for ($lvl = 0; $lvl -lt 4 -and $checkDir; $lvl++) {
            if ((Get-ChildItem -LiteralPath $checkDir -Filter '*.csproj' -ErrorAction SilentlyContinue) -or
                (Get-ChildItem -LiteralPath $checkDir -Filter '*.cs' -ErrorAction SilentlyContinue) -or
                (Get-ChildItem -LiteralPath $checkDir -Filter '*.sln' -ErrorAction SilentlyContinue) -or
                (Test-Path -LiteralPath (Join-Path $checkDir '.git'))) {
                $hasRepo = $true
                break
            }
            $parent = Split-Path -Path $checkDir -Parent
            if ($parent -eq $checkDir) { break }
            $checkDir = $parent
        }

        $dirName = Split-Path -Path $normAppDir -Leaf
        $isDedicatedFolder = ($dirName -match '(?i)driver[\s_]?booster|stream[\s_]?guard')

        if (-not $isProtected -and -not $hasRepo -and $isDedicatedFolder) {
            try {
                $remainingNonApp = Get-ChildItem -LiteralPath $normAppDir -Recurse -File -ErrorAction SilentlyContinue |
                    Where-Object { $_.Name -notmatch '(?i)(driver[\s_]?booster|streamguard|emergency_restore|\.deps\.json|\.runtimeconfig\.json|\.dll|\.pdb|\.exe)' }
                if (-not $remainingNonApp) {
                    Remove-Item -LiteralPath $normAppDir -Recurse -Force -ErrorAction SilentlyContinue
                }
            } catch {}
        }
    }
}

# 11. Self-delete this PowerShell uninstaller script
try {
    Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
} catch {}
");

        return sb.ToString();
    }

    /// <summary>
    /// Writes the detached uninstaller PowerShell script to %TEMP% and launches it in background.
    /// </summary>
    public static bool LaunchDetachedUninstaller(
        string exePath,
        int currentPid,
        bool isSourceRepo,
        bool includeScreenshots,
        string? customScreenshotDir,
        out string scriptPath)
    {
        scriptPath = Path.Combine(Path.GetTempPath(), $"StreamGuard_Uninstall_{Guid.NewGuid():N}.ps1");
        try
        {
            var content = GenerateUninstallScriptContent(
                exePath: exePath,
                currentPid: currentPid,
                isSourceRepo: isSourceRepo,
                includeScreenshots: includeScreenshots,
                customScreenshotDir: customScreenshotDir);

            File.WriteAllText(scriptPath, content, new UTF8Encoding(false));

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            var proc = Process.Start(psi);
            return proc != null;
        }
        catch
        {
            return false;
        }
    }
}
