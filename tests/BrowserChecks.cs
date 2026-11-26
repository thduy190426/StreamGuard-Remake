using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using StreamGuard;
using StreamGuard.Localization;
using StreamGuard.Services;

namespace StreamGuard.Tests;

internal static class BrowserChecks
{
    internal static void Run(Action<bool, string> check, string tempDir)
    {
        TestUrlResolution(check);
        TestUserDataFolder(check);
        TestEnvironmentChecks(check);
        TestBrowserScripts(check, tempDir);
        TestLocalizationStrings(check);
    }

    private static void TestEnvironmentChecks(Action<bool, string> check)
    {
        check(BrowserService.IsWebView2Available(), "WebView2 Runtime is available in the current environment");
        var chromePath = BrowserService.FindChromeExecutable();
        check(chromePath != null && File.Exists(chromePath), "FindChromeExecutable locates an existing chrome binary");
    }

    private static void TestUrlResolution(Action<bool, string> check)
    {
        // 1. Empty / whitespace -> Google home page
        check(BrowserService.ResolveNavigationUrl(null) == "https://www.google.com", "Null URL resolves to Google home");
        check(BrowserService.ResolveNavigationUrl("") == "https://www.google.com", "Empty URL resolves to Google home");
        check(BrowserService.ResolveNavigationUrl("   ") == "https://www.google.com", "Whitespace URL resolves to Google home");

        // 2. Explicit URLs with scheme
        check(BrowserService.ResolveNavigationUrl("https://www.google.com") == "https://www.google.com", "Preserves HTTPS URL");
        check(BrowserService.ResolveNavigationUrl("http://example.com/test?q=1") == "http://example.com/test?q=1", "Preserves HTTP URL");
        check(BrowserService.ResolveNavigationUrl("file:///C:/path/index.html") == "file:///C:/path/index.html", "Preserves file URI");
        check(BrowserService.ResolveNavigationUrl("about:blank") == "about:blank", "Preserves about: URI");
        check(BrowserService.ResolveNavigationUrl("edge://settings") == "edge://settings", "Preserves edge: URI");
        check(BrowserService.ResolveNavigationUrl("chrome://version") == "chrome://version", "Preserves chrome: URI");

        // 3. Domains without scheme -> prepend https://
        check(BrowserService.ResolveNavigationUrl("google.com") == "https://google.com", "Domain prepends https://");
        check(BrowserService.ResolveNavigationUrl("google.com?q=hello") == "https://google.com?q=hello", "Domain with query prepends https://");
        check(BrowserService.ResolveNavigationUrl("vietnamnet.vn#tin-moi") == "https://vietnamnet.vn#tin-moi", "Domain with fragment prepends https://");
        check(BrowserService.ResolveNavigationUrl("vnexpress.net") == "https://vnexpress.net", "VN domain prepends https://");
        check(BrowserService.ResolveNavigationUrl("sub.example.co.uk/path") == "https://sub.example.co.uk/path", "Subdomain path prepends https://");
        check(BrowserService.ResolveNavigationUrl("github.com/microsoft/webview2") == "https://github.com/microsoft/webview2", "GitHub repo path prepends https://");

        // 4. Localhost -> prepend http://
        check(BrowserService.ResolveNavigationUrl("localhost") == "http://localhost", "Localhost prepends http://");
        check(BrowserService.ResolveNavigationUrl("localhost:3000") == "http://localhost:3000", "Localhost:port prepends http://");
        check(BrowserService.ResolveNavigationUrl("localhost:8080/api") == "http://localhost:8080/api", "Localhost:port/path prepends http://");
        check(BrowserService.ResolveNavigationUrl("localhost:5000?test=1") == "http://localhost:5000?test=1", "Localhost:port?query prepends http://");

        // 5. IP Addresses -> prepend http://
        check(BrowserService.ResolveNavigationUrl("127.0.0.1") == "http://127.0.0.1", "IPv4 address prepends http://");
        check(BrowserService.ResolveNavigationUrl("192.168.1.1:8080") == "http://192.168.1.1:8080", "IPv4 with port prepends http://");
        check(BrowserService.ResolveNavigationUrl("10.0.0.1/dashboard") == "http://10.0.0.1/dashboard", "IPv4 with path prepends http://");
        check(BrowserService.ResolveNavigationUrl("192.168.1.1?query=1") == "http://192.168.1.1?query=1", "IPv4 with query prepends http://");

        // 6. Windows local file paths
        check(BrowserService.ResolveNavigationUrl(@"C:\path\to\page.html").StartsWith("file:///C:/path/to/page.html", StringComparison.OrdinalIgnoreCase), "Windows local file path converts to file:///");

        // 7. Multi-word search queries -> Google search URL
        check(BrowserService.ResolveNavigationUrl("driver booster streamguard") == "https://www.google.com/search?q=driver%20booster%20streamguard", "Multi-word search query format");
        check(BrowserService.ResolveNavigationUrl("c# webview2 tutorial") == "https://www.google.com/search?q=c%23%20webview2%20tutorial", "Search with special characters");
        check(BrowserService.ResolveNavigationUrl("thời tiết hôm nay") == "https://www.google.com/search?q=th%E1%BB%9Di%20ti%E1%BA%BFt%20h%C3%B4m%20nay", "Vietnamese unicode search query");

        // 8. Single word queries -> Google search URL
        check(BrowserService.ResolveNavigationUrl("weather") == "https://www.google.com/search?q=weather", "Single word query searches Google");
        check(BrowserService.ResolveNavigationUrl("wikipedia") == "https://www.google.com/search?q=wikipedia", "Single word company searches Google");
    }

