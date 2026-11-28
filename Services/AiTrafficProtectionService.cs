using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;

namespace StreamGuard.Services;

public sealed record ProxyAssessment(
    bool IsProxyEnabled,
    string? ProxyServer,
    string? AutoConfigUrl,
    bool IsRogueProxy,
    string? Reason
)
{
    public static ProxyAssessment Clean { get; } = new(false, null, null, false, null);
}

public sealed record RogueCertificateInfo(
    string Subject,
    string Issuer,
    string Thumbprint,
    string StoreLocationName,
    DateTime NotBefore,
    DateTime NotAfter
);

public sealed record AiHostsAssessment(
    bool IsTampered,
    IReadOnlyList<string> HijackedEntries
)
{
    public static AiHostsAssessment Clean { get; } = new(false, Array.Empty<string>());
}

public sealed record EnvProxyAssessment(
    bool HasRogueEnvVars,
    IReadOnlyList<string> DetectedVariables,
    bool HasTlsBypassOrCustomCa,
    string? Reason
)
{
    public static EnvProxyAssessment Clean { get; } = new(false, Array.Empty<string>(), false, null);
}

public sealed record WinHttpProxyAssessment(
    bool IsRogueWinHttpProxy,
    string? ProxyServer,
    string? Reason
)
{
    public static WinHttpProxyAssessment Clean { get; } = new(false, null, null);
}

public sealed record AiTrafficAssessment(
    ProxyAssessment Proxy,
    IReadOnlyList<RogueCertificateInfo> RogueCertificates,
    IReadOnlyList<CaptureTool> ActiveSniffers,
    AiHostsAssessment Hosts,
    EnvProxyAssessment? EnvProxy = null,
    WinHttpProxyAssessment? WinHttpProxy = null
)
{
    public bool IsAllSafe =>
        !Proxy.IsRogueProxy &&
        RogueCertificates.Count == 0 &&
        ActiveSniffers.Count == 0 &&
        !Hosts.IsTampered &&
        !(EnvProxy?.HasRogueEnvVars ?? false) &&
        !(EnvProxy?.HasTlsBypassOrCustomCa ?? false) &&
        !(WinHttpProxy?.IsRogueWinHttpProxy ?? false);
}

public sealed class AiTrafficProtectionService
{
    public const string InternetSettingsSubKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
    private const int INTERNET_OPTION_REFRESH = 37;

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DnsFlushResolverCacheNative();

    public static readonly string[] KnownMitmKeywords =
    {
        "fiddler",
        "do_not_trust_fiddlerroot",
        "charles",
        "mitmproxy",
        "portswigger",
        "httpdebugger",
        "http debugger",
        "proxifier",
        "burpsuite",
        "burp",
        "reqable",
        "whistle",
        "proxyman",
        "telerik"
    };

    public static readonly string[] ProtectedAiEndpoints =
    {
        "generativelanguage.googleapis.com",
        "gemini.google.com",
        "aiplatform.googleapis.com",
        "cloudcode-pa.googleapis.com",
        "daily-cloudcode-pa.sandbox.googleapis.com",
        "oauth2.googleapis.com",
        "accounts.google.com",
        "api.openai.com",
        "api.anthropic.com",
        "chatgpt.com",
        "claude.ai"
    };

    public static readonly string[] ProxyEnvVarNames =
    {
        "HTTP_PROXY",
        "HTTPS_PROXY",
        "ALL_PROXY",
        "http_proxy",
        "https_proxy",
        "all_proxy"
    };

    public static readonly string[] TlsOverrideEnvVarNames =
    {
        "NODE_EXTRA_CA_CERTS",
        "SSL_CERT_FILE",
        "REQUESTS_CA_BUNDLE",
        "NODE_TLS_REJECT_UNAUTHORIZED"
    };

    private static readonly string[] CommonProxyPorts =
    {
        ":8888",
        ":8080",
        ":8081",
        ":8889",
        ":9090",
        ":1080",
        ":10808",
        ":10809"
    };

