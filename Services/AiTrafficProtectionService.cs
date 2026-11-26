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

public sealed record AiTrafficAssessment(
    ProxyAssessment Proxy,
    IReadOnlyList<RogueCertificateInfo> RogueCertificates,
    IReadOnlyList<CaptureTool> ActiveSniffers,
    AiHostsAssessment Hosts
)
{
    public bool IsAllSafe =>
        !Proxy.IsRogueProxy &&
        RogueCertificates.Count == 0 &&
        ActiveSniffers.Count == 0 &&
        !Hosts.IsTampered;
}

public sealed class AiTrafficProtectionService
{
    public const string InternetSettingsSubKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
    private const int INTERNET_OPTION_REFRESH = 37;

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

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
        "burp"
    };

    public static readonly string[] ProtectedAiEndpoints =
    {
        "generativelanguage.googleapis.com",
        "gemini.google.com",
        "aiplatform.googleapis.com"
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

    public AiTrafficAssessment ScanAll(CaptureAssessment? captureAssessment = null, string? hostsPath = null)
    {
        var proxy = CheckProxyStatus();
        var certs = ScanRogueCertificates(forceRefresh: true);
        var hosts = CheckAiHostsProtection(hostsPath);

        var assessment = captureAssessment ?? new CaptureRiskService().Scan();
        var sniffers = assessment.Tools.Where(t => t.Kind == CaptureToolKind.Sniffer).ToArray();

        return new AiTrafficAssessment(proxy, certs, sniffers, hosts);
    }
}