    private static void TestUserDataFolder(Action<bool, string> check)
    {
        var path = BrowserService.GetUserDataFolderPath();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        check(path.StartsWith(localAppData, StringComparison.OrdinalIgnoreCase), "Browser user data folder is in LocalApplicationData");
        check(path.EndsWith(Path.Combine("StreamGuard", "BrowserProfile"), StringComparison.OrdinalIgnoreCase), "Browser user data folder is StreamGuard\\BrowserProfile");
    }

    private static void TestBrowserScripts(Action<bool, string> check, string tempDir)
    {
        var antiBlur = BrowserScriptService.GetAntiBlurScript();
        check(!string.IsNullOrWhiteSpace(antiBlur), "Anti-blur script is present");
        check(antiBlur.Contains("stopImmediatePropagation"), "Anti-blur stops event propagation");
        check(antiBlur.Contains("visibilityState"), "Anti-blur overrides visibilityState");
        check(antiBlur.Contains("hasFocus"), "Anti-blur overrides document.hasFocus");
        check(antiBlur.Contains("window.onblur = null"), "Anti-blur clears window.onblur");

        var disableAntiBlur = BrowserScriptService.GetDisableAntiBlurScript();
        check(disableAntiBlur.Contains("disable()"), "Anti-blur disable command is present");

        var copyPaste = BrowserScriptService.GetCopyPasteScript();
        check(!string.IsNullOrWhiteSpace(copyPaste), "Copy/paste script is present");
        check(!copyPaste.Contains("__STREAMGUARD_CLIPBOARD_MESSAGES__"), "Copy/paste replaces messages placeholder");

        var disable = BrowserScriptService.GetDisableCopyPasteScript();
        check(disable.Contains("disable()"), "Copy/paste disable command is present");

        File.WriteAllText(Path.Combine(tempDir, "anti-blur-test.js"), antiBlur);
    }

    private static void TestLocalizationStrings(Action<bool, string> check)
    {
        var L = LocalizationService.Current;

        // In Vietnamese
        L.SetLanguage("vi");
        check(L.Translate("Cannot initialize browser: WebView2 Runtime is not installed on this machine.") == "Không thể khởi tạo trình duyệt: Máy tính chưa được cài đặt WebView2 Runtime.", "Runtime missing notice translates to Vietnamese");
        check(L.Translate("Copy/Paste unlock script injected into the page.") == "Đã chèn kịch bản mở khóa Sao chép / Dán vào trang.", "Copy/paste injected notice translates to Vietnamese");
        check(L.Translate("Copy/Paste unlock disabled.") == "Đã tắt mở khóa Sao chép / Dán.", "Copy/paste locked notice translates to Vietnamese");
        check(L.Translate("Anti-Blur / Focus lock active on this page.") == "Đã bật Anti-Blur / Khóa Focus: trang web luôn ở trạng thái tập trung.", "Anti-blur enabled notice translates to Vietnamese");
        check(L.Translate("Anti-Blur / Focus lock disabled. Reload page to restore normal tab events.") == "Đã tắt Anti-Blur / Khóa Focus. Tải lại trang để khôi phục sự kiện bình thường.", "Anti-blur disabled notice translates to Vietnamese");
        check(L.Translate("Google Chrome was not found. Opening in default system browser instead.") == "Không tìm thấy trình duyệt Google Chrome trên máy tính. Đang mở bằng trình duyệt mặc định của hệ thống.", "Chrome not found notice translates to Vietnamese");
        check(L.Translate("Opening https://www.google.com in external browser") == "Đang mở https://www.google.com bằng trình duyệt ngoài", "Open external template translates to Vietnamese");

        // In English
        L.SetLanguage("en");
        check(L.Translate("Cannot initialize browser: WebView2 Runtime is not installed on this machine.") == "Cannot initialize browser: WebView2 Runtime is not installed on this machine.", "Runtime missing notice renders English");
        check(L.Translate("Copy/Paste unlock script injected into the page.") == "Copy/Paste unlock script injected into the page.", "Copy/paste injected notice renders English");
        check(L.Translate("Opening https://www.google.com in external browser") == "Opening https://www.google.com in external browser", "Open external template renders English");
    }