    public static bool NotifyInternetSettingsChanged()
    {
        try
        {
            var b1 = InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            var b2 = InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
            return b1 || b2;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsRogueProxyAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return false;
        var lower = address.Trim().ToLowerInvariant();

        if (lower.Contains("127.0.0.1") ||
            lower.Contains("localhost") ||
            lower.Contains("::1") ||
            lower.Contains("[::1]") ||
            lower.Contains("0.0.0.0") ||
            lower.Contains("::ffff:127.") ||
            System.Text.RegularExpressions.Regex.IsMatch(lower, @"(^|[^0-9])127\.\d{1,3}\.\d{1,3}\.\d{1,3}"))
        {
            return true;
        }

        foreach (var port in CommonProxyPorts)
        {
            if (lower.Contains(port)) return true;
        }

        return false;
    }

    public ProxyAssessment CheckProxyStatus(RegistryKey? testKey = null)
    {
        try
        {
            if (testKey != null)
            {
                return EvaluateProxyKey(testKey);
            }

            using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsSubKey, writable: false);
            if (key == null)
            {
                return ProxyAssessment.Clean;
            }

            return EvaluateProxyKey(key);
        }
        catch (Exception ex)
        {
            return new ProxyAssessment(false, null, null, false, $"Error reading proxy settings: {ex.Message}");
        }
    }

    private static ProxyAssessment EvaluateProxyKey(RegistryKey key)
    {
        var proxyEnableRaw = key.GetValue("ProxyEnable");
        var isEnabled = false;
        if (proxyEnableRaw is int dwordVal)
        {
            isEnabled = dwordVal != 0;
        }
        else if (proxyEnableRaw != null)
        {
            if (int.TryParse(proxyEnableRaw.ToString(), out var parsed))
                isEnabled = parsed != 0;
        }

        var proxyServer = key.GetValue("ProxyServer") as string;
        var autoConfigUrl = key.GetValue("AutoConfigURL") as string;

        var isRogue = false;
        string? reason = null;

        if (!string.IsNullOrWhiteSpace(autoConfigUrl) && IsRogueProxyAddress(autoConfigUrl))
        {
            isRogue = true;
            reason = $"Rogue PAC auto-config URL detected: {autoConfigUrl}";
        }

        if (isEnabled)
        {
            if (IsRogueProxyAddress(proxyServer))
            {
                isRogue = true;
                reason = $"Rogue loopback proxy server detected: {proxyServer}";
            }
            else if (string.IsNullOrWhiteSpace(proxyServer) && isRogue == false)
            {
                // Proxy enabled with no server or empty - potentially hijacked or anomalous
                isRogue = true;
                reason = "Proxy enabled without configured server";
            }
            else if (!isRogue && !string.IsNullOrWhiteSpace(proxyServer))
            {
                // In context of AI shield, any local proxy server enabled is considered a rogue proxy
                var parts = proxyServer.Split(new[] { ';', '=' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    if (IsRogueProxyAddress(part))
                    {
                        isRogue = true;
                        reason = $"Rogue proxy element detected: {part}";
                        break;
                    }
                }
            }
        }

        return new ProxyAssessment(isEnabled, proxyServer, autoConfigUrl, isRogue, reason);
    }

    public bool PurgeProxySettings(RegistryKey? testKey = null, bool notifyWinInet = true)
    {
        try
        {
            if (testKey != null)
            {
                return ApplyProxyPurge(testKey, notifyWinInet: false);
            }

            using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsSubKey, writable: true);
            if (key == null) return false;

            return ApplyProxyPurge(key, notifyWinInet);
        }
        catch
        {
            return false;
        }
    }

    private static bool ApplyProxyPurge(RegistryKey key, bool notifyWinInet)
    {
        key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
        key.SetValue("ProxyServer", string.Empty, RegistryValueKind.String);

        try
        {
            if (key.GetValue("AutoConfigURL") != null)
            {
                key.DeleteValue("AutoConfigURL", throwOnMissingValue: false);
            }
        }
        catch { }

        if (notifyWinInet)
        {
            NotifyInternetSettingsChanged();
        }

        return true;
    }

    public static bool IsRogueCertificate(string? subject, string? issuer, string? friendlyName = null)
    {
        var text = $"{subject ?? ""} {issuer ?? ""} {friendlyName ?? ""}".ToLowerInvariant();
        return KnownMitmKeywords.Any(k => text.Contains(k));
    }

    public static bool IsRogueCertificate(X509Certificate2 cert)
    {
        if (cert == null) return false;
        string? friendlyName = null;
        try { friendlyName = cert.FriendlyName; } catch { }
        return IsRogueCertificate(cert.Subject, cert.Issuer, friendlyName);
    }

