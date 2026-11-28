using System;
using System.Threading;
using System.Threading.Tasks;
using StreamGuard.Localization;
using StreamGuard.Services;

namespace StreamGuard.Tests
{
    internal static class AntigravityAuthBridgeChecks
    {
        public static void Run(Action<bool, string> check, string artifactDirectory)
        {
            TestGoogleAuthUrlValidation(check);
            TestLoopbackCallbackValidation(check);
            TestAuthUrlExtraction(check);
            TestListeningLifecycle(check);
            TestMockClipboardCapture(check);
            TestMockProcessTermination(check);
            TestLocalizationStrings(check);
        }

        private static void TestGoogleAuthUrlValidation(Action<bool, string> check)
        {
            // 1. Valid Google OAuth loopback URLs
            var validUrl1 = "https://accounts.google.com/o/oauth2/v2/auth?client_id=test-client.apps.googleusercontent.com&redirect_uri=http%3A%2F%2F127.0.0.1%3A54321%2Fcallback&response_type=code&scope=openid";
            check(AntigravityAuthBridgeService.IsValidGoogleAuthUrl(validUrl1),
                "Valid Google OAuth loopback URL is accepted");

            var validUrl2 = "https://accounts.google.com/signin/oauth?client_id=12345&redirect_uri=http://localhost:8080";
            check(AntigravityAuthBridgeService.IsValidGoogleAuthUrl(validUrl2),
                "Valid Google signin/oauth URL is accepted");

            var validUrl3 = "https://accounts.google.com/auth/oauth2?response_type=token";
            check(AntigravityAuthBridgeService.IsValidGoogleAuthUrl(validUrl3),
                "Valid Google auth/oauth2 URL is accepted");

            // 2. Invalid URLs
            check(!AntigravityAuthBridgeService.IsValidGoogleAuthUrl(null),
                "Null URL is rejected");
            check(!AntigravityAuthBridgeService.IsValidGoogleAuthUrl(""),
                "Empty URL is rejected");
            check(!AntigravityAuthBridgeService.IsValidGoogleAuthUrl("   "),
                "Whitespace URL is rejected");
            check(!AntigravityAuthBridgeService.IsValidGoogleAuthUrl("not a url"),
                "Malformed URL string is rejected");

            // Insecure HTTP scheme
            check(!AntigravityAuthBridgeService.IsValidGoogleAuthUrl("http://accounts.google.com/o/oauth2/v2/auth?client_id=123&redirect_uri=http://127.0.0.1"),
                "Non-HTTPS scheme is rejected");

            // Spoofed domain
            check(!AntigravityAuthBridgeService.IsValidGoogleAuthUrl("https://fake-accounts.google.com/o/oauth2/auth?client_id=123"),
                "Subdomain spoofing is rejected");
            check(!AntigravityAuthBridgeService.IsValidGoogleAuthUrl("https://accounts.google.com.attacker.com/o/oauth2/auth?client_id=123"),
                "Domain spoofing suffix is rejected");
            check(!AntigravityAuthBridgeService.IsValidGoogleAuthUrl("https://google.com/o/oauth2/auth?client_id=123"),
                "Non-accounts.google.com host is rejected");

            // Missing OAuth path or query
            check(!AntigravityAuthBridgeService.IsValidGoogleAuthUrl("https://accounts.google.com/"),
                "Generic accounts.google.com root is rejected");
            check(!AntigravityAuthBridgeService.IsValidGoogleAuthUrl("https://accounts.google.com/ServiceLogin"),
                "Standard ServiceLogin without OAuth parameters is rejected");
        }

        private static void TestLoopbackCallbackValidation(Action<bool, string> check)
        {
            check(AntigravityAuthBridgeService.IsLoopbackAuthCallback("http://127.0.0.1:54321/oauth2callback?code=4/0A..."),
                "Loopback callback with 127.0.0.1, oauth2callback and code is recognized");

            check(AntigravityAuthBridgeService.IsLoopbackAuthCallback("http://localhost:8080/callback?state=xyz&code=abc"),
                "Loopback callback with localhost, callback and code is recognized");

            check(AntigravityAuthBridgeService.IsLoopbackAuthCallback("http://127.0.0.1:9090/?code=secret123"),
                "Loopback callback with query code= is recognized");

            check(AntigravityAuthBridgeService.IsLoopbackAuthCallback("http://127.0.0.1:9090/?state=active"),
                "Loopback callback with query state= is recognized");

            // Non-loopback URLs
            check(!AntigravityAuthBridgeService.IsLoopbackAuthCallback("https://accounts.google.com/o/oauth2/auth"),
                "External Google accounts URL is not a loopback callback");

            check(!AntigravityAuthBridgeService.IsLoopbackAuthCallback("https://google.com/callback?code=123"),
                "External host is not a loopback callback");

            check(!AntigravityAuthBridgeService.IsLoopbackAuthCallback(null),
                "Null URL is not a loopback callback");
        }