    internal static void RunUiChecks(MainWindow window, Action<bool, string> check)
    {
        var tab = (RadioButton)window.FindName("TabBrowser");
        check(tab != null, "TabBrowser exists on MainWindow");
        var tabText = (TextBlock)window.FindName("TabBrowserText");
        check(tabText != null, "TabBrowserText exists on MainWindow");

        var view = (Grid)window.FindName("ViewBrowser");
        check(view != null, "ViewBrowser exists on MainWindow");

        var browserControl = window.FindName("BrowserControl");
        check(browserControl != null, "BrowserControl WebView2 element exists");

        var urlBox = (TextBox)window.FindName("TxtBrowserUrl");
        check(urlBox != null, "TxtBrowserUrl exists");

        var btnBack = (Button)window.FindName("BtnBrowserBack");
        check(btnBack != null && !btnBack.IsEnabled, "BtnBrowserBack exists and is disabled initially");

        var btnForward = (Button)window.FindName("BtnBrowserForward");
        check(btnForward != null && !btnForward.IsEnabled, "BtnBrowserForward exists and is disabled initially");

        var btnReload = (Button)window.FindName("BtnBrowserReload");
        check(btnReload != null, "BtnBrowserReload exists");

        var btnHome = (Button)window.FindName("BtnBrowserHome");
        check(btnHome != null, "BtnBrowserHome exists");

        var btnGo = (Button)window.FindName("BtnBrowserGo");
        check(btnGo != null, "BtnBrowserGo exists");

        var toggleCopy = (ToggleButton)window.FindName("ToggleBrowserCopyPaste");
        check(toggleCopy != null, "ToggleBrowserCopyPaste exists");

        var toggleAntiBlur = (ToggleButton)window.FindName("ToggleBrowserAntiBlur");
        check(toggleAntiBlur != null, "ToggleBrowserAntiBlur exists");

        var btnDevTools = (Button)window.FindName("BtnBrowserDevTools");
        check(btnDevTools != null, "BtnBrowserDevTools exists");

        var btnExternalChrome = (Button)window.FindName("BtnOpenInExternalChrome");
        check(btnExternalChrome != null, "BtnOpenInExternalChrome exists");

        var fallbackPanel = (Border)window.FindName("BrowserFallbackPanel");
        check(fallbackPanel != null, "BrowserFallbackPanel exists");
        check(fallbackPanel?.Visibility == Visibility.Collapsed, "BrowserFallbackPanel is collapsed initially");

        var btnDownload = (Button)window.FindName("BtnDownloadWebView2");
        check(btnDownload != null, "BtnDownloadWebView2 exists");

        var btnFallbackExternal = (Button)window.FindName("BtnOpenFallbackExternal");
        check(btnFallbackExternal != null, "BtnOpenFallbackExternal exists");

        // Check passive focus mode and Airspace tab switching behavior
        var passiveFocusField = typeof(MainWindow).GetField("_passiveFocusMode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        passiveFocusField?.SetValue(window, true);

        if (tab != null && view != null)
        {
            tab.IsChecked = true;
            check(view.Visibility == Visibility.Visible, "Switching to TabBrowser makes ViewBrowser visible");
            check(view.Opacity == 1.0, "ViewBrowser opacity is 1.0 without airspace opacity animation glitch");

            var tabProtected = (RadioButton)window.FindName("TabProtected");
            if (tabProtected != null)
            {
                tabProtected.IsChecked = true;
                check(view.Visibility == Visibility.Collapsed, "Switching away from TabBrowser collapses ViewBrowser");
                if (browserControl is UIElement bc)
                {
                    check(bc.Visibility == Visibility.Collapsed, "Switching away from TabBrowser collapses BrowserControl to prevent airspace bleed");
                }
            }

            // Verify WndProcHook activation behavior under PassiveFocusMode
            var wndProcMethod = typeof(MainWindow).GetMethod("WndProcHook", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (wndProcMethod != null)
            {
                tab.IsChecked = true;
                object[] argsActivate = new object[] { IntPtr.Zero, 0x0021 /* WM_MOUSEACTIVATE */, IntPtr.Zero, IntPtr.Zero, false };
                var resActivate = (IntPtr)wndProcMethod.Invoke(window, argsActivate)!;
                bool handledActivate = (bool)argsActivate[4];
                check(resActivate == (IntPtr)1 /* MA_ACTIVATE */ && handledActivate, "WndProcHook returns MA_ACTIVATE with handled=true in TabBrowser");

                tab.IsChecked = false;
                object[] argsNoActivate = new object[] { IntPtr.Zero, 0x0021 /* WM_MOUSEACTIVATE */, IntPtr.Zero, IntPtr.Zero, false };
                var resNoActivate = (IntPtr)wndProcMethod.Invoke(window, argsNoActivate)!;
                bool handledNoActivate = (bool)argsNoActivate[4];
                check(resNoActivate == (IntPtr)3 /* MA_NOACTIVATE */ && handledNoActivate, "WndProcHook returns MA_NOACTIVATE with handled=true outside text controls");
            }

            // Verify unhooking browser events resets flag safely
            var unhookMethod = typeof(MainWindow).GetMethod("UnhookBrowserEvents", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var hookedField = typeof(MainWindow).GetField("_browserEventsHooked", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (unhookMethod != null && hookedField != null)
            {
                hookedField.SetValue(window, true);
                unhookMethod.Invoke(window, null);
                check((bool)hookedField.GetValue(window)! == false, "UnhookBrowserEvents resets _browserEventsHooked safely");
            }
        }
    }
}