    private readonly object _certCacheLock = new();
    private IReadOnlyList<RogueCertificateInfo>? _cachedCerts;
    private DateTime _lastCertScanTime = DateTime.MinValue;
    private static readonly TimeSpan CertScanCacheTtl = TimeSpan.FromSeconds(30);

    public IReadOnlyList<RogueCertificateInfo> ScanRogueCertificates(
        IEnumerable<X509Certificate2>? testCertificates = null,
        bool forceRefresh = false)
    {
        if (testCertificates != null)
        {
            var testResults = new List<RogueCertificateInfo>();
            foreach (var cert in testCertificates)
            {
                if (IsRogueCertificate(cert))
                {
                    testResults.Add(new RogueCertificateInfo(
                        cert.Subject,
                        cert.Issuer,
                        cert.Thumbprint,
                        "TestStore",
                        cert.NotBefore,
                        cert.NotAfter
                    ));
                }
            }
            return testResults;
        }

        lock (_certCacheLock)
        {
            if (!forceRefresh && _cachedCerts != null && (DateTime.UtcNow - _lastCertScanTime) < CertScanCacheTtl)
            {
                return _cachedCerts;
            }

            var results = new List<RogueCertificateInfo>();
            var locations = new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine };
            foreach (var location in locations)
            {
                try
                {
                    using var store = new X509Store(StoreName.Root, location);
                    store.Open(OpenFlags.ReadOnly);
                    foreach (var cert in store.Certificates)
                    {
                        using (cert)
                        {
                            if (IsRogueCertificate(cert))
                            {
                                results.Add(new RogueCertificateInfo(
                                    cert.Subject,
                                    cert.Issuer,
                                    cert.Thumbprint,
                                    location.ToString(),
                                    cert.NotBefore,
                                    cert.NotAfter
                                ));
                            }
                        }
                    }
                }
                catch
                {
                    // Protected or restricted store access is handled safely
                }
            }

            _cachedCerts = results.AsReadOnly();
            _lastCertScanTime = DateTime.UtcNow;
            return _cachedCerts;
        }
    }

    public static readonly object HostsLock = new();
    private DateTime _lastHostsWriteTimeUtc = DateTime.MinValue;
    private DateTime _lastHostsCheckTime = DateTime.MinValue;
    private AiHostsAssessment? _cachedHostsAssessment;
    private static readonly TimeSpan HostsCheckThrottle = TimeSpan.FromSeconds(5);

    public static string GetDefaultHostsPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

    public AiHostsAssessment CheckAiHostsProtection(string? hostsPath = null, bool forceRefresh = false)
    {
        lock (HostsLock)
        {
            var isDefaultPath = hostsPath == null;
            var path = hostsPath ?? GetDefaultHostsPath();
            if (!File.Exists(path)) return AiHostsAssessment.Clean;

            try
            {
                var writeTime = File.GetLastWriteTimeUtc(path);
                if (isDefaultPath && !forceRefresh && _cachedHostsAssessment != null &&
                    writeTime == _lastHostsWriteTimeUtc &&
                    (DateTime.UtcNow - _lastHostsCheckTime) < HostsCheckThrottle)
                {
                    return _cachedHostsAssessment;
                }

                var lines = ReadAllLinesShared(path);
                var hijacked = new List<string>();

                foreach (var rawLine in lines)
                {
                    var line = rawLine.Trim();
                    if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line)) continue;

                    var commentIndex = line.IndexOf('#');
                    if (commentIndex >= 0) line = line[..commentIndex].Trim();

                    var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length < 2) continue;

                    // Check host tokens
                    for (var i = 1; i < tokens.Length; i++)
                    {
                        var host = tokens[i].Trim().ToLowerInvariant();
                        if (ProtectedAiEndpoints.Contains(host))
                        {
                            hijacked.Add(rawLine.Trim());
                            break;
                        }
                    }
                }

                var assessment = new AiHostsAssessment(hijacked.Count > 0, hijacked);
                if (isDefaultPath)
                {
                    _lastHostsWriteTimeUtc = writeTime;
                    _lastHostsCheckTime = DateTime.UtcNow;
                    _cachedHostsAssessment = assessment;
                }
                return assessment;
            }
            catch
            {
                return (isDefaultPath ? _cachedHostsAssessment : null) ?? AiHostsAssessment.Clean;
            }
        }
    }

    public bool PurgeAiHostsHijacks(string? hostsPath = null)
    {
        lock (HostsLock)
        {
            var path = hostsPath ?? GetDefaultHostsPath();
            if (!File.Exists(path)) return false;

            try
            {
                var lines = ReadAllLinesShared(path);
                var filtered = new List<string>();
                var modified = false;

                foreach (var rawLine in lines)
                {
                    var line = rawLine.Trim();
                    if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line))
                    {
                        filtered.Add(rawLine);
                        continue;
                    }

                    var commentIndex = line.IndexOf('#');
                    var hostPart = commentIndex >= 0 ? line[..commentIndex].Trim() : line;
                    var commentPart = commentIndex >= 0 ? line[commentIndex..] : string.Empty;
                    var tokens = hostPart.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

                    if (tokens.Length < 2)
                    {
                        filtered.Add(rawLine);
                        continue;
                    }

                    var ip = tokens[0];
                    var keptHosts = new List<string>();
                    var lineHadAiHijack = false;

                    for (var i = 1; i < tokens.Length; i++)
                    {
                        var host = tokens[i].Trim().ToLowerInvariant();
                        if (ProtectedAiEndpoints.Contains(host))
                        {
                            lineHadAiHijack = true;
                        }
                        else
                        {
                            keptHosts.Add(tokens[i]);
                        }
                    }

                    if (lineHadAiHijack)
                    {
                        modified = true;
                        if (keptHosts.Count > 0)
                        {
                            var reconstructed = $"{ip}\t{string.Join(" ", keptHosts)}";
                            if (!string.IsNullOrWhiteSpace(commentPart))
                                reconstructed += $" {commentPart}";
                            filtered.Add(reconstructed);
                        }
                    }
                    else
                    {
                        filtered.Add(rawLine);
                    }
                }

                if (modified)
                {
                    var isReadOnly = false;
                    FileAttributes? originalAttributes = null;
                    try
                    {
                        originalAttributes = File.GetAttributes(path);
                        if (originalAttributes.Value.HasFlag(FileAttributes.ReadOnly))
                        {
                            isReadOnly = true;
                            File.SetAttributes(path, originalAttributes.Value & ~FileAttributes.ReadOnly);
                        }
                    }
                    catch { }

                    WriteAllLinesWithRetry(path, filtered);

                    if (isReadOnly && originalAttributes.HasValue)
                    {
                        try { File.SetAttributes(path, originalAttributes.Value); } catch { }
                    }

                    _lastHostsWriteTimeUtc = File.GetLastWriteTimeUtc(path);
                    _cachedHostsAssessment = null;
                    FlushDnsResolverCache();
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private static List<string> ReadAllLinesShared(string path)
    {
        var lines = new List<string>();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            lines.Add(line);
        }
        return lines;
    }

    private static void WriteAllLinesWithRetry(string path, IEnumerable<string> lines)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(stream);
                foreach (var line in lines)
                {
                    writer.WriteLine(line);
                }
                return;
            }
            catch (IOException) when (attempt < 2)
            {
                System.Threading.Thread.Sleep(50);
            }
        }
    }

    public static void FlushDnsResolverCache()
    {
        try
        {
            if (DnsFlushResolverCacheNative())
                return;
        }
        catch
        {
            // Fallback below if dnsapi.dll is unavailable in restricted environments
        }

        try
        {
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ipconfig.exe",
                Arguments = "/flushdns",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            proc?.WaitForExit(2000);
        }
        catch { }
    }

    public EnvProxyAssessment CheckEnvironmentProxyStatus(IDictionary<string, string?>? testEnv = null)
    {
        var detected = new List<string>();
        var hasProxy = false;
        var hasTls = false;

        void InspectValue(string name, string? val, string scopeLabel)
        {
            if (string.IsNullOrWhiteSpace(val)) return;
            var trimmed = val.Trim();

            if (ProxyEnvVarNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                if (IsRogueProxyAddress(trimmed))
                {
                    hasProxy = true;
                    detected.Add($"{scopeLabel}:{name}={trimmed}");
                }
                return;
            }

            if (string.Equals(name, "NODE_TLS_REJECT_UNAUTHORIZED", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(trimmed, "0", StringComparison.Ordinal))
            {
                hasTls = true;
                detected.Add($"{scopeLabel}:{name}=0");
                return;
            }

            if (string.Equals(name, "NODE_EXTRA_CA_CERTS", StringComparison.OrdinalIgnoreCase) ||
                KnownMitmKeywords.Any(k => trimmed.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                hasTls = true;
                detected.Add($"{scopeLabel}:{name}={trimmed}");
            }
        }

        if (testEnv != null)
        {
            foreach (var kv in testEnv)
            {
                InspectValue(kv.Key, kv.Value, "Test");
            }
        }
        else
        {
            foreach (var name in ProxyEnvVarNames.Concat(TlsOverrideEnvVarNames))
            {
                try { InspectValue(name, Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process), "Process"); } catch { }
                try { InspectValue(name, Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User), "User"); } catch { }
                try { InspectValue(name, Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine), "Machine"); } catch { }
            }
        }

        var distinct = detected.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (distinct.Length == 0)
            return EnvProxyAssessment.Clean;

        return new EnvProxyAssessment(
            hasProxy,
            distinct,
            hasTls,
            $"Detected AI proxy/TLS environment overrides: {string.Join(", ", distinct)}");
    }

    public bool PurgeEnvironmentProxySettings(IDictionary<string, string?>? testUserEnv = null)
    {
        var purgedAny = false;

        bool ShouldRemove(string name, string? val)
        {
            if (string.IsNullOrWhiteSpace(val)) return false;
            var trimmed = val.Trim();
            if (ProxyEnvVarNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                return IsRogueProxyAddress(trimmed);
            if (string.Equals(name, "NODE_TLS_REJECT_UNAUTHORIZED", StringComparison.OrdinalIgnoreCase))
                return string.Equals(trimmed, "0", StringComparison.Ordinal);
            if (string.Equals(name, "NODE_EXTRA_CA_CERTS", StringComparison.OrdinalIgnoreCase))
                return true;
            return KnownMitmKeywords.Any(k => trimmed.Contains(k, StringComparison.OrdinalIgnoreCase));
        }

        if (testUserEnv != null)
        {
            foreach (var key in testUserEnv.Keys.ToArray())
            {
                if (ShouldRemove(key, testUserEnv[key]))
                {
                    testUserEnv.Remove(key);
                    purgedAny = true;
                }
            }
            return purgedAny;
        }

        foreach (var name in ProxyEnvVarNames.Concat(TlsOverrideEnvVarNames))
        {
            try
            {
                var procVal = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
                if (ShouldRemove(name, procVal))
                {
                    Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.Process);
                    purgedAny = true;
                }
            }
            catch { }

            try
            {
                var userVal = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
                if (ShouldRemove(name, userVal))
                {
                    Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.User);
                    purgedAny = true;
                }
            }
            catch { }
        }

        return purgedAny;
    }

    public WinHttpProxyAssessment CheckWinHttpProxy(byte[]? testWinHttpSettings = null)
    {
        try
        {
            byte[]? raw = testWinHttpSettings;
            if (raw == null)
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings\Connections",
                    writable: false);
                raw = key?.GetValue("WinHttpSettings") as byte[];
            }

            if (raw == null || raw.Length < 16)
                return WinHttpProxyAssessment.Clean;

            var flags = BitConverter.ToInt32(raw, 8);
            var len = BitConverter.ToInt32(raw, 12);
            if ((flags & 0x02) == 0 || len <= 0 || 16 + len > raw.Length)
                return WinHttpProxyAssessment.Clean;

            var proxyServer = System.Text.Encoding.ASCII.GetString(raw, 16, len).Trim('\0', ' ');
            if (IsRogueProxyAddress(proxyServer))
            {
                return new WinHttpProxyAssessment(true, proxyServer, $"Rogue WinHTTP proxy detected: {proxyServer}");
            }

            return WinHttpProxyAssessment.Clean;
        }
        catch
        {
            return WinHttpProxyAssessment.Clean;
        }
    }

    public AiTrafficAssessment ScanAll(CaptureAssessment? captureAssessment = null, string? hostsPath = null)
    {
        var proxy = CheckProxyStatus();
        var certs = ScanRogueCertificates(forceRefresh: true);
        var hosts = CheckAiHostsProtection(hostsPath);
        var envProxy = CheckEnvironmentProxyStatus();
        var winHttpProxy = CheckWinHttpProxy();

        var assessment = captureAssessment ?? new CaptureRiskService().Scan();
        var sniffers = assessment.Tools.Where(t => t.Kind == CaptureToolKind.Sniffer).ToArray();

        return new AiTrafficAssessment(proxy, certs, sniffers, hosts, envProxy, winHttpProxy);
    }
}
