using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using Microsoft.Win32;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using StreamGuard.Helpers;

namespace StreamGuard.Services
{
    public sealed record ThreatBlockerStatus(
        bool IsActive,
        int TotalContained,
        int LastContained,
        string Message,
        DateTime Timestamp);

    public sealed class ThreatBlockerService : IDisposable
    {
        public const int ActiveScanIntervalMs = 2000;
        public const int IdleScanIntervalMs = 5000;
        private const int ScanIntervalMs = ActiveScanIntervalMs;
        private const string FirewallPrefix = "StreamGuard Block RAIA";
        private const string RemoteFirewallPrefix = "StreamGuard Block RAIA Remote";
        public const string HostsStartMarker = "# StreamGuard RAIA block START";
        public const string HostsEndMarker = "# StreamGuard RAIA block END";
        public const string RaiaWebsiteHost = "raia.edu.vn";
        public const string PortalWebsiteHost = "portal.rikkei.edu.vn";

        private static readonly HashSet<string> KnownSha256Hashes = new(StringComparer.OrdinalIgnoreCase)
        {
            "97932D27A506E7F226F60D75A178483889D7301E5075158BF77C61BF4446EFAF",
            "E2CAA4243EB49306ED954DFD93B8780DD4E54007471279C6D73062FFE501A68B"
        };

        private sealed record RaiaProcessSnapshot(int ProcessId, string Name, string? ExecutablePath);
        private sealed record PowerShellResult(int ExitCode, string StandardOutput, string StandardError);
        private sealed record PrivacyCapability(string Name, string Value);

        private readonly System.Timers.Timer _scanTimer = new(ScanIntervalMs);
        private readonly Dictionary<string, string?> _hashCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _privacyConfiguredExecutableCapabilities = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _firewalledExecutables = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _blockedRemoteAddresses = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<int> _seenRaiaProcessIds = new();
        private readonly object _scanLock = new();
        private DateTime _lastWmiProcessScan = DateTime.MinValue;
        private static readonly TimeSpan WmiScanThrottle = TimeSpan.FromSeconds(30);
        private ManagementEventWatcher? _processStartWatcher;
        private FileSystemWatcher? _raiaAuthWatcher;
        private bool _staticBlocksApplied;
        private bool _networkAdminWarningSent;
        private bool _hostsBlockApplied;
        private bool _hostsBlockFailed;
        private bool _allowRulesRemoved;
        private bool _isWindowMinimized;
        private int _lastRunningRaiaCount;
        private volatile bool _disposed;
        private volatile bool _isActive;

        public event Action<ThreatBlockerStatus>? StatusChanged;

        public bool IsActive => _isActive;
        public int TotalContained { get; private set; }
        public bool BlockKnownWebsites { get; private set; }
        public bool BlockRaiaWebsite { get; private set; }
        public bool BlockPortalWebsite { get; private set; }
        public bool BlockCameraAccess { get; private set; } = true;
        public bool BlockMicrophoneAccess { get; private set; } = true;
        public int CurrentScanIntervalMs => (int)_scanTimer.Interval;
        public string KnownBlockedWebsites
        {
            get
            {
                var hosts = GetSelectedWebsiteHosts();
                return hosts.Count == 0 ? "none" : string.Join(", ", hosts);
            }
        }

        public ThreatBlockerService()
        {
            _scanTimer.AutoReset = true;
            _scanTimer.Elapsed += (_, _) =>
            {
                if (_disposed || !IsActive) return;
                if (!Monitor.TryEnter(_scanLock)) return;
                try { BlockNow(); }
                finally { Monitor.Exit(_scanLock); }
            };
        }

        public void SetWindowMinimized(bool minimized)
        {
            lock (_scanLock)
            {
                _isWindowMinimized = minimized;
                UpdateAdaptiveInterval(hasRisk: _lastRunningRaiaCount > 0);
            }
        }

        public void RestoreActivePolling()
        {
            lock (_scanLock)
            {
                _isWindowMinimized = false;
                _scanTimer.Interval = ActiveScanIntervalMs;
                if (IsActive && !_disposed)
                {
                    ThreadPool.QueueUserWorkItem(_ => BlockNow());
                }
            }
        }

        private void UpdateAdaptiveInterval(bool hasRisk)
        {
            var targetInterval = (hasRisk || !_isWindowMinimized)
                ? ActiveScanIntervalMs
                : IdleScanIntervalMs;

            if (Math.Abs(_scanTimer.Interval - targetInterval) > 1)
            {
                _scanTimer.Interval = targetInterval;
            }
        }

        public void Start()
        {
            if (_disposed || IsActive) return;
            _isActive = true;
            _scanTimer.Interval = ActiveScanIntervalMs;
            StartProcessStartWatcher();
            StartRecordWatcher();
            BlockNow();
            _scanTimer.Start();
        }

        public void Stop()
        {
            if (!IsActive) return;
            _isActive = false;
            _scanTimer.Stop();
            StopWatchers();
            Publish(0, "RAIA shield paused");
        }

        public ThreatBlockerStatus SetBlockKnownWebsites(bool enabled)
        {
            return SetWebsiteBlockPreference(enabled, enabled);
        }

