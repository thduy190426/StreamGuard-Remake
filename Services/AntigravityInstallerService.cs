using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace StreamGuard.Services
{
    public sealed record AntigravityInstallationInfo(
        bool IsInstalled,
        string? Version,
        string? ExePath,
        string? InstallLocation,
        string? Source = null)
    {
        public static AntigravityInstallationInfo NotInstalled { get; } = new(false, null, null, null);
    }

    public sealed record AntigravityInstallResult(
        bool Success,
        string Message,
        AntigravityInstallationInfo Info);

    public sealed class AntigravityInstallerService
    {
        public const string KnownDesktopUninstallGuid = "121a0be4-63bd-531e-acf8-fc3924c7e984";
        public const string DesktopExeName = "Antigravity.exe";
        public const string OfflineInstallerFileName = "AntigravitySetup.exe";

        private readonly Func<AntigravityInstallationInfo>? _customDetector;
        private readonly Func<string?>? _customInstallerLocator;
        private readonly Func<bool, Task<bool>>? _customProcessLauncher;
        private readonly Func<bool>? _customLaunchHandler;

        public event EventHandler<AntigravityInstallationInfo>? InstallationChanged;

        public AntigravityInstallerService(
            Func<AntigravityInstallationInfo>? customDetector = null,
            Func<string?>? customInstallerLocator = null,
            Func<bool, Task<bool>>? customProcessLauncher = null,
            Func<bool>? customLaunchHandler = null)
        {
            _customDetector = customDetector;
            _customInstallerLocator = customInstallerLocator;
            _customProcessLauncher = customProcessLauncher;
            _customLaunchHandler = customLaunchHandler;
        }

        public static bool IsAntigravityDesktopRegistryEntry(string? keyName, string? displayName)
        {
            // Strictly exclude Antigravity IDE and CLI variants first
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                if (displayName.Contains("IDE", StringComparison.OrdinalIgnoreCase) ||
                    displayName.Contains("CLI", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (!string.IsNullOrWhiteSpace(keyName))
            {
                if (keyName.Contains("IDE", StringComparison.OrdinalIgnoreCase) ||
                    keyName.Contains("CLI", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            // Match known standalone Desktop uninstall GUID
            if (!string.IsNullOrWhiteSpace(keyName) &&
                string.Equals(keyName, KnownDesktopUninstallGuid, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(displayName))
                return false;

            // Recognize Antigravity Desktop variants (e.g. "Antigravity 2.32.1", "Google Antigravity", "Antigravity Desktop")
            return displayName.Contains("Antigravity", StringComparison.OrdinalIgnoreCase);
        }

        public AntigravityInstallationInfo GetInstallationInfo()
        {
            if (_customDetector != null)
                return _customDetector();

            // 1. Check exact known registry key in HKCU
            try
            {
                using var exactKey = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{KnownDesktopUninstallGuid}");
                if (exactKey != null)
                {
                    var info = ExtractInfoFromRegistryKey(KnownDesktopUninstallGuid, exactKey);
                    if (info.IsInstalled)
                        return info;
                }
            }
            catch { }

            // 2. Scan HKCU & HKLM uninstall registry keys
            var registryInfo = ScanUninstallRegistry();
            if (registryInfo.IsInstalled)
                return registryInfo;

            // 3. Filesystem check
            var fileInfo = ScanStandardFileLocations();
            if (fileInfo.IsInstalled)
                return fileInfo;

            return AntigravityInstallationInfo.NotInstalled;
        }

        private static AntigravityInstallationInfo ScanUninstallRegistry()
        {
            var registryTargets = new[]
            {
                (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
                (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
                (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall")
            };

            foreach (var (root, subPath) in registryTargets)
            {
                try
                {
                    using var parentKey = root.OpenSubKey(subPath);
                    if (parentKey == null) continue;

                    foreach (var subKeyName in parentKey.GetSubKeyNames())
                    {
                        using var key = parentKey.OpenSubKey(subKeyName);
                        if (key == null) continue;

                        var displayName = key.GetValue("DisplayName")?.ToString();
                        if (!IsAntigravityDesktopRegistryEntry(subKeyName, displayName))
                            continue;

                        var info = ExtractInfoFromRegistryKey(subKeyName, key);
                        if (info.IsInstalled)
                            return info;
                    }
                }
                catch { }
            }

            return AntigravityInstallationInfo.NotInstalled;
        }

        private static AntigravityInstallationInfo ExtractInfoFromRegistryKey(string keyName, RegistryKey key)
        {
            var displayName = key.GetValue("DisplayName")?.ToString();

            // Enforce strict desktop matching and exclusion of IDE/CLI
            if (!IsAntigravityDesktopRegistryEntry(keyName, displayName))
                return AntigravityInstallationInfo.NotInstalled;

            var displayVersion = key.GetValue("DisplayVersion")?.ToString();
            var installLocation = key.GetValue("InstallLocation")?.ToString()?.Trim('"', ' ');
            var displayIcon = key.GetValue("DisplayIcon")?.ToString();

            string? exePath = null;

            // Clean DisplayIcon to find executable path
            if (!string.IsNullOrWhiteSpace(displayIcon))
            {
                var cleaned = Regex.Replace(displayIcon, @",\d+$", "").Trim('"', ' ');
                if (File.Exists(cleaned) && cleaned.EndsWith(DesktopExeName, StringComparison.OrdinalIgnoreCase))
                    exePath = cleaned;
            }

            // Check InstallLocation
            if (string.IsNullOrWhiteSpace(exePath) && !string.IsNullOrWhiteSpace(installLocation))
            {
                var candidate = Path.Combine(installLocation, DesktopExeName);
                if (File.Exists(candidate))
                    exePath = candidate;
            }

            // Check standard file locations
            if (string.IsNullOrWhiteSpace(exePath))
            {
                var standard = ScanStandardFileLocations();
                if (standard.IsInstalled)
                {
                    exePath = standard.ExePath?.Trim('"', ' ');
                    if (string.IsNullOrWhiteSpace(installLocation))
                        installLocation = standard.InstallLocation?.Trim('"', ' ');
                }
            }

            if (string.IsNullOrWhiteSpace(installLocation) && !string.IsNullOrWhiteSpace(exePath))
            {
                installLocation = Path.GetDirectoryName(exePath);
            }

            // Version resolution
            var version = displayVersion;
            if (string.IsNullOrWhiteSpace(version) && !string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
            {
                try
                {
                    var fvi = FileVersionInfo.GetVersionInfo(exePath);
                    version = fvi.ProductVersion ?? fvi.FileVersion;
                }
                catch { }
            }

            if (string.IsNullOrWhiteSpace(version) && !string.IsNullOrWhiteSpace(displayName))
            {
                var match = Regex.Match(displayName, @"\d+(\.\d+)+");
                if (match.Success)
                    version = match.Value;
            }

            var hasExe = !string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath);
            var hasVersion = !string.IsNullOrWhiteSpace(version);
            var isKnownGuid = string.Equals(keyName, KnownDesktopUninstallGuid, StringComparison.OrdinalIgnoreCase);

            var isInstalled = hasExe || (isKnownGuid && hasVersion) || (hasVersion && !string.IsNullOrWhiteSpace(installLocation));

            return new AntigravityInstallationInfo(isInstalled, version, exePath, installLocation, "Registry");
        }

        private static AntigravityInstallationInfo ScanStandardFileLocations()
        {
            var candidates = new List<string>();

            var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localApp))
                candidates.Add(Path.Combine(localApp, "Programs", "antigravity", DesktopExeName));

            var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(progFiles))
                candidates.Add(Path.Combine(progFiles, "antigravity", DesktopExeName));

            var progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(progFilesX86))
                candidates.Add(Path.Combine(progFilesX86, "antigravity", DesktopExeName));

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    string? version = null;
                    try
                    {
                        var fvi = FileVersionInfo.GetVersionInfo(candidate);
                        version = fvi.ProductVersion ?? fvi.FileVersion;
                    }
                    catch { }

                    return new AntigravityInstallationInfo(
                        true,
                        version,
                        candidate,
                        Path.GetDirectoryName(candidate),
                        "FileSystem");
                }
            }

            return AntigravityInstallationInfo.NotInstalled;
        }

        public string? GetOfflineInstallerPath()
        {
            if (_customInstallerLocator != null)
                return _customInstallerLocator();

            var candidates = new List<string>();

            // 1. Base directory and installers/ subfolder
            var baseDir = AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(baseDir))
            {
                candidates.Add(Path.Combine(baseDir, "installers", OfflineInstallerFileName));
                candidates.Add(Path.Combine(baseDir, OfflineInstallerFileName));

                var installersDir = Path.Combine(baseDir, "installers");
                if (Directory.Exists(installersDir))
                {
                    try
                    {
                        var matching = Directory.GetFiles(installersDir, "AntigravitySetup*.exe");
                        candidates.AddRange(matching);
                    }
                    catch { }
                }

                // Relative checks up the tree (e.g. tests or build subfolders)
                candidates.Add(Path.Combine(baseDir, "..", "release", "installers", OfflineInstallerFileName));
                candidates.Add(Path.Combine(baseDir, "..", "..", "release", "installers", OfflineInstallerFileName));
                candidates.Add(Path.Combine(baseDir, "..", "..", "..", "release", "installers", OfflineInstallerFileName));
                candidates.Add(Path.Combine(baseDir, "..", "..", "..", "..", "release", "installers", OfflineInstallerFileName));
            }

            // Direct release folder location
            candidates.Add(@"C:\Users\VirusBetaTester\Downloads\PalStatus\release\installers\AntigravitySetup.exe");
            candidates.Add(@"C:\Users\VirusBetaTester\Downloads\PalStatus\StreamGuard\bin\Release\net8.0-windows\installers\AntigravitySetup.exe");

            // 2. Windows temp cache locations
            var tempPath = Path.GetTempPath();
            if (!string.IsNullOrEmpty(tempPath))
            {
                var tempSubDir = Path.Combine(tempPath, "antigravity-stable-user-x64");
                if (Directory.Exists(tempSubDir))
                {
                    try
                    {
                        var tempFiles = Directory.GetFiles(tempSubDir, "AntigravitySetup*.exe");
                        candidates.AddRange(tempFiles);
                    }
                    catch { }
                }

                try
                {
                    var looseTemp = Directory.GetFiles(tempPath, "AntigravitySetup*.exe");
                    candidates.AddRange(looseTemp);
                }
                catch { }
            }

            var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localApp))
            {
                var localTempSubDir = Path.Combine(localApp, "Temp", "antigravity-stable-user-x64");
                if (Directory.Exists(localTempSubDir))
                {
                    try
                    {
                        var localFiles = Directory.GetFiles(localTempSubDir, "AntigravitySetup*.exe");
                        candidates.AddRange(localFiles);
                    }
                    catch { }
                }
            }

            foreach (var path in candidates)
            {
                try
                {
                    var fullPath = Path.GetFullPath(path);
                    if (File.Exists(fullPath))
                        return fullPath;
                }
                catch { }
            }

            return null;
        }

        public static string GetSilentArguments(string installerPath)
        {
            try
            {
                if (File.Exists(installerPath))
                {
                    var fvi = FileVersionInfo.GetVersionInfo(installerPath);
                    var comments = fvi.Comments ?? string.Empty;
                    var desc = fvi.FileDescription ?? string.Empty;
                    if (comments.Contains("Inno Setup", StringComparison.OrdinalIgnoreCase) ||
                        desc.Contains("Inno Setup", StringComparison.OrdinalIgnoreCase))
                    {
                        return "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-";
                    }
                }
            }
            catch { }

            return "/S";
        }

        public async Task<AntigravityInstallResult> InstallAsync(bool silent, CancellationToken cancellationToken = default)
        {
            var installerPath = GetOfflineInstallerPath();
            if (string.IsNullOrEmpty(installerPath) || (_customProcessLauncher == null && !File.Exists(installerPath)))
            {
                return new AntigravityInstallResult(
                    false,
                    "Offline installer not found in installers directory.",
                    GetInstallationInfo());
            }

            if (_customProcessLauncher != null)
            {
                var ok = await _customProcessLauncher(silent);
                var info = GetInstallationInfo();
                InstallationChanged?.Invoke(this, info);
                return new AntigravityInstallResult(
                    ok,
                    ok ? "Antigravity Desktop installed successfully!" : "Installer exited, but Antigravity Desktop installation could not be verified.",
                    info);
            }

            try
            {
                var args = silent ? GetSilentArguments(installerPath) : string.Empty;
                var psi = new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = args,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(installerPath) ?? string.Empty
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    return new AntigravityInstallResult(
                        false,
                        "Failed to start installer process.",
                        GetInstallationInfo());
                }

                await process.WaitForExitAsync(cancellationToken);

                // Give child processes or registry writes a brief window to finalize
                await Task.Delay(1500, cancellationToken);

                var updatedInfo = GetInstallationInfo();
                var success = updatedInfo.IsInstalled;
                var message = success
                    ? "Antigravity Desktop installed successfully!"
                    : "Installer completed, but Antigravity Desktop installation could not be verified.";

                InstallationChanged?.Invoke(this, updatedInfo);
                return new AntigravityInstallResult(success, message, updatedInfo);
            }
            catch (Exception ex)
            {
                return new AntigravityInstallResult(
                    false,
                    $"Installation error: {ex.Message}",
                    GetInstallationInfo());
            }
        }

        public bool LaunchAntigravity()
        {
            if (_customLaunchHandler != null)
                return _customLaunchHandler();

            var info = GetInstallationInfo();
            var exePath = info.ExePath?.Trim('"', ' ');
            if (!info.IsInstalled || string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                return false;

            try
            {
                var workDir = info.InstallLocation?.Trim('"', ' ');
                if (string.IsNullOrWhiteSpace(workDir) || !Directory.Exists(workDir))
                    workDir = Path.GetDirectoryName(exePath) ?? string.Empty;

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = workDir,
                    UseShellExecute = true
                };
                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool IsAntigravityRunning()
        {
            try
            {
                var procs = Process.GetProcessesByName("Antigravity");
                return procs.Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