        private static void TestAuthUrlExtraction(Action<bool, string> check)
        {
            var rawUrl = "https://accounts.google.com/o/oauth2/v2/auth?client_id=123.apps.googleusercontent.com&redirect_uri=http%3A%2F%2F127.0.0.1%3A8080&response_type=code";

            // Direct URL
            var extracted1 = AntigravityAuthBridgeService.ExtractAuthUrl(rawUrl);
            check(extracted1 == rawUrl, "ExtractAuthUrl extracts direct URL matching valid OAuth pattern");

            // Embedded in browser command line
            var commandLine = $"\"C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe\" --profile-directory=Default \"{rawUrl}\"";
            var extracted2 = AntigravityAuthBridgeService.ExtractAuthUrl(commandLine);
            check(extracted2 == rawUrl, "ExtractAuthUrl extracts valid URL from browser process command line");

            // Embedded in text with surrounding punctuation
            var textWithPunctuation = $"Login link is: ({rawUrl}).";
            var extracted3 = AntigravityAuthBridgeService.ExtractAuthUrl(textWithPunctuation);
            check(extracted3 == rawUrl, "ExtractAuthUrl trims enclosing brackets and trailing punctuation");

            // Text with no valid URL
            check(AntigravityAuthBridgeService.ExtractAuthUrl("Please visit https://google.com for more info") == null,
                "ExtractAuthUrl returns null for non-OAuth URLs");
            check(AntigravityAuthBridgeService.ExtractAuthUrl("") == null,
                "ExtractAuthUrl returns null for empty string");
            check(AntigravityAuthBridgeService.ExtractAuthUrl(null) == null,
                "ExtractAuthUrl returns null for null string");
        }

        private static void TestListeningLifecycle(Action<bool, string> check)
        {
            using var service = new AntigravityAuthBridgeService(
                customClipboardReader: () => null,
                customProcessTerminator: _ => { });

            check(!service.IsListening, "Initial state of AntigravityAuthBridgeService is not listening");

            var stateChanges = 0;
            service.ListeningStateChanged += (_, isListening) =>
            {
                if (isListening) stateChanges++;
            };

            var started = service.StartListening(timeout: TimeSpan.FromSeconds(30));
            check(started, "StartListening returns true");
            check(service.IsListening, "IsListening is true while active");
            check(stateChanges == 1, "ListeningStateChanged fired when started");

            service.StopListening();
            check(!service.IsListening, "IsListening is false after StopListening");

            // Test timeout
            var timeoutTriggered = false;
            using var timeoutService = new AntigravityAuthBridgeService(
                customClipboardReader: () => null);
            timeoutService.TimeoutOccurred += (_, _) => timeoutTriggered = true;

            timeoutService.StartListening(timeout: TimeSpan.FromMilliseconds(50));
            Thread.Sleep(120);

            check(!timeoutService.IsListening, "Service stops listening after timeout");
            check(timeoutTriggered, "TimeoutOccurred event fired on timeout");
        }

        private static void TestMockClipboardCapture(Action<bool, string> check)
        {
            var targetUrl = "https://accounts.google.com/o/oauth2/v2/auth?client_id=mock-client&redirect_uri=http%3A%2F%2F127.0.0.1%3A9999&response_type=code";
            string? capturedFromCallback = null;
            string? capturedFromEvent = null;

            using var service = new AntigravityAuthBridgeService(
                customClipboardReader: () => targetUrl);

            service.AuthUrlCaptured += (_, url) => capturedFromEvent = url;

            var started = service.StartListening(url => capturedFromCallback = url, TimeSpan.FromSeconds(5));

            check(started, "StartListening succeeds with mock clipboard");
            check(capturedFromCallback == targetUrl, "Auth URL is immediately captured from clipboard reader");
            check(capturedFromEvent == targetUrl, "AuthUrlCaptured event fired with target URL");
            check(!service.IsListening, "Service stops listening automatically upon URL capture");
        }