        public ThreatBlockerStatus SetWebsiteBlockPreference(bool blockRaiaWebsite, bool blockPortalWebsite)
        {
            lock (_scanLock)
            {
                if (_disposed) return Publish(0, "Service stopped");
                BlockRaiaWebsite = blockRaiaWebsite;
                BlockPortalWebsite = blockPortalWebsite;
                BlockKnownWebsites = blockRaiaWebsite || blockPortalWebsite;
                _hostsBlockApplied = false;
                _hostsBlockFailed = false;
                var selectedHosts = GetSelectedWebsiteHosts();

                if (!IsRunningElevated())
                    return Publish(0, BlockKnownWebsites
                        ? "website block failed - run Driver Booster as Administrator"
                        : "website unblock failed - run Driver Booster as Administrator");

                var changed = selectedHosts.Count > 0
                    ? WriteRaiaHostsBlock(selectedHosts)
                    : RemoveRaiaHostsBlock();
                _hostsBlockApplied = selectedHosts.Count > 0 && TestRaiaHostsBlock(selectedHosts);
                return Publish(0, changed
                    ? (selectedHosts.Count > 0
                        ? $"Website block applied: {KnownBlockedWebsites}"
                        : "RAIA websites unblocked in hosts")
                    : (selectedHosts.Count > 0
                        ? $"Website block already set: {KnownBlockedWebsites}"
                        : "RAIA websites block already off"));
            }
        }

        public ThreatBlockerStatus SetDevicePrivacyPreference(bool blockCameraAccess, bool blockMicrophoneAccess)
        {
            lock (_scanLock)
            {
                if (_disposed) return Publish(0, "Service stopped");
                BlockCameraAccess = blockCameraAccess;
                BlockMicrophoneAccess = blockMicrophoneAccess;
                _privacyConfiguredExecutableCapabilities.Clear();

                var details = new List<string>();
                if (ApplyCurrentUserDevicePrivacySettings())
                    details.Add(GetDevicePrivacySummary());

                var perAppChanges = IsActive
                    ? ApplyPerAppPrivacyDeniesForNewExecutables(GetRaiaProcessSnapshots(), details: null)
                    : ApplyPerAppPrivacySettingsForExecutables(GetRaiaExecutableCandidates());
                if (perAppChanges > 0)
                    details.Add($"updated {perAppChanges} per-app RAIA privacy rule(s)");

                if (!BlockCameraAccess && TryEnableCameraDevices())
                    details.Add("camera devices re-enabled if Windows had disabled them");

                var message = details.Count > 0
                    ? string.Join("; ", details.Distinct(StringComparer.OrdinalIgnoreCase))
                    : $"Device privacy unchanged: {GetDevicePrivacySummary()}";

                return Publish(0, message);
            }
        }

        public ThreatBlockerStatus BlockNow()
        {
            lock (_scanLock)
            {
                if (_disposed || !IsActive) return Publish(0, "RAIA shield paused");
                var raiaProcesses = GetRaiaProcessSnapshots();
                var runningRaiaCount = raiaProcesses.Count;
                _lastRunningRaiaCount = runningRaiaCount;
                UpdateAdaptiveInterval(runningRaiaCount > 0);
                var newlySeen = 0;
                foreach (var process in raiaProcesses)
                {
                    if (_seenRaiaProcessIds.Add(process.ProcessId))
                        newlySeen++;
                }
                TotalContained += newlySeen;

                var details = new List<string>();
                if (!_staticBlocksApplied)
                {
                    ApplyLocalBlocks(raiaProcesses, details);
                    if (_processStartWatcher != null)
                        details.Add("WMI process-start watcher armed");
                    if (_raiaAuthWatcher != null)
                        details.Add("record watcher armed");
                    _staticBlocksApplied = true;
                }
                else
                {
                    ApplyPerAppPrivacyDeniesForNewExecutables(raiaProcesses, details);
                    ApplyNetworkBlocks(raiaProcesses, details);
                }

                if (runningRaiaCount > 0)
                    details.Insert(0, $"detected {runningRaiaCount} running RAIA process(es)");

                var message = details.Count > 0
                    ? string.Join("; ", details.Distinct(StringComparer.OrdinalIgnoreCase))
                    : "RAIA containment active - no RAIA process found";

                return Publish(runningRaiaCount, message);
            }
        }

        private List<RaiaProcessSnapshot> GetRaiaProcessSnapshots()
        {
            var snapshots = new Dictionary<int, RaiaProcessSnapshot>();

            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    if (process.Id == Environment.ProcessId) continue;

                    var name = TryGetProcessName(process);
                    var path = TryGetProcessPath(process);
                    if (!LooksLikeRaiaProcess(name, path, commandLine: null)) continue;

                    snapshots[process.Id] = new RaiaProcessSnapshot(
                        process.Id,
                        name ?? "",
                        path);
                }
            }

            var now = DateTime.UtcNow;
            if (now - _lastWmiProcessScan >= WmiScanThrottle)
            {
                _lastWmiProcessScan = now;
                try
                {
                    using var searcher = new ManagementObjectSearcher(
                        "SELECT ProcessId, Name, ExecutablePath, CommandLine FROM Win32_Process");

                    foreach (ManagementObject item in searcher.Get())
                    {
                        using (item)
                        {
                            var processIdValue = item["ProcessId"];
                            if (processIdValue == null) continue;

                            var processId = Convert.ToInt32(processIdValue);
                            if (processId == Environment.ProcessId) continue;

                            var name = Convert.ToString(item["Name"]) ?? "";
                            var path = Convert.ToString(item["ExecutablePath"]);
                            var commandLine = Convert.ToString(item["CommandLine"]);

                            if (!LooksLikeRaiaProcess(name, path, commandLine)) continue;

                            snapshots[processId] = new RaiaProcessSnapshot(processId, name, path);
                        }
                    }
                }
                catch
                {
                }
            }

