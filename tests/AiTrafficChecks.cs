using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;
using StreamGuard.Models;
using StreamGuard.Services;

namespace StreamGuard.Tests;

internal static class AiTrafficChecks
{
    public static void Run(Action<bool, string> check, string artifactDirectory)
    {
        TestRogueProxyDetection(check);
        TestProxyPurge(check);
        TestCertificateDetection(check);
        TestHostsTampering(check, artifactDirectory);
        TestConcurrentHostsAccess(check, artifactDirectory);
        TestSnifferCatalog(check);
        TestConfigPersistence(check, artifactDirectory);
        TestEnvironmentProxyAndTlsShield(check);
        TestWinHttpProxyDetection(check);
        TestAntigravitySanitizedLaunch(check);
        TestExpandedEndpointsAndMitmKeywords(check);
    }

    private static void TestRogueProxyDetection(Action<bool, string> check)
    {
        // 1. Loopback addresses
        check(AiTrafficProtectionService.IsRogueProxyAddress("127.0.0.1:8888"), "127.0.0.1:8888 is identified as rogue proxy");
        check(AiTrafficProtectionService.IsRogueProxyAddress("http=127.0.0.1:8080;https=127.0.0.1:8080"), "Multi-protocol 127.0.0.1 is rogue");
        check(AiTrafficProtectionService.IsRogueProxyAddress("localhost:8888"), "localhost is rogue");
        check(AiTrafficProtectionService.IsRogueProxyAddress("[::1]:8080"), "IPv6 loopback [::1] is rogue");
        check(AiTrafficProtectionService.IsRogueProxyAddress("::1:8080"), "IPv6 loopback ::1 is rogue");
        check(AiTrafficProtectionService.IsRogueProxyAddress("0.0.0.0:8888"), "0.0.0.0 is rogue");
        check(AiTrafficProtectionService.IsRogueProxyAddress("127.0.0.2:8000"), "IPv4 loopback 127.0.0.2 is rogue");
        check(AiTrafficProtectionService.IsRogueProxyAddress("127.1.0.1:9999"), "IPv4 loopback 127.1.0.1 is rogue");
        check(AiTrafficProtectionService.IsRogueProxyAddress("http=127.2.3.4:1234"), "Multi-protocol IPv4 loopback 127.2.3.4 is rogue");
        check(AiTrafficProtectionService.IsRogueProxyAddress("::ffff:127.0.0.1:8080"), "IPv4-mapped IPv6 loopback is rogue");

        // 2. Common proxy ports
        check(AiTrafficProtectionService.IsRogueProxyAddress("proxy.local:8888"), "Port 8888 (Fiddler) is rogue");
        check(AiTrafficProtectionService.IsRogueProxyAddress("intercept.lan:8080"), "Port 8080 (Burp/mitm) is rogue");
        check(AiTrafficProtectionService.IsRogueProxyAddress("proxy.test:8081"), "Port 8081 is rogue");
        check(AiTrafficProtectionService.IsRogueProxyAddress("proxy.test:8889"), "Port 8889 is rogue");
        check(AiTrafficProtectionService.IsRogueProxyAddress("proxy.test:9090"), "Port 9090 is rogue");
        check(AiTrafficProtectionService.IsRogueProxyAddress("192.168.1.50:1080"), "Port 1080 (SOCKS) is rogue");

        // 3. Clean addresses
        check(!AiTrafficProtectionService.IsRogueProxyAddress("corporate-proxy.corp.net:3128"), "Corporate proxy on port 3128 is not flagged as rogue");
        check(!AiTrafficProtectionService.IsRogueProxyAddress(""), "Empty address is not rogue");
        check(!AiTrafficProtectionService.IsRogueProxyAddress(null), "Null address is not rogue");
    }