        private static void TestMockProcessTermination(Action<bool, string> check)
        {
            var terminatedPid = 0;

            using var service = new AntigravityAuthBridgeService(
                customClipboardReader: () => null,
                customProcessTerminator: pid => terminatedPid = pid);

            check(terminatedPid == 0, "No process terminated initially");

            // Verify IsTargetBrowserProcess recognition
            var isChrome = (bool)typeof(AntigravityAuthBridgeService)
                .GetMethod("IsTargetBrowserProcess", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .Invoke(null, new object[] { "chrome.exe" })!;
            check(isChrome, "chrome.exe is recognized as target browser process");

            var isEdge = (bool)typeof(AntigravityAuthBridgeService)
                .GetMethod("IsTargetBrowserProcess", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .Invoke(null, new object[] { "msedge.exe" })!;
            check(isEdge, "msedge.exe is recognized as target browser process");

            var isNotepad = (bool)typeof(AntigravityAuthBridgeService)
                .GetMethod("IsTargetBrowserProcess", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .Invoke(null, new object[] { "notepad.exe" })!;
            check(!isNotepad, "notepad.exe is not treated as target browser process");

            // Verify clipboard clearer is invoked when OAuth URL is captured from clipboard
            var clipboardCleared = false;
            string? capturedUrl = null;
            const string oauthUrl = "https://accounts.google.com/o/oauth2/v2/auth?client_id=123&redirect_uri=http://127.0.0.1:4200/callback&response_type=code";
            using var clearBridge = new AntigravityAuthBridgeService(
                customClipboardReader: () => oauthUrl,
                customClipboardClearer: () => clipboardCleared = true);
            clearBridge.StartListening(url => capturedUrl = url);
            check(capturedUrl == oauthUrl, "Clipboard OAuth URL captured immediately");
            check(clipboardCleared, "Clipboard cleared immediately after capturing OAuth URL to prevent snooping");
        }

        private static void TestLocalizationStrings(Action<bool, string> check)
        {
            var keys = new[]
            {
                "AgyInstall.BtnSecureLogin",
                "AgyInstall.BtnSecureLoginTooltip",
                "Browser.AntigravityLogin",
                "Browser.AntigravityLoginTooltip",
                "AgyBridge.Listening",
                "AgyBridge.Captured",
                "AgyBridge.Success",
                "AgyBridge.Timeout",
                "AgyBridge.Cancelled",
                "AgyBridge.NotInstalled"
            };

            // Test in both languages
            var originalLang = LocalizationService.Current.Language;
            try
            {
                LocalizationService.Current.SetLanguage("vi-VN");
                foreach (var key in keys)
                {
                    var vi = LocalizationService.GetString(key);
                    check(!string.IsNullOrWhiteSpace(vi) && !vi.StartsWith("!"),
                        $"Vietnamese string for '{key}' is defined: '{vi}'");
                }

                LocalizationService.Current.SetLanguage("en-US");
                foreach (var key in keys)
                {
                    var en = LocalizationService.GetString(key);
                    check(!string.IsNullOrWhiteSpace(en) && !en.StartsWith("!"),
                        $"English string for '{key}' is defined: '{en}'");
                }
            }
            finally
            {
                LocalizationService.Current.SetLanguage(originalLang);
            }
        }

        public static void RunUiChecks(MainWindow window, Action<bool, string> check)
        {
            var btnSecureLogin = window.FindName("BtnAntigravitySecureLogin") as System.Windows.Controls.Button;
            var btnBrowserLogin = window.FindName("BtnBrowserAntigravityLogin") as System.Windows.Controls.Button;

            check(btnSecureLogin != null, "BtnAntigravitySecureLogin exists in MainWindow Antigravity card");
            check(btnBrowserLogin != null, "BtnBrowserAntigravityLogin exists in MainWindow Browser toolbar");

            var detector = new AntigravityInstallerService();
            var info = detector.GetInstallationInfo();
            if (info.IsInstalled)
            {
                check(btnSecureLogin != null && btnSecureLogin.IsEnabled,
                    "BtnAntigravitySecureLogin is enabled when Antigravity is installed and executable exists");
            }
        }
    }
}