            return snapshots.Values
                .OrderBy(p => p.ProcessId)
                .ToList();
        }

        private static string? TryGetProcessName(Process process)
        {
            try { return process.ProcessName; }
            catch { return null; }
        }

        private static string? TryGetProcessPath(Process process)
        {
            try
            {
                var path = StreamGuard.Helpers.WindowEnumerator.TryGetExecutablePathFast(process.Id);
                return string.IsNullOrEmpty(path) ? null : path;
            }
            catch { return null; }
        }

        private bool LooksLikeRaiaProcess(string? processName, string? executablePath, string? commandLine)
        {
            var normalizedName = Path.GetFileNameWithoutExtension(processName ?? "");
            if (ContainsRaia(normalizedName)) return true;
            if (normalizedName.Equals("window_raia_c", StringComparison.OrdinalIgnoreCase)) return true;

            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                if (LooksLikeRaiaExecutablePath(executablePath)) return true;
                if (HasKnownRaiaHash(executablePath)) return true;
            }

            if (!string.IsNullOrWhiteSpace(commandLine))
            {
                return commandLine.IndexOf("window_raia_c", StringComparison.OrdinalIgnoreCase) >= 0
                    || commandLine.IndexOf("raia-auth", StringComparison.OrdinalIgnoreCase) >= 0
                    || commandLine.IndexOf("raia_v", StringComparison.OrdinalIgnoreCase) >= 0
                    || commandLine.IndexOf("raia-v", StringComparison.OrdinalIgnoreCase) >= 0
                    || commandLine.IndexOf(@"\RaiaV", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return false;
        }

        private static bool ContainsRaia(string? value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.IndexOf("raia", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetRaiaAuthDir()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RaiaAuth");
        }

        private static string GetRaiaRecordDir()
        {
            return Path.Combine(GetRaiaAuthDir(), "ScreenRecords");
        }

        private static string GetRaiaWebStorageDir()
        {
            return Path.Combine(GetRaiaAuthDir(), "WebView2");
        }

        private static IEnumerable<string> GetRaiaSessionPaths()
        {
            yield return Path.Combine(GetRaiaAuthDir(), "token.dat");
            yield return Path.Combine(GetRaiaAuthDir(), "working_folder.txt");
        }

        private void StartProcessStartWatcher()
        {
            try
            {
                _processStartWatcher = new ManagementEventWatcher(
                    new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
                _processStartWatcher.EventArrived += (_, args) =>
                {
                    var processName = Convert.ToString(args.NewEvent.Properties["ProcessName"]?.Value) ?? "";
                    if (!LooksLikeRaiaProcessName(processName)) return;
                    TriggerAsyncBlock($"process-start watcher saw {processName}");
                };
                _processStartWatcher.Start();
            }
            catch
            {
                _processStartWatcher?.Dispose();
                _processStartWatcher = null;
            }
        }

        private void StartRecordWatcher()
        {
            try
            {
                Directory.CreateDirectory(GetRaiaRecordDir());
                _raiaAuthWatcher = new FileSystemWatcher(GetRaiaAuthDir())
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName
                        | NotifyFilters.DirectoryName
                        | NotifyFilters.LastWrite
                        | NotifyFilters.Size
                        | NotifyFilters.CreationTime
                };
                _raiaAuthWatcher.Created += (_, args) => OnRaiaArtifactTouched(args.FullPath);
                _raiaAuthWatcher.Changed += (_, args) => OnRaiaArtifactTouched(args.FullPath);
                _raiaAuthWatcher.Renamed += (_, args) => OnRaiaArtifactTouched(args.FullPath);
                _raiaAuthWatcher.EnableRaisingEvents = true;
            }
            catch
            {
                _raiaAuthWatcher?.Dispose();
                _raiaAuthWatcher = null;
            }
        }

        private void StopWatchers()
        {
            try { _processStartWatcher?.Stop(); } catch { }
            _processStartWatcher?.Dispose();
            _processStartWatcher = null;

            if (_raiaAuthWatcher != null)
            {
                _raiaAuthWatcher.EnableRaisingEvents = false;
                _raiaAuthWatcher.Dispose();
                _raiaAuthWatcher = null;
            }
        }

        private void OnRaiaArtifactTouched(string path)
        {
            lock (_scanLock)
            {
                if (_disposed || !IsActive || string.IsNullOrWhiteSpace(path)) return;
                if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && LooksLikeRaiaExecutablePath(path))
                {
                    Publish(0, $"record watcher saw RAIA executable artifact: {Path.GetFileName(path)}");
                    TriggerAsyncBlock("record watcher saw RAIA executable artifact");
                    return;
                }

                if (path.IndexOf(@"\ScreenRecords", StringComparison.OrdinalIgnoreCase) < 0) return;

                var cleaned = CleanDirectory(GetRaiaRecordDir());
                if (cleaned > 0)
                    Publish(0, $"record watcher removed {cleaned} new RAIA record item(s)");

                TriggerAsyncBlock("record watcher saw RAIA file activity");
            }
        }

        private void TriggerAsyncBlock(string reason)
        {
            if (_disposed || !IsActive) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (_disposed || !IsActive) return;
                var status = BlockNow();
                if (!_disposed && IsActive && status.LastContained == 0)
                    Publish(0, $"{reason}; {status.Message}");
            });
        }

        private static bool LooksLikeRaiaProcessName(string processName)
        {
            var name = Path.GetFileNameWithoutExtension(processName);
            return ContainsRaia(name)
                || name.Equals("window_raia_c", StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyLocalBlocks(IReadOnlyCollection<RaiaProcessSnapshot> raiaProcesses, ICollection<string> details)
        {
            var recordDir = GetRaiaRecordDir();

            var cleaned = CleanDirectory(recordDir);
            if (cleaned > 0)
                details.Add($"cleaned {cleaned} RAIA record item(s)");

            if (TryDenyWrites(recordDir))
                details.Add("locked RAIA ScreenRecords");

            var webStorageCleaned = CleanDirectory(GetRaiaWebStorageDir());
            if (webStorageCleaned > 0)
                details.Add($"cleared {webStorageCleaned} RAIA WebView storage item(s)");

            if (TryDenyWrites(GetRaiaWebStorageDir()))
                details.Add("locked RAIA WebView storage");

            var isolatedSessions = 0;
            foreach (var path in GetRaiaSessionPaths())
            {
                EnsureFile(path);
                if (TryDenyFileAccess(path))
                    isolatedSessions++;
            }
            if (isolatedSessions > 0)
                details.Add($"isolated {isolatedSessions} RAIA session file(s)");

            if (ApplyCurrentUserPrivacySettings())
                details.Add($"{GetDevicePrivacySummary()}; denied graphics capture for current user");

            var perAppDenies = ApplyPerAppPrivacyDeniesForNewExecutables(raiaProcesses, details: null);
            if (perAppDenies > 0)
                details.Add($"applied {perAppDenies} per-app RAIA privacy deny rule(s)");

            ApplyNetworkBlocks(raiaProcesses, details);
        }

        private int ApplyPerAppPrivacyDeniesForNewExecutables(
            IReadOnlyCollection<RaiaProcessSnapshot> raiaProcesses,
            ICollection<string>? details)
        {
            return ApplyPerAppPrivacySettingsForExecutables(GetRaiaExecutableCandidates(raiaProcesses), details);
        }

        private int ApplyPerAppPrivacySettingsForExecutables(
            IEnumerable<string> executables,
            ICollection<string>? details = null)
        {
            var pending = new Dictionary<string, List<PrivacyCapability>>(StringComparer.OrdinalIgnoreCase);
            var capabilities = GetPerAppPrivacyCapabilities();
            foreach (var exe in executables)
            {
                foreach (var capability in capabilities)
                {
                    var capabilityKey = $"{exe}|{capability.Name}|{capability.Value}";
                    if (!_privacyConfiguredExecutableCapabilities.Add(capabilityKey))
                        continue;

                    if (!pending.TryGetValue(exe, out var pendingCapabilities))
                    {
                        pendingCapabilities = new List<PrivacyCapability>();
                        pending[exe] = pendingCapabilities;
                    }

                    pendingCapabilities.Add(capability);
                }
            }

            if (pending.Count == 0) return 0;

            var applied = ApplyCurrentUserPerAppPrivacySettings(pending);
            if (applied > 0)
                details?.Add($"updated {applied} per-app RAIA privacy rule(s)");

            return applied;
        }

        private void ApplyNetworkBlocks(
            IReadOnlyCollection<RaiaProcessSnapshot> raiaProcesses,
            ICollection<string> details)
        {
            if (!IsRunningElevated())
            {
                if (!_networkAdminWarningSent)
                {
                    details.Add("network lock skipped - run Driver Booster as Administrator");
                    _networkAdminWarningSent = true;
                }
                return;
            }

            _networkAdminWarningSent = false;

            if (!_allowRulesRemoved)
            {
                RemoveRaiaAllowFirewallRules(GetRaiaExecutableCandidates(raiaProcesses));
                _allowRulesRemoved = true;
                details.Add("RAIA allow firewall rules removed if present");
            }

            var executableBlocks = EnsureProgramFirewallBlocks(GetRaiaExecutableCandidates(raiaProcesses));
            if (executableBlocks > 0)
                details.Add($"ensured firewall block for {executableBlocks} RAIA executable(s)");

            RemoveRaiaRemoteFirewallRules();

            if (BlockKnownWebsites)
            {
                var selectedHosts = GetSelectedWebsiteHosts();
                if (!_hostsBlockApplied || !TestRaiaHostsBlock(selectedHosts))
                {
                    if (WriteRaiaHostsBlock(selectedHosts))
                    {
                        _hostsBlockApplied = true;
                        _hostsBlockFailed = false;
                        details.Add($"website block applied: {KnownBlockedWebsites}");
                    }
                    else if (!_hostsBlockFailed)
                    {
                        _hostsBlockFailed = true;
                        details.Add("RAIA websites block failed");
                    }
                }
            }
            else if (TestRaiaHostsBlock() && RemoveRaiaHostsBlock())
            {
                _hostsBlockApplied = false;
                details.Add("RAIA websites unblocked in hosts");
            }
        }

        private int EnsureProgramFirewallBlocks(IEnumerable<string> executables)
        {
            var pending = executables
                .Where(exe => !string.IsNullOrWhiteSpace(exe))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(exe => _firewalledExecutables.Add(exe))
                .ToList();

            if (pending.Count == 0) return 0;

            var script = new StringBuilder();
            script.AppendLine("$ErrorActionPreference = 'Stop'");
            foreach (var exe in pending)
            {
                var token = ShortStableHash(exe);
                AppendEnsureProgramFirewallRule(script, $"{FirewallPrefix} {token} Outbound", "Outbound", exe);
                AppendEnsureProgramFirewallRule(script, $"{FirewallPrefix} {token} Inbound", "Inbound", exe);
            }

            var result = RunPowerShell(script.ToString(), timeoutMs: 15000);
            if (result.ExitCode == 0)
                return pending.Count;

            // Dual-Engine Fallback: netsh.exe advfirewall
            var netshCount = 0;
            foreach (var exe in pending)
            {
                var token = ShortStableHash(exe);
                var outArgs = NetshFirewallHelper.FormatAddProgramRuleArgs($"{FirewallPrefix} {token} Outbound", exe, "Outbound");
                var inArgs = NetshFirewallHelper.FormatAddProgramRuleArgs($"{FirewallPrefix} {token} Inbound", exe, "Inbound");
                var outRes = NetshFirewallHelper.RunNetsh(outArgs, 5000);
                var inRes = NetshFirewallHelper.RunNetsh(inArgs, 5000);
                if (outRes == 0 || inRes == 0)
                {
                    netshCount++;
                }
                else
                {
                    _firewalledExecutables.Remove(exe);
                }
            }
            return netshCount;
        }

        private int EnsureRemoteIpFirewallBlocks(IEnumerable<string> remoteAddresses)
        {
            var pending = remoteAddresses
                .Where(IsRemoteAddressBlockCandidate)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(address => _blockedRemoteAddresses.Add(address))
                .ToList();

            if (pending.Count == 0) return 0;

            var script = new StringBuilder();
            script.AppendLine("$ErrorActionPreference = 'Stop'");
            foreach (var address in pending)
            {
                var token = ShortStableHash(address);
                AppendEnsureRemoteFirewallRule(script, $"{RemoteFirewallPrefix} {token}", address);
            }

            var result = RunPowerShell(script.ToString(), timeoutMs: 15000);
            if (result.ExitCode == 0)
                return pending.Count;

            // Dual-Engine Fallback: netsh.exe advfirewall
            var netshCount = 0;
            foreach (var address in pending)
            {
                var token = ShortStableHash(address);
                var args = NetshFirewallHelper.FormatAddRemoteIpRuleArgs($"{RemoteFirewallPrefix} {token}", address);
                if (NetshFirewallHelper.RunNetsh(args, 5000) == 0)
                {
                    netshCount++;
                }
                else
                {
                    _blockedRemoteAddresses.Remove(address);
                }
            }
            return netshCount;
        }

        private static void AppendEnsureProgramFirewallRule(
            StringBuilder script,
            string name,
            string direction,
            string programPath)
        {
            script.Append("if (-not (Get-NetFirewallRule -DisplayName ");
            script.Append(PsQuote(name));
            script.AppendLine(" -ErrorAction SilentlyContinue)) {");
            script.Append("  New-NetFirewallRule -DisplayName ");
            script.Append(PsQuote(name));
            script.Append(" -Direction ");
            script.Append(direction);
            script.Append(" -Action Block -Program ");
            script.Append(PsQuote(programPath));
            script.AppendLine(" -Profile Any | Out-Null");
            script.AppendLine("}");
        }

        private static void AppendEnsureRemoteFirewallRule(
            StringBuilder script,
            string name,
            string remoteAddress)
        {
            script.Append("if (-not (Get-NetFirewallRule -DisplayName ");
            script.Append(PsQuote(name));
            script.AppendLine(" -ErrorAction SilentlyContinue)) {");
            script.Append("  New-NetFirewallRule -DisplayName ");
            script.Append(PsQuote(name));
            script.Append(" -Direction Outbound -Action Block -RemoteAddress ");
            script.Append(PsQuote(remoteAddress));
            script.AppendLine(" -Profile Any | Out-Null");
            script.AppendLine("}");
        }

        private static bool RemoveRaiaAllowFirewallRules(IEnumerable<string>? candidates = null)
        {
            var script = @"
$ErrorActionPreference = 'Stop'
Get-NetFirewallRule -ErrorAction SilentlyContinue |
    Where-Object { $_.Action -eq 'Allow' -and $_.DisplayName -match '(?i)raia' } |
    Remove-NetFirewallRule
";
            var result = RunPowerShell(script, timeoutMs: 10000);
            if (result.ExitCode == 0) return true;

            // Dual-Engine Fallback: Delete matching program rules via netsh
            if (candidates != null)
            {
                foreach (var exe in candidates)
                {
                    var args = NetshFirewallHelper.FormatDeleteRulesByProgramArgs(exe);
                    NetshFirewallHelper.RunNetsh(args, 3000);
                }
            }
            return true;
        }

        private bool RemoveRaiaRemoteFirewallRules()
        {
            var script = $@"
$ErrorActionPreference = 'Stop'
Get-NetFirewallRule -DisplayName {PsQuote(RemoteFirewallPrefix + "*")} -ErrorAction SilentlyContinue |
    Remove-NetFirewallRule
";
            var result = RunPowerShell(script, timeoutMs: 10000);
            if (result.ExitCode == 0) return true;

            // Dual-Engine Fallback: Delete known remote rules via netsh
            foreach (var address in _blockedRemoteAddresses.ToList())
            {
                var token = ShortStableHash(address);
                var args = NetshFirewallHelper.FormatDeleteRuleByNameArgs($"{RemoteFirewallPrefix} {token}");
                NetshFirewallHelper.RunNetsh(args, 3000);
            }
            return true;
        }

        private static IReadOnlyCollection<string> GetRemoteAddressesForProcesses(IEnumerable<int> processIds)
        {
            var ids = processIds
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (ids.Count == 0) return Array.Empty<string>();

            var script = $@"
$ids = @({string.Join(",", ids)})
Get-NetTCPConnection -ErrorAction SilentlyContinue |
    Where-Object {{ $ids -contains [int]$_.OwningProcess -and $_.RemoteAddress }} |
    Select-Object -ExpandProperty RemoteAddress -Unique
";
            var result = RunPowerShell(script, timeoutMs: 8000);
            if (result.ExitCode != 0) return Array.Empty<string>();

            return result.StandardOutput
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(IsRemoteAddressBlockCandidate)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsRemoteAddressBlockCandidate(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return false;
            if (address.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase)) return false;
            if (address.Equals("::", StringComparison.OrdinalIgnoreCase)) return false;
            if (address.StartsWith("127.", StringComparison.OrdinalIgnoreCase)) return false;
            if (address.StartsWith("169.254.", StringComparison.OrdinalIgnoreCase)) return false;
            if (address.StartsWith("::ffff:127.", StringComparison.OrdinalIgnoreCase)) return false;

            return !IPAddress.TryParse(address, out var ipAddress)
                || !IPAddress.IsLoopback(ipAddress);
        }

        private bool WriteRaiaHostsBlock(IReadOnlyCollection<string> hosts)
        {
            lock (AiTrafficProtectionService.HostsLock)
            {
                try
                {
                    if (hosts.Count == 0)
                        return RemoveRaiaHostsBlock();

                    var hostsPath = GetHostsPath();
                    var text = File.Exists(hostsPath) ? File.ReadAllText(hostsPath) : "";
                    var pattern = $@"(?ms)\r?\n?{Regex.Escape(HostsStartMarker)}.*?{Regex.Escape(HostsEndMarker)}\r?\n?";
                    var clean = Regex.Replace(text, pattern, Environment.NewLine).TrimEnd();
                    var blockLines = hosts.SelectMany(host => new[]
                    {
                        $"127.0.0.1 {host}",
                        $"::1 {host}"
                    });

                    var newText = clean
                        + Environment.NewLine
                        + HostsStartMarker
                        + Environment.NewLine
                        + string.Join(Environment.NewLine, blockLines)
                        + Environment.NewLine
                        + HostsEndMarker
                        + Environment.NewLine;

                    File.WriteAllText(hostsPath, newText, Encoding.UTF8);
                    RunProcess("ipconfig.exe", 2500, "/flushdns");
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool RemoveRaiaHostsBlock()
        {
            lock (AiTrafficProtectionService.HostsLock)
            {
                try
                {
                    var hostsPath = GetHostsPath();
                    if (!File.Exists(hostsPath)) return false;

                    var text = File.ReadAllText(hostsPath);
                    var pattern = $@"(?ms)\r?\n?{Regex.Escape(HostsStartMarker)}.*?{Regex.Escape(HostsEndMarker)}\r?\n?";
                    var clean = Regex.Replace(text, pattern, Environment.NewLine).TrimEnd() + Environment.NewLine;

                    if (clean == text) return false;

                    File.WriteAllText(hostsPath, clean, Encoding.UTF8);
                    RunProcess("ipconfig.exe", 2500, "/flushdns");
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        private bool TestRaiaHostsBlock()
        {
            lock (AiTrafficProtectionService.HostsLock)
            {
                try
                {
                    var hostsPath = GetHostsPath();
                    if (!File.Exists(hostsPath)) return false;

                    var text = File.ReadAllText(hostsPath);
                    return text.Contains(HostsStartMarker, StringComparison.Ordinal)
                        && text.Contains(HostsEndMarker, StringComparison.Ordinal);
                }
                catch
                {
                    return false;
                }
            }
        }

        private static bool TestRaiaHostsBlock(IReadOnlyCollection<string> expectedHosts)
        {
            lock (AiTrafficProtectionService.HostsLock)
            {
                try
                {
                    if (expectedHosts.Count == 0) return false;
                    var hostsPath = GetHostsPath();
                    if (!File.Exists(hostsPath)) return false;

                    var text = File.ReadAllText(hostsPath);
                    return text.Contains(HostsStartMarker, StringComparison.Ordinal)
                        && text.Contains(HostsEndMarker, StringComparison.Ordinal)
                        && expectedHosts.All(host =>
                            text.Contains($"127.0.0.1 {host}", StringComparison.OrdinalIgnoreCase)
                            && text.Contains($"::1 {host}", StringComparison.OrdinalIgnoreCase));
                }
                catch
                {
                    return false;
                }
            }
        }

        private IReadOnlyCollection<string> GetSelectedWebsiteHosts()
        {
            var hosts = new List<string>();
            if (BlockRaiaWebsite)
                hosts.Add(RaiaWebsiteHost);
            if (BlockPortalWebsite)
                hosts.Add(PortalWebsiteHost);
            return hosts;
        }

        private static string GetHostsPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32",
                "drivers",
                "etc",
                "hosts");
        }

        private static int CleanDirectory(string path)
        {
            var cleaned = 0;
            try
            {
                Directory.CreateDirectory(path);
                foreach (var entry in Directory.EnumerateFileSystemEntries(path))
                {
                    try
                    {
                        if (Directory.Exists(entry))
                            Directory.Delete(entry, recursive: true);
                        else
                            File.Delete(entry);
                        cleaned++;
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            return cleaned;
        }

        private static IEnumerable<string> GetRaiaExecutableCandidates(
            IReadOnlyCollection<RaiaProcessSnapshot>? runningProcesses = null)
        {
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var process in runningProcesses ?? Array.Empty<RaiaProcessSnapshot>())
            {
                if (!string.IsNullOrWhiteSpace(process.ExecutablePath))
                    AddIfExists(process.ExecutablePath, candidates);
            }

            AddCandidateFromParents(AppContext.BaseDirectory, candidates);
            AddCandidateFromParents(Environment.CurrentDirectory, candidates);

            AddIfExists(Path.Combine(GetRaiaAuthDir(), "window_raia_c.exe"), candidates);
            AddIfExists(Path.Combine(GetRaiaAuthDir(), "RaiaV3", "window_raia_c.exe"), candidates);
            AddKnownRaiaExecutablesFromDirectory(GetRaiaAuthDir(), candidates);

            return candidates;
        }

        private static void AddCandidateFromParents(string startDirectory, ISet<string> candidates)
        {
            if (string.IsNullOrWhiteSpace(startDirectory)) return;

            DirectoryInfo? dir;
            try
            {
                dir = new DirectoryInfo(startDirectory);
            }
            catch
            {
                return;
            }

            for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                AddIfExists(Path.Combine(dir.FullName, "RaiaV3", "window_raia_c.exe"), candidates);
                AddRaiaVersionExecutablesFromDirectory(dir.FullName, candidates);
            }
        }

        private static void AddRaiaVersionExecutablesFromDirectory(string parentDirectory, ISet<string> candidates)
        {
            try
            {
                if (!Directory.Exists(parentDirectory)) return;
                foreach (var directory in Directory.EnumerateDirectories(parentDirectory, "RaiaV*", SearchOption.TopDirectoryOnly))
                {
                    AddKnownRaiaExecutablesFromDirectory(directory, candidates);
                }
            }
            catch
            {
            }
        }

        private static void AddIfExists(string path, ISet<string> candidates)
        {
            try
            {
                if (File.Exists(path))
                    candidates.Add(path);
            }
            catch
            {
            }
        }

        private static void AddKnownRaiaExecutablesFromDirectory(string directory, ISet<string> candidates)
        {
            try
            {
                if (!Directory.Exists(directory)) return;
                foreach (var exe in Directory.EnumerateFiles(directory, "*.exe", SearchOption.AllDirectories))
                {
                    if (LooksLikeRaiaExecutablePath(exe) || HasKnownRaiaHashUncached(exe))
                        candidates.Add(exe);
                }
            }
            catch
            {
            }
        }

        private static bool LooksLikeRaiaExecutablePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            var fileName = Path.GetFileNameWithoutExtension(path);
            return ContainsRaia(fileName)
                || fileName.Equals("window_raia_c", StringComparison.OrdinalIgnoreCase)
                || LooksLikeRaiaInstallPath(path);
        }

        private static bool LooksLikeRaiaInstallPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            var normalized = path.Replace('/', '\\');
            return normalized.IndexOf(@"\RaiaAuth\", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf(@"\RaiaV", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool HasKnownRaiaHash(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;

            string? hash;
            lock (_hashCache)
            {
                if (!_hashCache.TryGetValue(path, out hash))
                {
                    hash = ComputeSha256(path);
                    _hashCache[path] = hash;
                }
            }

            return hash != null && KnownSha256Hashes.Contains(hash);
        }

        private static bool HasKnownRaiaHashUncached(string path)
        {
            var hash = ComputeSha256(path);
            return hash != null && KnownSha256Hashes.Contains(hash);
        }

        private static string? ComputeSha256(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return Convert.ToHexString(SHA256.HashData(stream));
            }
            catch
            {
                return null;
            }
        }

        private static bool TryDenyWrites(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                return RunIcacls(path, "/deny", $"{GetCurrentAccount()}:(OI)(CI)(W,D,AD,DC)") == 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryDenyFileAccess(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                return RunIcacls(path, "/deny", $"{GetCurrentAccount()}:(R,W,D,AD,DC)") == 0;
            }
            catch
            {
                return false;
            }
        }

        private static void EnsureFile(string path)
        {
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);
                if (!File.Exists(path))
                    using (File.Create(path)) { }
            }
            catch
            {
            }
        }

        private bool ApplyCurrentUserPrivacySettings()
        {
            var changed = false;
            changed |= ApplyCurrentUserDevicePrivacySettings();
            changed |= TrySetCurrentUserRegistryValue(
                @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\graphicsCaptureProgrammatic",
                "Value",
                "Deny",
                RegistryValueKind.String);
            changed |= TrySetCurrentUserRegistryValue(
                @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\graphicsCaptureWithoutBorder",
                "Value",
                "Deny",
                RegistryValueKind.String);
            changed |= TrySetCurrentUserRegistryValue(
                @"Software\Microsoft\Windows\CurrentVersion\GameDVR",
                "AppCaptureEnabled",
                0,
                RegistryValueKind.DWord);
            changed |= TrySetCurrentUserRegistryValue(
                @"System\GameConfigStore",
                "GameDVR_Enabled",
                0,
                RegistryValueKind.DWord);
            changed |= TrySetCurrentUserRegistryValue(
                @"Software\Policies\Microsoft\TabletPC",
                "DisableSnippingTool",
                1,
                RegistryValueKind.DWord);

            return changed;
        }

        private bool ApplyCurrentUserDevicePrivacySettings()
        {
            var changed = false;
            changed |= TrySetLocalMachineRegistryValue(
                @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy",
                "LetAppsAccessCamera",
                BlockCameraAccess ? 2 : 0,
                RegistryValueKind.DWord);
            changed |= TrySetCurrentUserRegistryValue(
                @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam",
                "Value",
                BlockCameraAccess ? "Deny" : "Allow",
                RegistryValueKind.String);
            changed |= TrySetLocalMachineRegistryValue(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam",
                "Value",
                BlockCameraAccess ? "Deny" : "Allow",
                RegistryValueKind.String);
            changed |= TrySetLocalMachineRegistryValue(
                @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy",
                "LetAppsAccessMicrophone",
                BlockMicrophoneAccess ? 2 : 0,
                RegistryValueKind.DWord);
            changed |= TrySetCurrentUserRegistryValue(
                @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone",
                "Value",
                BlockMicrophoneAccess ? "Deny" : "Allow",
                RegistryValueKind.String);
            changed |= TrySetLocalMachineRegistryValue(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone",
                "Value",
                BlockMicrophoneAccess ? "Deny" : "Allow",
                RegistryValueKind.String);

            return changed;
        }

        private string GetDevicePrivacySummary()
        {
            return $"camera {(BlockCameraAccess ? "denied" : "allowed")}, microphone {(BlockMicrophoneAccess ? "denied" : "allowed")}";
        }

        private static bool TryEnableCameraDevices()
        {
            var script = @"
$ErrorActionPreference = 'SilentlyContinue'
$devices = @(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object {
    $_.Class -eq 'Camera' -or
    ($_.Class -eq 'Image' -and $_.FriendlyName -match 'camera|webcam|integrated')
})
foreach ($device in $devices) {
    if ($device.InstanceId) {
        Enable-PnpDevice -InstanceId $device.InstanceId -Confirm:$false -ErrorAction SilentlyContinue | Out-Null
    }
}
";
            return RunPowerShell(script, timeoutMs: 12000).ExitCode == 0;
        }

        private static bool TrySetCurrentUserRegistryValue(string subKey, string name, object value, RegistryValueKind kind)
        {
            return TrySetRegistryValue(Registry.CurrentUser, subKey, name, value, kind);
        }

        private static bool TrySetLocalMachineRegistryValue(string subKey, string name, object value, RegistryValueKind kind)
        {
            return TrySetRegistryValue(Registry.LocalMachine, subKey, name, value, kind);
        }

        private static bool TrySetRegistryValue(RegistryKey root, string subKey, string name, object value, RegistryValueKind kind)
        {
            try
            {
                using var key = root.CreateSubKey(subKey, writable: true);
                if (key == null)
                    return false;

                var existing = key.GetValue(name);
                if (ValuesEqual(existing, value))
                    return false;

                key.SetValue(name, value, kind);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool ValuesEqual(object? existing, object value)
        {
            if (existing == null)
                return false;

            if (existing is int existingInt && value is int valueInt)
                return existingInt == valueInt;

            return string.Equals(
                Convert.ToString(existing),
                Convert.ToString(value),
                StringComparison.OrdinalIgnoreCase);
        }

        private IReadOnlyCollection<PrivacyCapability> GetPerAppPrivacyCapabilities()
        {
            var capabilities = new List<PrivacyCapability>
            {
                new("webcam", BlockCameraAccess ? "Deny" : "Allow"),
                new("microphone", BlockMicrophoneAccess ? "Deny" : "Allow"),
                new("graphicsCaptureProgrammatic", "Deny"),
                new("graphicsCaptureWithoutBorder", "Deny")
            };

            return capabilities;
        }

        private static int ApplyCurrentUserPerAppPrivacySettings(
            IReadOnlyDictionary<string, List<PrivacyCapability>> executableCapabilities)
        {
            var applied = 0;

            foreach (var item in executableCapabilities)
            {
                var appKey = ConvertToConsentStoreAppKey(item.Key);
                foreach (var capability in item.Value)
                {
                    var subKey = $@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{capability.Name}\NonPackaged\{appKey}";
                    if (TrySetCurrentUserRegistryValue(subKey, "Value", capability.Value, RegistryValueKind.String))
                        applied++;
                    if (TrySetLocalMachineRegistryValue(subKey, "Value", capability.Value, RegistryValueKind.String))
                        applied++;
                }
            }

            return applied;
        }

        private static string ConvertToConsentStoreAppKey(string path)
        {
            try
            {
                return Path.GetFullPath(path).Replace('\\', '#');
            }
            catch
            {
                return path.Replace('\\', '#');
            }
        }

        private static string GetCurrentAccount()
        {
            return WindowsIdentity.GetCurrent().Name;
        }

        private static bool IsRunningElevated()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private static string PsQuote(string value)
        {
            return "'" + value.Replace("'", "''") + "'";
        }

        private static string ShortStableHash(string value)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes)[..12];
        }

        private static PowerShellResult RunPowerShell(string script, int timeoutMs)
        {
            try
            {
                var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                using var process = new Process();
                process.StartInfo.FileName = "powershell.exe";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.ArgumentList.Add("-NoProfile");
                process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
                process.StartInfo.ArgumentList.Add("Bypass");
                process.StartInfo.ArgumentList.Add("-EncodedCommand");
                process.StartInfo.ArgumentList.Add(encoded);

                var result = ChildProcessRunner.Run(process.StartInfo, timeoutMs);
                return new PowerShellResult(result.ExitCode, result.StandardOutput, result.StandardError);
            }
            catch (Exception ex)
            {
                return new PowerShellResult(-1, string.Empty, ex.Message);
            }
        }

        private static int RunProcess(string fileName, int timeoutMs, params string[] args)
        {
            var info = new ProcessStartInfo(fileName);
            foreach (var argument in args) info.ArgumentList.Add(argument);
            return ChildProcessRunner.Run(info, timeoutMs).ExitCode;
        }

        private static int RunIcacls(string path, params string[] args) =>
            RunProcess("icacls.exe", 2500, new[] { path }.Concat(args).ToArray());

        private ThreatBlockerStatus Publish(int lastContained, string message)
        {
            var status = new ThreatBlockerStatus(IsActive, TotalContained, lastContained, message, DateTime.Now);
            if (!_disposed) StatusChanged?.Invoke(status);
            return status;
        }

        public void Dispose()
        {
            lock (_scanLock)
            {
                if (_disposed) return;
                _disposed = true;
                _isActive = false;
            }
            _scanTimer.Stop();
            _scanTimer.Dispose();
            StopWatchers();
        }
    }
}