    private static void TestProxyPurge(Action<bool, string> check)
    {
        var service = new AiTrafficProtectionService();
        const string testKeyPath = @"Software\StreamGuardTestPolicies\InternetSettingsTest";

        try
        {
            // 1. Clean registry state
            using (var key = Registry.CurrentUser.CreateSubKey(testKeyPath, writable: true))
            {
                key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                key.SetValue("ProxyServer", string.Empty, RegistryValueKind.String);
                if (key.GetValue("AutoConfigURL") != null) key.DeleteValue("AutoConfigURL");
            }
            using (var key = Registry.CurrentUser.OpenSubKey(testKeyPath, writable: false)!)
            {
                var assessment = service.CheckProxyStatus(key);
                check(!assessment.IsProxyEnabled, "Clean key: Proxy is not enabled");
                check(!assessment.IsRogueProxy, "Clean key: Not a rogue proxy");
            }

            // 2. Inject Rogue Proxy: ProxyEnable=1, ProxyServer="127.0.0.1:8888"
            using (var key = Registry.CurrentUser.OpenSubKey(testKeyPath, writable: true)!)
            {
                key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                key.SetValue("ProxyServer", "127.0.0.1:8888", RegistryValueKind.String);
            }
            using (var key = Registry.CurrentUser.OpenSubKey(testKeyPath, writable: false)!)
            {
                var assessment = service.CheckProxyStatus(key);
                check(assessment.IsProxyEnabled, "Injected key: Proxy is enabled");
                check(assessment.IsRogueProxy, "Injected key: Rogue proxy detected");
                check(assessment.Reason != null && assessment.Reason.Contains("127.0.0.1"), "Rogue reason identifies 127.0.0.1");
            }

            // 3. Purge Proxy Settings
            using (var key = Registry.CurrentUser.OpenSubKey(testKeyPath, writable: true)!)
            {
                var purged = service.PurgeProxySettings(key, notifyWinInet: false);
                check(purged, "PurgeProxySettings returned true");
            }

            // 4. Verify post-purge clean state
            using (var key = Registry.CurrentUser.OpenSubKey(testKeyPath, writable: false)!)
            {
                var assessment = service.CheckProxyStatus(key);
                check(!assessment.IsProxyEnabled, "Post-purge: Proxy is disabled");
                check(!assessment.IsRogueProxy, "Post-purge: No longer rogue");
                check(string.IsNullOrEmpty(assessment.ProxyServer), "Post-purge: ProxyServer is empty");
            }

            // 5. Inject Rogue PAC script URL
            using (var key = Registry.CurrentUser.OpenSubKey(testKeyPath, writable: true)!)
            {
                key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                key.SetValue("AutoConfigURL", "http://127.0.0.1:8888/proxy.pac", RegistryValueKind.String);
            }
            using (var key = Registry.CurrentUser.OpenSubKey(testKeyPath, writable: false)!)
            {
                var assessment = service.CheckProxyStatus(key);
                check(assessment.IsRogueProxy, "Rogue PAC auto-config URL detected even if ProxyEnable=0");
            }

            // Purge again to clean up PAC URL
            using (var key = Registry.CurrentUser.OpenSubKey(testKeyPath, writable: true)!)
            {
                service.PurgeProxySettings(key, notifyWinInet: false);
            }
            using (var key = Registry.CurrentUser.OpenSubKey(testKeyPath, writable: false)!)
            {
                var assessment = service.CheckProxyStatus(key);
                check(!assessment.IsRogueProxy, "Post-purge PAC URL is removed");
            }
        }
        finally
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(testKeyPath, throwOnMissingSubKey: false); } catch { }
        }
    }

    private static void TestCertificateDetection(Action<bool, string> check)
    {
        // Known MITM keywords
        check(AiTrafficProtectionService.IsRogueCertificate("CN=DO_NOT_TRUST_FiddlerRoot, O=DO_NOT_TRUST", "DO_NOT_TRUST"), "FiddlerRoot certificate recognized");
        check(AiTrafficProtectionService.IsRogueCertificate("CN=Fiddler.Cert", "Fiddler"), "Fiddler certificate recognized");
        check(AiTrafficProtectionService.IsRogueCertificate("CN=Charles Proxy Custom Root Certificate", "Charles Proxy CA"), "Charles Proxy recognized");
        check(AiTrafficProtectionService.IsRogueCertificate("CN=mitmproxy", "mitmproxy"), "mitmproxy certificate recognized");
        check(AiTrafficProtectionService.IsRogueCertificate("CN=PortSwigger CA", "PortSwigger"), "PortSwigger certificate recognized");
        check(AiTrafficProtectionService.IsRogueCertificate("CN=HTTPDebuggerRoot", "HTTP Debugger Pro"), "HTTPDebugger recognized");
        check(AiTrafficProtectionService.IsRogueCertificate("CN=Proxifier CA", "Proxifier"), "Proxifier certificate recognized");
        check(AiTrafficProtectionService.IsRogueCertificate("CN=Burp Suite CA", "PortSwigger"), "Burp certificate recognized");

        // Innocent certificates
        check(!AiTrafficProtectionService.IsRogueCertificate("CN=DigiCert Global Root CA, OU=www.digicert.com", "DigiCert Inc"), "DigiCert is not flagged");
        check(!AiTrafficProtectionService.IsRogueCertificate("CN=Google Trust Services Global Root CA", "Google Trust Services"), "Google Trust Services is not flagged");
        check(!AiTrafficProtectionService.IsRogueCertificate("CN=Microsoft Root Certificate Authority 2011", "Microsoft Corporation"), "Microsoft Root CA is not flagged");

        // Real scan executes safely with cache and forceRefresh
        var service = new AiTrafficProtectionService();
        var certs = service.ScanRogueCertificates();
        check(certs != null, "ScanRogueCertificates executed and returned non-null list");
        var certsCached = service.ScanRogueCertificates(forceRefresh: false);
        check(ReferenceEquals(certs, certsCached), "ScanRogueCertificates returns cached instance on subsequent call");
        var certsRefreshed = service.ScanRogueCertificates(forceRefresh: true);
        check(certsRefreshed != null, "ScanRogueCertificates with forceRefresh returns non-null list");
    }

    private static void TestHostsTampering(Action<bool, string> check, string artifactDirectory)
    {
        var service = new AiTrafficProtectionService();
        var tempHosts = Path.Combine(artifactDirectory, "test-hosts-" + Guid.NewGuid().ToString("N"));

        try
        {
            // 1. Clean hosts file
            var cleanHosts = "# Copyright (c) Microsoft Corp.\n127.0.0.1 localhost\n::1 localhost\n";
            File.WriteAllText(tempHosts, cleanHosts);
            var assessment = service.CheckAiHostsProtection(tempHosts);
            check(!assessment.IsTampered, "Clean hosts file is not tampered");
            check(assessment.HijackedEntries.Count == 0, "No hijacked entries in clean hosts file");

            // 2. Tampered hosts file mapping AI endpoints (including multi-host line)
            var tamperedHosts = cleanHosts +
                "127.0.0.1 myapp.local generativelanguage.googleapis.com\n" +
                "0.0.0.0 gemini.google.com # block gemini\n" +
                "192.168.1.1 aiplatform.googleapis.com\n";
            File.WriteAllText(tempHosts, tamperedHosts);
            var tamperedAssessment = service.CheckAiHostsProtection(tempHosts);
            check(tamperedAssessment.IsTampered, "Tampered hosts file is flagged");
            check(tamperedAssessment.HijackedEntries.Count == 3, "All 3 hijacked AI endpoints detected");

            // 3. Purge AI Hosts Hijacks
            var purged = service.PurgeAiHostsHijacks(tempHosts);
            check(purged, "PurgeAiHostsHijacks returned true");

            // 4. Verify post-purge
            var postAssessment = service.CheckAiHostsProtection(tempHosts);
            check(!postAssessment.IsTampered, "Post-purge hosts file is clean");
            check(postAssessment.HijackedEntries.Count == 0, "Post-purge hijacked entries count is 0");
            var remainingContent = File.ReadAllText(tempHosts);
            check(remainingContent.Contains("127.0.0.1 localhost"), "Standard localhost entry preserved");
            check(remainingContent.Contains("myapp.local"), "Co-located non-AI host 'myapp.local' preserved on cleansed line");
            check(!remainingContent.Contains("generativelanguage.googleapis.com"), "generativelanguage endpoint removed");
            check(!remainingContent.Contains("gemini.google.com"), "gemini endpoint removed");
            check(!remainingContent.Contains("aiplatform.googleapis.com"), "aiplatform endpoint removed");
        }
        finally
        {
            try { if (File.Exists(tempHosts)) File.Delete(tempHosts); } catch { }
        }
    }

    private static void TestSnifferCatalog(Action<bool, string> check)
    {
        var catalog = CaptureRiskService.Catalog;
        var snifferTools = catalog.Where(t => t.Kind == CaptureToolKind.Sniffer).ToList();

        check(snifferTools.Count >= 7, $"Catalog has at least 7 sniffer tools (found {snifferTools.Count})");

        var requiredIds = new[] { "wireshark", "fiddler", "charles", "mitmproxy", "httpdebugger", "proxifier", "burpsuite" };
        foreach (var id in requiredIds)
        {
            var found = snifferTools.Any(t => t.Id == id);
            check(found, $"Required sniffer tool '{id}' is present in Catalog");
        }

        // Test process assessment with sniffer names
        var assessment = CaptureRiskService.Assess(new[] { "wireshark", "fiddler", "charles", "mitmdump", "HTTPDebuggerPro", "Proxifier", "burpsuite" });
        check(assessment.Tools.Count == 7, "Assess identifies all 7 sniffer processes");
        check(assessment.Tools.All(t => t.Kind == CaptureToolKind.Sniffer), "All recognized sniffer processes have Sniffer kind");
    }

    private static void TestConcurrentHostsAccess(Action<bool, string> check, string artifactDirectory)
    {
        var service = new AiTrafficProtectionService();
        var testHostsPath = Path.Combine(artifactDirectory, "hosts_concurrent_test");
        File.WriteAllLines(testHostsPath, new[]
        {
            "127.0.0.1 localhost",
            "127.0.0.1 generativelanguage.googleapis.com # hijacked",
            "::1 localhost"
        });

        var tasks = new Task[10];
        for (var i = 0; i < 5; i++)
        {
            tasks[i] = Task.Run(() => service.CheckAiHostsProtection(testHostsPath));
            tasks[i + 5] = Task.Run(() => service.PurgeAiHostsHijacks(testHostsPath));
        }
        Task.WaitAll(tasks);
        var finalCheck = service.CheckAiHostsProtection(testHostsPath);
        check(!finalCheck.IsTampered, "Concurrent hosts checks and purges are thread-safe and remove hijacks");
        check(AiTrafficProtectionService.HostsLock != null, "AiTrafficProtectionService.HostsLock is available for cross-service synchronization");
    }

    private static void TestConfigPersistence(Action<bool, string> check, string artifactDirectory)
    {
        var configPath = Path.Combine(artifactDirectory, "ai-traffic-config-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var service = new ConfigService(configPath);
            service.Load();
            check(service.EnableAiTrafficShield, "EnableAiTrafficShield is true by default");

            var app = new GuardedApp { Name = "AI Test App", ProcessName = "aitest", SecureHotkey = "Ctrl+Alt+9" };

            // Save as false
            service.Save(new[] { app }, enableAiTrafficShield: false);
            var reloaded = new ConfigService(configPath);
            reloaded.Load();
            check(!reloaded.EnableAiTrafficShield, "Disabling EnableAiTrafficShield persists across save/load");

            // Save as true
            service.Save(new[] { app }, enableAiTrafficShield: true);
            var reloadedTrue = new ConfigService(configPath);
            reloadedTrue.Load();
            check(reloadedTrue.EnableAiTrafficShield, "Enabling EnableAiTrafficShield persists across save/load");
        }
        finally
        {
            try { if (File.Exists(configPath)) File.Delete(configPath); } catch { }
        }
    }

    private static void TestEnvironmentProxyAndTlsShield(Action<bool, string> check)
    {
        var svc = new AiTrafficProtectionService();
        var env = new System.Collections.Generic.Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["HTTP_PROXY"] = "http://127.0.0.1:8888",
            ["HTTPS_PROXY"] = "http://localhost:8080",
            ["NODE_EXTRA_CA_CERTS"] = @"C:\temp\mitmproxy-ca.pem",
            ["NODE_TLS_REJECT_UNAUTHORIZED"] = "0",
            ["PATH"] = @"C:\Windows\System32"
        };

        var assessment = svc.CheckEnvironmentProxyStatus(env);
        check(assessment.HasRogueEnvVars, "CheckEnvironmentProxyStatus detects rogue HTTP_PROXY/HTTPS_PROXY env vars");
        check(assessment.HasTlsBypassOrCustomCa, "CheckEnvironmentProxyStatus detects NODE_EXTRA_CA_CERTS and NODE_TLS_REJECT_UNAUTHORIZED=0");
        check(assessment.DetectedVariables.Count == 4, $"Expected 4 detected env overrides, got {assessment.DetectedVariables.Count}");

        var purged = svc.PurgeEnvironmentProxySettings(env);
        check(purged, "PurgeEnvironmentProxySettings removes rogue env overrides");
        check(!env.ContainsKey("HTTP_PROXY") && !env.ContainsKey("HTTPS_PROXY"), "Proxy env vars removed after purge");
        check(!env.ContainsKey("NODE_EXTRA_CA_CERTS") && !env.ContainsKey("NODE_TLS_REJECT_UNAUTHORIZED"), "TLS/CA override env vars removed after purge");
        check(env.ContainsKey("PATH"), "Unrelated PATH env var preserved");

        var after = svc.CheckEnvironmentProxyStatus(env);
        check(!after.HasRogueEnvVars && !after.HasTlsBypassOrCustomCa, "Environment is clean after purge");
    }

    private static void TestWinHttpProxyDetection(Action<bool, string> check)
    {
        var svc = new AiTrafficProtectionService();

        // Direct connection WinHTTP binary blob (flags = 0x01)
        var directBlob = new byte[16];
        BitConverter.GetBytes(0x01).CopyTo(directBlob, 8);
        var clean = svc.CheckWinHttpProxy(directBlob);
        check(!clean.IsRogueWinHttpProxy, "Direct WinHTTP settings reported clean");

        // Named loopback proxy WinHTTP binary blob (flags = 0x03, server = "127.0.0.1:8888")
        var serverBytes = System.Text.Encoding.ASCII.GetBytes("127.0.0.1:8888");
        var proxyBlob = new byte[16 + serverBytes.Length];
        BitConverter.GetBytes(0x03).CopyTo(proxyBlob, 8);
        BitConverter.GetBytes(serverBytes.Length).CopyTo(proxyBlob, 12);
        serverBytes.CopyTo(proxyBlob, 16);

        var rogue = svc.CheckWinHttpProxy(proxyBlob);
        check(rogue.IsRogueWinHttpProxy && rogue.ProxyServer == "127.0.0.1:8888",
            $"WinHTTP loopback proxy detected accurately (server={rogue.ProxyServer})");
    }

    private static void TestAntigravitySanitizedLaunch(Action<bool, string> check)
    {
        var prevHttp = Environment.GetEnvironmentVariable("HTTP_PROXY");
        var prevCa = Environment.GetEnvironmentVariable("NODE_EXTRA_CA_CERTS");
        try
        {
            Environment.SetEnvironmentVariable("HTTP_PROXY", "http://127.0.0.1:8888");
            Environment.SetEnvironmentVariable("NODE_EXTRA_CA_CERTS", @"C:\mitm.pem");

            var psi = AntigravityInstallerService.CreateSanitizedLaunchStartInfo(@"C:\Dummy\Antigravity.exe", @"C:\Dummy");
            check(!psi.UseShellExecute, "Sanitized launch uses UseShellExecute=false to override child environment");
            check(!psi.EnvironmentVariables.ContainsKey("HTTP_PROXY"), "HTTP_PROXY stripped from Antigravity child environment");
            check(!psi.EnvironmentVariables.ContainsKey("NODE_EXTRA_CA_CERTS"), "NODE_EXTRA_CA_CERTS stripped from Antigravity child environment");
            check(psi.EnvironmentVariables["NO_PROXY"] == "*", "NO_PROXY=* set on Antigravity child environment");
        }
        finally
        {
            Environment.SetEnvironmentVariable("HTTP_PROXY", prevHttp);
            Environment.SetEnvironmentVariable("NODE_EXTRA_CA_CERTS", prevCa);
        }
    }

    private static void TestExpandedEndpointsAndMitmKeywords(Action<bool, string> check)
    {
        check(AiTrafficProtectionService.ProtectedAiEndpoints.Contains("cloudcode-pa.googleapis.com"),
            "ProtectedAiEndpoints includes Antigravity cloudcode-pa.googleapis.com");
        check(AiTrafficProtectionService.ProtectedAiEndpoints.Contains("oauth2.googleapis.com"),
            "ProtectedAiEndpoints includes oauth2.googleapis.com");
        check(AiTrafficProtectionService.KnownMitmKeywords.Contains("reqable") &&
              AiTrafficProtectionService.KnownMitmKeywords.Contains("proxyman") &&
              AiTrafficProtectionService.KnownMitmKeywords.Contains("whistle"),
            "KnownMitmKeywords includes reqable, proxyman, and whistle");

        var snifferCatalog = CaptureRiskService.Assess(new[] { "reqable.exe", "proxyman.exe", "whistle.exe" });
        check(snifferCatalog.Tools.Count == 3 && snifferCatalog.Tools.All(t => t.Kind == CaptureToolKind.Sniffer),
            $"CaptureRiskService identifies Reqable, Proxyman, and Whistle as Sniffers (count={snifferCatalog.Tools.Count})");
    }
}
