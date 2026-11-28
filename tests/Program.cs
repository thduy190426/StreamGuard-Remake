using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using StreamGuard;
using StreamGuard.Localization;
using StreamGuard.Models;
using StreamGuard.Services;

internal static class Program
{
    private static int _checks;
    private static readonly string Temp = Path.Combine(Path.GetTempPath(), "StreamGuard-localization-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly LocalizationService L = LocalizationService.Current;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0].StartsWith("--captions-", StringComparison.Ordinal)) return CaptionChecks.Fixture(args);
        if (args.Length == 3 && args[0] == "--recovery-crash-fixture") return RecoveryChecks.RunCrashFixture(args);
        if (args.Contains("--audit-child-flood"))
        {
            for (var i = 0; i < 1024; i++) { Console.Out.Write(new string('o', 128)); Console.Error.Write(new string('e', 128)); }
            return 7;
        }
        if (args.Contains("--audit-child-wait")) { Thread.Sleep(30000); return 0; }
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        Directory.CreateDirectory(Temp);
        try
        {
            TestCatalog();
            TestConfiguration();
            AuditChecks.Configuration(Check, Temp);
            AuditChecks.Hotkeys(Check);
            AuditChecks.ChildProcesses(Check);
            RecoveryChecks.Run(Check, Temp);
            CaptionChecks.Run(Check, Temp);
            TranscriptChecks.Run(Check, Temp);
            StreamGuard.Tests.CaptionTranslationChecks.Run(Check, Temp);
            StreamGuard.Tests.CaptionVocabularyChecks.Run(Check, Temp);
            StreamGuard.Tests.BrowserChecks.Run(Check, Temp);
            TestLiveBindings();
            TestBrowserScripts();
            TestApplicationIdentity();
            ProcessSafetyChecks.Run(Check, Temp);
            TestProcessSafetyTimerAllocation();
            TestWindow();
            CaptureGuardChecks.Run(Check, Temp);
            StreamGuard.Tests.SnippingChecks.Run(Check, Temp);
            StreamGuard.Tests.AiTrafficChecks.Run(Check, Temp);
            StreamGuard.Tests.CompatibilityChecks.Run(Check);
            StreamGuard.Tests.AntigravityInstallerChecks.Run(Check, Temp);
            StreamGuard.Tests.AntigravityAuthBridgeChecks.Run(Check, Temp);
            StreamGuard.Tests.DiagnosticsChecks.Run(Check);
            StreamGuard.Tests.BackupChecks.Run(Check, Temp);
            StreamGuard.Tests.CleanWipeChecks.Run(Check, Temp);
            StreamGuard.Tests.StealthFocusChecks.Run(Check, Temp);
            StreamGuard.Tests.CursorCaptureProtectionChecks.Run(Check, Temp);
            StreamGuard.Tests.ToastNotificationChecks.Run(Check, Temp);
            StreamGuard.Tests.SwitchCooldownChecks.Run(Check);
            StreamGuard.Tests.ThemeChecks.Run(Check, Temp);
            StreamGuard.Tests.OptimizationAuditChecks.Run(Check);
            Console.WriteLine($"PASS: {_checks} checks. Artifacts: {Temp}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static string Fill(string template) => Regex.Replace(template, @"\{(\d+)\}", m => "ARG" + m.Groups[1].Value + "_ứng dụng $1{data}");

    private static void TestCatalog()
    {
        using var stream = typeof(LocalizationService).Assembly.GetManifestResourceStream("StreamGuard.Localization.Strings.json")!;
        var entries = JsonSerializer.Deserialize<LocalizationService.Entry[]>(stream)!;
        Check(entries.Select(e => e.Key).Distinct().Count() == entries.Length, "Duplicate catalog keys");
        foreach (var entry in entries)
        {
            var placeholders = Regex.Matches(entry.English, @"\{\d+\}").Select(m => m.Value).Order().ToArray();
            foreach (var text in new[] { entry.Vietnamese, entry.Source }.OfType<string>())
                Check(placeholders.SequenceEqual(Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).Order()), $"Placeholder mismatch: {entry.Key}");
            Check(!string.IsNullOrWhiteSpace(entry.English) && !string.IsNullOrWhiteSpace(entry.Vietnamese), $"Empty translation: {entry.Key}");
            L.SetLanguage("vi");
            Check(L[entry.Key] == entry.Vietnamese, $"Vietnamese lookup: {entry.Key}");
            Check(L.Translate(Fill(entry.English)) == Fill(entry.Vietnamese), $"English → Vietnamese template: {entry.Key}: {L.Translate(Fill(entry.English))}");
            L.SetLanguage("en");
            Check(L.Translate(Fill(entry.English)) == Fill(entry.English), $"English original preserved: {entry.Key}");
            if (entry.Source != null)
                Check(L.Translate(Fill(entry.Source)) == Fill(entry.English), $"Legacy literal: {entry.Key}");
        }
        Check(LocalizationService.NormalizeLanguage("VI-vn") == "vi", "Normalize Vietnamese culture");
        Check(LocalizationService.NormalizeLanguage(null) == "en", "Null language fallback");
        Check(LocalizationService.NormalizeLanguage("fr") == "en", "Unsupported language fallback");
        L.SetLanguage("en");
        Check(L["Snipping.StartButton"].Contains("Ctrl+Alt+S"), "English start button contains Ctrl+Alt+S");
        Check(L["Snipping.QuickSnipTooltip"].Contains("Ctrl+Alt+S"), "English tooltip contains Ctrl+Alt+S");
        Check(L["Snipping.TrayMenuItem"].Contains("Ctrl+Alt+S"), "English tray item contains Ctrl+Alt+S");
        Check(L["Snipping.CheatSheetLabel"] == "Take protected screenshot", "English cheat sheet label");
        Check(L["Snipping.ActionTakeScreenshot"] == "Take Screenshot", "English action take screenshot");
        Check(L["Snipping.ActionQuickSnip"] == "Protected Screen Snip", "English action quick snip");
        Check(L["Snipping.HotkeyCanceled"] == "Snipping hotkey recording canceled", "English hotkey canceled message");
        L.SetLanguage("vi");
        Check(L["Snipping.StartButton"].Contains("Ctrl+Alt+S"), "Vietnamese start button contains Ctrl+Alt+S");
        Check(L["Snipping.QuickSnipTooltip"].Contains("Ctrl+Alt+S"), "Vietnamese tooltip contains Ctrl+Alt+S");
        Check(L["Snipping.TrayMenuItem"].Contains("Ctrl+Alt+S"), "Vietnamese tray item contains Ctrl+Alt+S");
        Check(L["Snipping.CheatSheetLabel"] == "Chụp màn hình bảo vệ", "Vietnamese cheat sheet label");
        Check(L["Snipping.ActionTakeScreenshot"] == "Chụp màn hình", "Vietnamese action take screenshot");
        Check(L["Snipping.ActionQuickSnip"] == "Chụp màn hình bảo vệ", "Vietnamese action quick snip");
        Check(L["Snipping.HotkeyCanceled"] == "Đã hủy đổi phím tắt chụp màn hình", "Vietnamese hotkey canceled message");
        L.SetLanguage("vi");
        Check(L.Translate("Ready to protect $1; Ứng dụng {0}: Desktop") == "Sẵn sàng bảo vệ $1; Ứng dụng {0}: Desktop", "App name must remain unchanged");
        Check(L.Translate("RAIA contained. Total contained: 3") == "Đã cô lập RAIA. Tổng đã cô lập: 3", "Nested service status");
        Check(L.Translate("Website block applied: raia.edu.vn. Total contained: 3") == "Đã áp dụng chặn trang web: raia.edu.vn. Tổng đã cô lập: 3", "Nested template with a greedy detail argument");
        Check(L.Translate("Invalid hotkey: use a modifier, e.g. Ctrl+Alt+1") == "Phím tắt không hợp lệ: dùng phím bổ trợ, ví dụ Ctrl+Alt+1", "Nested known validation error");
        Check(L.Translate("camera denied, microphone allowed; WMI process-start watcher armed") == "camera bị chặn, micrô được phép; đã bật theo dõi khởi chạy tiến trình WMI", "Composite diagnostic");
        Check(L.Translate("C:\\Native error\\unknown.txt") == "C:\\Native error\\unknown.txt", "Native error fallback");
    }

    private static void TestConfiguration()
    {
        var path = Path.Combine(Temp, "config.json");
        var service = new ConfigService(path);
        Check(service.Load().Count == 0 && service.Language == "en", "Missing config defaults");
        File.WriteAllText(path, "[{\"Name\":\"Legacy\",\"ProcessName\":\"legacy\",\"SecureHotkey\":\"Ctrl+Alt+1\"}]");
        Check(service.Load().Single().Name == "Legacy" && service.Language == "en", "Legacy array config");
        var app = new GuardedApp { Name = "Ứng dụng $1", ProcessName = "example", ExecutablePath = "C:\\Example\\app.exe", IsEnabled = false, SecureHotkey = "Ctrl+Alt+7" };
        Check(service.Save(new[] { app }, true, true, false, false, true, true, "vi-VN"), "Save Vietnamese configuration");
        var read = new ConfigService(path);
        var loaded = read.Load().Single();
        Check(read.Language == "vi" && loaded.Name == app.Name && loaded.SecureHotkey == app.SecureHotkey && !loaded.IsEnabled, "Language and app round-trip");
        Check(read.BlockRaiaWebsite && !read.BlockPortalWebsite && !read.BlockCameraAccess && read.BlockMicrophoneAccess && read.PassiveFocusMode, "Protection preferences round-trip");
        Check(read.Save(new[] { app }, true, true, false, false, true, true), "Save without language argument");
        Check(new ConfigService(path).Load().Count == 1 && JObject.Parse(File.ReadAllText(path))["Language"]!.Value<string>() == "vi", "Existing saves preserve language");
        var json = JObject.Parse(File.ReadAllText(path)); json.Remove("Language"); File.WriteAllText(path, json.ToString());
        Check(new ConfigService(path).Load().Count == 1, "Old object config remains readable");
        json["Language"] = "unknown"; File.WriteAllText(path, json.ToString());
        read = new ConfigService(path); read.Load(); Check(read.Language == "en", "Unknown saved language");
        json["Language"] = null; File.WriteAllText(path, json.ToString());
        read = new ConfigService(path); read.Load(); Check(read.Language == "en", "Null saved language");
        File.WriteAllText(path, "broken json");
        var recovery = new ConfigService(path);
        Check(recovery.Load().Count == 1 && recovery.LoadedFromBackup && recovery.LastLoadError != null, "Corrupt configuration recovers the last valid backup");
        Check(!new ConfigService(Temp).Save(Array.Empty<GuardedApp>(), language: "vi"), "Save errors are reported");
    }

    private static void TestLiveBindings()
    {
        var text = new TextBlock();
        L.SetLanguage("en");
        LocalizationService.BindText(text, TextBlock.TextProperty, "Protected: 2 window(s), desktop=1, hide=1"); Pump();
        Check(text.Text == "Protected: 2 window(s), desktop=1, hide=1", "Initial live status");
        L.SetLanguage("vi"); Pump(); Check(text.Text == "Đã bảo vệ: 2 cửa sổ, desktop=1, ẩn=1", "Existing status switches immediately");
        var app = new GuardedApp { Status = "Queued: visible", SecureHotkey = "Ctrl+Alt+1", IsProtected = true };
        text.DataContext = app;
        // Use a real WPF MultiBinding for model status, as the item template does.
        var binding = new MultiBinding { Converter = TranslationConverter.Instance };
        binding.Bindings.Add(new Binding("Status")); binding.Bindings.Add(new Binding("Language") { Source = L });
        BindingOperations.SetBinding(text, TextBlock.TextProperty, binding); Pump();
        Check(text.Text == "Đang chờ: hiển thị", "Model status in Vietnamese");
        app.Status = "Protected: 4 window(s)"; Pump(); Check(text.Text == "Đã bảo vệ: 4 cửa sổ", "New model status");
        for (var i = 0; i < 25; i++) { L.SetLanguage("en"); Pump(); L.SetLanguage("vi"); Pump(); }
        Check(app.Status == "Protected: 4 window(s)" && app.IsProtected && app.SecureHotkey == "Ctrl+Alt+1", "Repeated switching preserves state and hotkey");
        var parser = typeof(MainWindow).GetMethod("TryParseHotkey", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var language in new[] { "en", "vi" })
        {
            L.SetLanguage(language);
            foreach (var input in new[] { "Ctrl+Alt+7", "Control+Shift+F12", "Ctrl+A" })
            {
                object[] args = { input, (uint)0, (uint)0, "", "" };
                Check((bool)parser.Invoke(null, args)!, $"Hotkey parses in {language}: {input}");
            }
            object[] invalid = { "Ctrl+Alt+Unknown", (uint)0, (uint)0, "", "" };
            Check(!(bool)parser.Invoke(null, invalid)!, "Invalid hotkey is still rejected");
            Check((string)invalid[4] == "supported keys are A-Z, 0-9, and F1-F12", "Raw validation error stays language independent");
        }
    }

    private static void TestBrowserScripts()
    {
        foreach (var language in new[] { "en", "vi" })
        {
            L.SetLanguage(language);
            var script = BrowserScriptService.GetCopyPasteScript();
            Check(!script.Contains("__STREAMGUARD_CLIPBOARD_MESSAGES__"), "Browser script localization resolved");
            Check(script.Contains("__streamGuardClipboardUnlock"), "Embedded browser script loaded");
            var bookmarklet = BrowserScriptService.GetCopyPasteBookmarklet();
            Check(bookmarklet.StartsWith("javascript:") && Uri.UnescapeDataString(bookmarklet[11..]) == "void " + script, "Bookmarklet preserves the complete script");
            File.WriteAllText(Path.Combine(Temp, $"copy-paste-{language}.js"), script);
            File.WriteAllText(Path.Combine(Temp, $"copy-paste-{language}-bookmarklet.txt"), bookmarklet);
        }
        File.WriteAllText(Path.Combine(Temp, "copy-paste-disable.js"), BrowserScriptService.GetDisableCopyPasteScript());
        var status = new TextBlock();
        LocalizationService.BindKey(status, TextBlock.TextProperty, "Clipboard.CopiedConsole");
        L.SetLanguage("en"); Pump(); Check(status.Text.StartsWith("Unlock script copied."), "Copy status uses English");
        L.SetLanguage("vi"); Pump(); Check(status.Text.StartsWith("Đã sao chép script"), "Copy status updates to Vietnamese");
    }

    private static void TestApplicationIdentity()
    {
        var assembly = typeof(MainWindow).Assembly;
        Check(assembly.GetName().Name == "Driver Booster", "Runtime assembly uses the requested executable identity");
        var metadata = FileVersionInfo.GetVersionInfo(assembly.Location);
        Check(metadata.ProductName == "Driver Booster" && metadata.FileDescription == "Driver Booster", "Product and Windows file description use the requested name");
        var ownProcess = typeof(MainWindow).GetMethod("IsOwnProcess", BindingFlags.Static | BindingFlags.NonPublic)!;
        using var current = Process.GetCurrentProcess();
        Check((bool)ownProcess.Invoke(null, new object[] { current.ProcessName })!, "Self-protection recognizes the actual running process independently of the old brand");
        Check(!(bool)ownProcess.Invoke(null, new object[] { "unrelated-test-process" })!, "Unrelated processes are not classified as the application");
    }

    private static void TestProcessSafetyTimerAllocation()
    {
        using var current = Process.GetCurrentProcess();
        int currentPid = current.Id;

        using var testHwndSource = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("Timer-allocation-test-window")
        {
            Width = 10, Height = 10, PositionX = -32000, PositionY = -32000, WindowStyle = unchecked((int)0x80000000)
        });
        IntPtr validHwnd = testHwndSource.Handle;

        // Warmup: JIT and initial cache population
        Check(ProcessSafetyService.Default.CanManageProcess(currentPid), "Warmup CanManageProcess must succeed");
        Check(ProcessSafetyService.Default.CanManageWindow(validHwnd, currentPid), "Warmup CanManageWindow must succeed");

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long startBytes = GC.GetAllocatedBytesForCurrentThread();
        const int iterations = 1000;
        for (int i = 0; i < iterations; i++)
        {
            var isManageableProcess = ProcessSafetyService.Default.CanManageProcess(currentPid);
            var isManageableWindow = ProcessSafetyService.Default.CanManageWindow(validHwnd, currentPid);
            if (!isManageableProcess || !isManageableWindow) throw new InvalidOperationException("Current process and window should both be manageable");
        }
        long endBytes = GC.GetAllocatedBytesForCurrentThread();
        long allocated = endBytes - startBytes;

        // Verify that 1000 rapid calls (equivalent to 50 seconds of 50ms timer) allocate 0 bytes on the heap
        Check(allocated == 0, $"ProcessSafety timer calls allocated 0 bytes on managed heap (actual: {allocated} bytes for {iterations * 2} calls)");
    }

    private static void TestWindow()
    {
        var errors = new BindingErrors();
        PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        // Use a plain Application: App schedules real startup when the dispatcher is pumped.
        var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var themeStream = typeof(Program).Assembly.GetManifestResourceStream("TestTheme.xml")!;
        var theme = XDocument.Load(themeStream).Descendants().First(e => e.Name.LocalName == "ResourceDictionary");
        theme.SetAttributeValue(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml");
        theme.SetAttributeValue(XNamespace.Xmlns + "ui", "urn:streamguard:ui");
        var themeContext = new System.Windows.Markup.ParserContext
        { XamlTypeMapper = new System.Windows.Markup.XamlTypeMapper(Array.Empty<string>()) };
        themeContext.XamlTypeMapper.AddMappingProcessingInstruction("urn:streamguard:ui", "StreamGuard.Controls", typeof(MainWindow).Assembly.GetName().Name!);
        application.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(theme.ToString(), themeContext);
        var configPath = Path.Combine(Temp, "window-config.json");
        var config = new ConfigService(configPath);
        config.Save(new[] { new GuardedApp { Name = "Demo", ProcessName = "nonexistent-test-process", SecureHotkey = "Ctrl+Alt+9", IsEnabled = false } }, false, false, false, true, false, true, "en");
        var testCurtain = new TestCurtain();
        var window = (MainWindow)Activator.CreateInstance(typeof(MainWindow), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { new ConfigService(configPath), testCurtain }, null)!;
        Check(window.Title == "Driver Booster" && ((TextBlock)window.FindName("AppBrandText")).Text == "Driver Booster", "Window title and sidebar display the requested application name");
        // Never show the window: Loaded applies real device preferences and global hotkeys.
        HoverFeedbackChecks.Run(window, Check);
        StreamGuard.Tests.BrowserChecks.RunUiChecks(window, Check);
        StreamGuard.Tests.AntigravityInstallerChecks.RunUiChecks(window, Check);
        StreamGuard.Tests.AntigravityAuthBridgeChecks.RunUiChecks(window, Check);
        StreamGuard.Tests.DiagnosticsChecks.RunUiChecks(window, Check);
        StreamGuard.Tests.StealthFocusChecks.RunUiChecks(window, Check);
        var autoStartSettings = (CheckBox)window.FindName("AutoStartShieldCheckBox");
        var autoStartQuick = (CheckBox)window.FindName("AutoStartShieldQuickCheckBox");
        var btnExport = (Button)window.FindName("BtnExportSettings");
        var btnImport = (Button)window.FindName("BtnImportSettings");
        Check(autoStartSettings != null && autoStartQuick != null, "AutoStartShield checkboxes exist in both ViewSettings and ViewProtected");
        Check(btnExport != null && btnImport != null, "Export and Import buttons exist in ViewSettings");
        var inAppCloak = (CheckBox)window.FindName("InAppCursorCloakCheckBox");
        var globalCloak = (CheckBox)window.FindName("GlobalCursorCloakCheckBox");
        Check(inAppCloak != null && globalCloak != null, "InAppCursorCloak and GlobalCursorCloak checkboxes exist in MainWindow UI");
        Check(autoStartSettings!.IsChecked == false && autoStartQuick!.IsChecked == false, "AutoStartShield is unchecked by default");
        autoStartSettings.IsChecked = true; Pump();
        Check(autoStartQuick!.IsChecked == true, "AutoStartShield checkboxes are dynamically synchronized");
        autoStartQuick.IsChecked = false; Pump();
        Check(autoStartSettings.IsChecked == false, "Quick toggle synchronizes back to settings switch");
        var combo = (ComboBox)window.FindName("LanguageComboBox");
        var title = (TextBlock)window.FindName("TabProtectedText");
        var button = (TextBlock)window.FindName("BtnProtectText");
        var rawBefore = JObject.Parse(File.ReadAllText(configPath)); rawBefore.Remove("Language");
        var apps = (ObservableCollection<GuardedApp>)typeof(MainWindow).GetField("_apps", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var originalApp = apps.Single();
        var protectionField = typeof(MainWindow).GetField("_isProtecting", BindingFlags.Instance | BindingFlags.NonPublic)!;
        protectionField.SetValue(window, true);
        originalApp.IsProtected = true;
        var statusMethod = typeof(MainWindow).GetMethod("UpdateStatus", BindingFlags.Instance | BindingFlags.NonPublic)!;
        statusMethod.Invoke(window, new object[] { "Ready to protect Demo", true });
        combo.SelectedValue = "vi"; Pump();
        Check(title.Text == "Bảng bảo vệ" && button.Text == "Bắt đầu bảo vệ", $"Real XAML language switch: title={title.Text}, button={button.Text}, language={L.Language}, selected={combo.SelectedValue}; {string.Join("\n", errors.Messages)}");
        Check(((TextBlock)window.FindName("StatusText")).Text == "Sẵn sàng bảo vệ Demo", "Existing banner language switch");
        var rawAfter = JObject.Parse(File.ReadAllText(configPath)); Check(rawAfter["Language"]!.Value<string>() == "vi", "Selector persists language"); rawAfter.Remove("Language");
        Check(JToken.DeepEquals(rawBefore, rawAfter), "Selector preserves all other configuration");
        Check(ReferenceEquals(originalApp, apps.Single()) && originalApp.SecureHotkey == "Ctrl+Alt+9", "Switch does not rebuild apps or hotkeys");
        Check((bool)protectionField.GetValue(window)! && originalApp.IsProtected, "Language switch preserves active protection state");
        typeof(MainWindow).GetMethod("SetProtectionUiState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { true });
        Pump(); Check(button.Text == "Đang bảo vệ", "Active shield button translation");
        combo.SelectedValue = "en"; Pump(); Check(button.Text == "Shield Active", "Active state survives language change");
        var sidebarConfigBefore = File.ReadAllText(configPath);
        SidebarChecks.Run(window, Check);
        Check(File.ReadAllText(configPath) == sidebarConfigBefore && ReferenceEquals(originalApp, apps.Single()) &&
            (bool)protectionField.GetValue(window)! && originalApp.IsProtected, "Sidebar resizing preserves configuration, app list and active protection");
        foreach (var lang in new[] { "vi", "en" })
        {
            combo.SelectedValue = lang; Pump();
            foreach (var tab in new[] { "TabProtected", "TabAvailable", "TabThreats", "TabSettings", "TabRecovery", "TabCaptions", "TabBrowser" })
            {
                ((System.Windows.Controls.RadioButton)window.FindName(tab)).IsChecked = true; Pump();
                if (tab == "TabAvailable")
                {
                    var deadline = DateTime.UtcNow.AddSeconds(10);
                    var scanFlag = typeof(MainWindow).GetField("_isRefreshingAvailableWindows", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    while ((bool)scanFlag.GetValue(window)! && DateTime.UtcNow < deadline)
                    {
                        var frame = new DispatcherFrame();
                        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
                        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
                        timer.Start(); Dispatcher.PushFrame(frame);
                    }
                    Check(!(bool)scanFlag.GetValue(window)!, "Window scan completes after language switching");
                }
                // Off-screen visuals have no presentation clock. Complete the fade explicitly for layout snapshots.
                foreach (var name in new[] { "ViewProtected", "ViewAvailable", "ViewThreats", "ViewSettings", "ViewRecovery", "ViewCaptions", "ViewBrowser" })
                {
                    var view = (FrameworkElement)window.FindName(name);
                    view.BeginAnimation(UIElement.OpacityProperty, null); view.Opacity = 1;
                }
                Render(window, lang + "-" + tab, 860, 600);
                Render(window, lang + "-" + tab, 640, 460);
                if (tab == "TabSettings")
                {
                    foreach (var sidebarWidth in new[] { 200d, 300d })
                    {
                        Render(window, lang + "-SidebarSetup", 860, 600);
                        typeof(MainWindow).GetMethod("ResizeSidebarTo", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { sidebarWidth });
                        Render(window, lang + "-Sidebar" + sidebarWidth, 860, 600);
                    }
                    typeof(MainWindow).GetMethod("ResizeSidebarTo", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { 210d });
                }
                if (tab == "TabThreats")
                {
                    var copyButton = (Button)window.FindName("BtnCopyPasteScript");
                    Check(copyButton.Content as string == (lang == "vi" ? "Sao chép script mở khóa" : "Copy unlock script"), "Copy/paste tool buttons follow the selected language");
                    copyButton.BringIntoView(); Pump();
                    Render(window, lang + "-ClipboardTools", 640, 460);
                }
                if (tab == "TabBrowser")
                {
                    var urlBox = (TextBox)window.FindName("TxtBrowserUrl");
                    Check(urlBox.ActualWidth >= 80, "Address bar has usable width: " + urlBox.ActualWidth);
                    var chromeBtn = (Button)window.FindName("BtnOpenInExternalChrome");
                    Check(chromeBtn.ActualWidth > 0, "External Chrome button is laid out: " + chromeBtn.ActualWidth);
                }
            }
        }
        var search = (TextBox)window.FindName("SearchBox");
        search.Text = "__streamguard-no-match-" + Guid.NewGuid().ToString("N");
        var query = search.Text;
        combo.SelectedValue = "vi"; Pump();
        Check(search.Text == query && ((TextBlock)window.FindName("AvailableEmptyStateTitle")).Text == "Không tìm thấy ứng dụng phù hợp", "Search and empty state survive switch to Vietnamese");
        combo.SelectedValue = "en"; Pump();
        Check(search.Text == query && ((TextBlock)window.FindName("AvailableEmptyStateTitle")).Text == "No matching applications", "Search and empty state survive switch to English");
        var focusPredicate = typeof(MainWindow).GetMethod("IsTextEntryVisual", BindingFlags.NonPublic | BindingFlags.Static)!;
        Check((bool)focusPredicate.Invoke(null, new object[] { combo })!, "Language selector supports keyboard activation in passive focus mode");
        Check(!(bool)focusPredicate.Invoke(null, new object[] { new Button() })!, "Ordinary buttons retain passive focus behavior");
        TestCaptureGuardTransitions(window, testCurtain);
        ProcessSafetyChecks.CheckUiGuards(window, Check);
        AuditChecks.Window(Check, Temp);
        AuditChecks.AvailableWindowCancellation(Check, Temp);
        RecoveryChecks.Ui(Check, Temp);
        CaptionChecks.Ui(window, Check, Temp);
        StreamGuard.Tests.BackupChecks.RunUiChecks(window, Check);
        StreamGuard.Tests.ThemeChecks.RunUiChecks(window, configPath, Check);
        TestUiUxImprovements(window, configPath);
        Check(errors.Messages.Count == 0, "WPF binding errors: " + string.Join("\n", errors.Messages));
        // Dispose only local resources. CleanupAndExit also kills managed apps and is deliberately excluded.
        ((IDisposable)typeof(MainWindow).GetField("_vdService", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!).Dispose();
        ((IDisposable)typeof(MainWindow).GetField("_threatBlocker", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!).Dispose();
        ((IDisposable)typeof(MainWindow).GetField("_systemToolsBlocker", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!).Dispose();
        ((IDisposable)typeof(MainWindow).GetField("_antigravityNotifications", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!).Dispose();
        File.WriteAllText(Path.Combine(Temp, "binding-errors.txt"), string.Join("\n", errors.Messages));
    }

    private static void Render(MainWindow window, string language, int width, int height)
    {
        var content = (FrameworkElement)window.Content;
        window.Width = width; window.Height = height;
        // Mirror Window_SizeChanged for off-screen rendering (no HWND means no native resize event).
        typeof(MainWindow).GetMethod("ApplySidebarLayout", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { (double)width });
        window.Measure(new Size(width, height)); window.Arrange(new Rect(0, 0, width, height));
        ((System.Windows.Controls.Grid)content).Background = window.Background;
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout(); Pump();
        if (language.Contains("Sidebar"))
        {
            var settings = (FrameworkElement)window.FindName("ViewSettings");
            void CheckVisibleBounds(DependencyObject parent)
            {
                for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
                {
                    var child = VisualTreeHelper.GetChild(parent, index);
                    if (child is TextBlock text && (text.Text.StartsWith("Ctrl +") || text.Text.StartsWith("Shift +")))
                    {
                        var right = text.TranslatePoint(new System.Windows.Point(text.ActualWidth, 0), content).X;
                        Check(right <= width, "Hotkey remains inside the main view after sidebar resizing: " + language);
                    }
                    CheckVisibleBounds(child);
                }
            }
            CheckVisibleBounds(settings);
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(Temp, $"settings-{language}-{width}.png")); encoder.Save(file);
    }

    private static void TestCaptureGuardTransitions(MainWindow window, TestCurtain curtain)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainWindow).GetField("_captureCurtainHotkeyReady", flags)!.SetValue(window, true);
        typeof(MainWindow).GetField("_captureScanInProgress", flags)!.SetValue(window, true);
        typeof(MainWindow).GetField("_captureAssessment", flags)!.SetValue(window, CaptureRiskService.Assess(new[] { "obs64" }));
        typeof(MainWindow).GetMethod("SetProtectionUiState", flags)!.Invoke(window, new object[] { true });
        var option = (CheckBox)window.FindName("CaptureGuardCheckBox");
        Check(option.IsChecked == true, "Recorder guard is enabled by default in the loaded UI");
        option.IsChecked = true; Pump();
        Check(curtain.IsVisible, "Default curtain activates during protection when a recorder is present");
        typeof(MainWindow).GetMethod("TogglePrivacyCurtain", flags)!.Invoke(window, null); Pump();
        Check(!curtain.IsVisible && (bool)typeof(MainWindow).GetField("_captureGuardPaused", flags)!.GetValue(window)!, "Recovery hotkey pauses and removes the automatic curtain");
        typeof(MainWindow).GetMethod("UpdateCaptureGuardState", flags)!.Invoke(window, null);
        Check(!curtain.IsVisible, "Subsequent updates do not immediately re-cover after recovery");
        typeof(MainWindow).GetMethod("BtnResumeCaptureGuard_Click", flags)!.Invoke(window, new object[] { window, new RoutedEventArgs() });
        Check(curtain.IsVisible, "Explicit resume re-enables the curtain");
        curtain.Complete = false;
        typeof(MainWindow).GetMethod("UpdateCaptureGuardState", flags)!.Invoke(window, null); Pump();
        Check(((TextBlock)window.FindName("CaptureGuardStateText")).Text.StartsWith("Unable to cover"), "A partial curtain is reported as a failure");
        typeof(MainWindow).GetMethod("SetProtectionUiState", flags)!.Invoke(window, new object[] { false });
        Check(!curtain.IsVisible, "Stopping protection removes the automatic curtain");
        option.IsChecked = false;
        Check(!curtain.IsVisible, "Disabling the guard leaves the desktop uncovered");
        typeof(MainWindow).GetField("_captureCurtainHotkeyReady", flags)!.SetValue(window, false);
        typeof(MainWindow).GetMethod("SetProtectionUiState", flags)!.Invoke(window, new object[] { true });
        option.IsChecked = true; Pump();
        Check(!curtain.IsVisible && ((TextBlock)window.FindName("CaptureGuardStateText")).Text.StartsWith("Automatic curtain unavailable"), "An unavailable recovery hotkey prevents automatic covering");
        option.IsChecked = false;
    }

    private static void TestUiUxImprovements(MainWindow window, string configPath)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var apps = (ObservableCollection<GuardedApp>)typeof(MainWindow).GetField("_apps", flags)!.GetValue(window)!;
        var available = (ObservableCollection<StreamGuard.Helpers.WindowInfo>)typeof(MainWindow).GetField("_availableWindows", flags)!.GetValue(window)!;
        var app = apps.First();
        var winInfo = new StreamGuard.Helpers.WindowInfo { ProcessName = app.ProcessName, Title = "Demo Window", Handle = new IntPtr(12345) };
        available.Add(winInfo);
        typeof(MainWindow).GetMethod("SyncAvailableWindowsShieldState", flags)!.Invoke(window, null);
        Check(winInfo.IsInShieldList, "Available window matching a guarded app is marked as in shield list");

        // Verify per-app enable toggle handler persists and updates status/protection
        app.IsProtected = false;
        var toggle = new CheckBox { Tag = app, IsChecked = false };
        typeof(MainWindow).GetMethod("AppEnabledCheckBox_Click", flags)!.Invoke(window, new object[] { toggle, new RoutedEventArgs() });
        Check(!app.IsEnabled && app.Status == "Disabled", "Toggling app off updates status to Disabled");
        Check(!new ConfigService(configPath).Load().Single().IsEnabled, "Toggling app off persists to config");

        toggle.IsChecked = true;
        typeof(MainWindow).GetMethod("AppEnabledCheckBox_Click", flags)!.Invoke(window, new object[] { toggle, new RoutedEventArgs() });
        Check(app.IsEnabled && app.Status.StartsWith("Queued:"), "Toggling app on restores queued status");
        Check(new ConfigService(configPath).Load().Single().IsEnabled, "Toggling app on persists to config");

        // Verify Stop button enabled state reflects active protection and icon spacing
        var btnStop = (Button)window.FindName("BtnStop");
        var btnStopIcon = (System.Windows.Shapes.Path)window.FindName("BtnStopIcon");
        Check(btnStopIcon.Margin.Right >= 8, "Stop button square icon has comfortable spacing from text");
        typeof(MainWindow).GetMethod("SetProtectionUiState", flags)!.Invoke(window, new object[] { false });
        Check(!btnStop.IsEnabled, "Stop button is disabled when protection and curtains are idle");
        typeof(MainWindow).GetMethod("SetProtectionUiState", flags)!.Invoke(window, new object[] { true });
        Check(btnStop.IsEnabled, "Stop button is enabled when protection is active");
        typeof(MainWindow).GetMethod("SetProtectionUiState", flags)!.Invoke(window, new object[] { false });

        // Verify responsive UI & font scaling across small, normal, and maximized states
        var scaleTransform = (ScaleTransform)window.FindName("MainContentScaleTransform");
        var tabProtectedText = (TextBlock)window.FindName("TabProtectedText");
        var applyScale = typeof(MainWindow).GetMethod("ApplyResponsiveScale", flags)!;
        var isCustomMaxField = typeof(MainWindow).GetField("_isCustomMaximized", flags)!;

        isCustomMaxField.SetValue(window, false);
        applyScale.Invoke(window, new object[] { 640d, 460d });
        Check(scaleTransform.ScaleX < 1.0 && scaleTransform.ScaleX >= 0.92, "Small window uses compact proportional scale");
        Check(tabProtectedText.FontSize < 12.0, "Small window uses compact sidebar font size");

        applyScale.Invoke(window, new object[] { 860d, 600d });
        Check(Math.Abs(scaleTransform.ScaleX - 1.0) < 0.001, "Normal window uses standard 1.0 scale");
        Check(Math.Abs(tabProtectedText.FontSize - 12.0) < 0.01, "Normal window uses standard 12pt sidebar font size");

        isCustomMaxField.SetValue(window, true);
        applyScale.Invoke(window, new object[] { 1920d, 1080d });
        Check(scaleTransform.ScaleX >= 1.18 && scaleTransform.ScaleX <= 1.30, "Maximized window scales up content for easier reading and interaction");
        Check(tabProtectedText.FontSize >= 14.0, "Maximized window scales up sidebar typography");

        isCustomMaxField.SetValue(window, false);
        applyScale.Invoke(window, new object[] { 860d, 600d });
        Check(Math.Abs(scaleTransform.ScaleX - 1.0) < 0.001, "Restoring window from maximized returns to standard 1.0 scale");

        // Verify System Administration Tools Lock switch (Task Manager, Registry Editor, Command Prompt, Group Policy Editor)
        var shouldCloseMethod = typeof(SystemToolsBlockerService).GetMethod("ShouldCloseToolWindow", BindingFlags.Static | BindingFlags.NonPublic)!;
        Check((bool)shouldCloseMethod.Invoke(null, new object?[] { "Taskmgr", "Task Manager", "TaskManagerWindow" })!, "System tools blocker matches Task Manager");
        Check((bool)shouldCloseMethod.Invoke(null, new object?[] { "regedit", "Registry Editor", "RegEdit_RegEdit" })!, "System tools blocker matches Registry Editor");
        Check((bool)shouldCloseMethod.Invoke(null, new object?[] { "cmd", "Command Prompt", "ConsoleWindowClass" })!, "System tools blocker matches Command Prompt");
        Check((bool)shouldCloseMethod.Invoke(null, new object?[] { "mmc", "Local Group Policy Editor", "MMCMainFrame" })!, "System tools blocker matches Group Policy Editor");
        Check(!(bool)shouldCloseMethod.Invoke(null, new object?[] { "mmc", "Device Manager", "MMCMainFrame" })!, "System tools blocker ignores unrelated MMC snap-ins");
        Check(!(bool)shouldCloseMethod.Invoke(null, new object?[] { "explorer", "File Explorer", "CabinetWClass" })!, "System tools blocker ignores normal user windows");

        var policyCalls = new List<bool>();
        var blockerField = typeof(MainWindow).GetField("_systemToolsBlocker", flags)!;
        ((IDisposable)blockerField.GetValue(window)!).Dispose();
        var mockBlocker = new SystemToolsBlockerService(enable => { policyCalls.Add(enable); return true; });
        blockerField.SetValue(window, mockBlocker);

        var sysToolsCheckBox = (CheckBox)window.FindName("BlockSystemToolsCheckBox");
        var sysToolsStatusText = (TextBlock)window.FindName("SystemToolsStatusText");
        Check(sysToolsCheckBox.IsChecked == false, "System tools blocker is off by default");

        sysToolsCheckBox.IsChecked = true; Pump();
        Check(mockBlocker.IsEnabled && policyCalls.LastOrDefault() == true, "Enabling switch activates system tools blocker");
        var cfgAfterEnable = new ConfigService(configPath); cfgAfterEnable.Load();
        Check(cfgAfterEnable.BlockSystemTools, "Enabling system tools switch persists to config");
        Check(sysToolsStatusText.Text.Contains("Locked") || sysToolsStatusText.Text.Contains("Đang chặn"), "System tools status text reflects locked state");

        window.PanicRestoreAll(); Pump();
        Check(!mockBlocker.IsEnabled && sysToolsCheckBox.IsChecked == false && policyCalls.LastOrDefault() == false, "Panic restore automatically unlocks system tools");

        sysToolsCheckBox.IsChecked = true; Pump();
        sysToolsCheckBox.IsChecked = false; Pump();
        var cfgAfterDisable = new ConfigService(configPath); cfgAfterDisable.Load();
        Check(!mockBlocker.IsEnabled && !cfgAfterDisable.BlockSystemTools && policyCalls.LastOrDefault() == false, "Disabling switch unlocks system tools and persists to config");
        Check(sysToolsStatusText.Text.Contains("Unlocked") || sysToolsStatusText.Text.Contains("Đang mở khóa"), "System tools status text reflects unlocked state");

        // Verify Emergency Restore Static Script (.bat) generation
        var tempEmergencyDir = Path.Combine(Temp, "emergency-test");
        var scriptPath = SystemToolsBlockerService.EnsureEmergencyRestoreScript(tempEmergencyDir);
        Check(File.Exists(scriptPath), "Emergency restore script generated in target directory");
        var scriptContent = File.ReadAllText(scriptPath);
        Check(scriptContent.Contains("DisableTaskMgr") &&
              scriptContent.Contains("DisableRegistryTools") &&
              scriptContent.Contains("DisableCMD") &&
              scriptContent.Contains("Restrict_Run") &&
              scriptContent.Contains("RestrictRun") &&
              scriptContent.Contains("chcp 65001") &&
              scriptContent.Contains("net session") &&
              scriptContent.Contains("reg delete"),
              "Emergency restore script contains proper registry delete commands for all administrative tools and self-elevation logic");
        Check(scriptContent.Contains("[5/5]") &&
              scriptContent.Contains("SystemParametersInfo(0x0057, 0, [IntPtr]::Zero, 2)"),
              "Emergency restore script contains step 5/5 reloading Windows system cursors with SPIF_SENDCHANGE");

        // Verify Emergency Unlock method and UI button flow
        sysToolsCheckBox.IsChecked = true; Pump();
        Check(mockBlocker.IsEnabled, "System tools blocker re-enabled prior to emergency unlock test");
        window.EmergencyUnlockSystemTools(); Pump();
        var cfgAfterEmergency = new ConfigService(configPath); cfgAfterEmergency.Load();
        Check(!mockBlocker.IsEnabled && sysToolsCheckBox.IsChecked == false && !cfgAfterEmergency.BlockSystemTools && policyCalls.LastOrDefault() == false,
              "EmergencyUnlockSystemTools unlocks tools, unticks UI switch, updates config, and invokes restore");
        Check(sysToolsStatusText.Text.Contains("Unlocked") || sysToolsStatusText.Text.Contains("Đang mở khóa"),
              "System tools status text reflects unlocked state after emergency unlock");

        // Verify ForceUnlockAllTools static call executes safely
        Check(SystemToolsBlockerService.ForceUnlockAllTools(), "SystemToolsBlockerService.ForceUnlockAllTools executes safely without exception");

        // Verify Real Registry Lockout Simulation and Sweeping using writable HKCU test subkeys
        const string testBase = @"Software\StreamGuardTestPolicies";
        var testSys = $@"{testBase}\System";
        var testWin = $@"{testBase}\WindowsSystem";
        var testMmc = $@"{testBase}\MMC";
        var testExp = $@"{testBase}\Explorer";
        try
        {
            using (var sys = Registry.CurrentUser.CreateSubKey(testSys, writable: true))
            {
                sys?.SetValue(SystemToolsBlockerService.DisableTaskMgrValueName, 1, RegistryValueKind.DWord);
                sys?.SetValue(SystemToolsBlockerService.DisableRegistryToolsValueName, "1", RegistryValueKind.String);
            }
            using (var win = Registry.CurrentUser.CreateSubKey(testWin, writable: true))
            {
                win?.SetValue(SystemToolsBlockerService.DisableCmdValueName, 2L, RegistryValueKind.QWord);
            }
            using (var mmc = Registry.CurrentUser.CreateSubKey(testMmc, writable: true))
            {
                mmc?.SetValue(SystemToolsBlockerService.RestrictRunValueName, 1, RegistryValueKind.DWord);
                mmc?.SetValue("RestrictRun", 1, RegistryValueKind.DWord);
            }
            using (var exp = Registry.CurrentUser.CreateSubKey(testExp, writable: true))
            {
                exp?.SetValue("RestrictRun", "1", RegistryValueKind.String);
            }

            Check(SystemToolsBlockerService.IsAnyToolBlockedCore(Registry.CurrentUser, testSys, testWin, testMmc, testExp),
                  "IsAnyToolBlockedCore accurately detects simulated dirty registry lockout keys with varied types");

            var sweepSuccess = SystemToolsBlockerService.ForceUnlockToolsCore(Registry.CurrentUser, testSys, testWin, testMmc, testExp);
            Check(sweepSuccess, "ForceUnlockToolsCore reports success when clearing simulated registry locks");
            Check(!SystemToolsBlockerService.IsAnyToolBlockedCore(Registry.CurrentUser, testSys, testWin, testMmc, testExp),
                  "IsAnyToolBlockedCore reports all tools clean after ForceUnlockToolsCore");

            using (var sysCheck = Registry.CurrentUser.OpenSubKey(testSys, writable: false))
            {
                Check(sysCheck?.GetValue(SystemToolsBlockerService.DisableTaskMgrValueName) == null, "DisableTaskMgr registry key successfully deleted");
                Check(sysCheck?.GetValue(SystemToolsBlockerService.DisableRegistryToolsValueName) == null, "DisableRegistryTools registry key successfully deleted");
            }
            using (var winCheck = Registry.CurrentUser.OpenSubKey(testWin, writable: false))
            {
                Check(winCheck?.GetValue(SystemToolsBlockerService.DisableCmdValueName) == null, "DisableCMD registry key successfully deleted");
            }
            using (var mmcCheck = Registry.CurrentUser.OpenSubKey(testMmc, writable: false))
            {
                Check(mmcCheck?.GetValue(SystemToolsBlockerService.RestrictRunValueName) == null, "Restrict_Run registry key successfully deleted");
                Check(mmcCheck?.GetValue("RestrictRun") == null, "RestrictRun registry key in MMC successfully deleted");
            }
            using (var expCheck = Registry.CurrentUser.OpenSubKey(testExp, writable: false))
            {
                Check(expCheck?.GetValue("RestrictRun") == null, "RestrictRun registry key in Explorer successfully deleted");
            }

            // Verify IsBlockedValue helper edge cases
            Check(SystemToolsBlockerService.IsBlockedValue(1) &&
                  SystemToolsBlockerService.IsBlockedValue(2L) &&
                  SystemToolsBlockerService.IsBlockedValue("1") &&
                  SystemToolsBlockerService.IsBlockedValue(new byte[] { 1 }) &&
                  !SystemToolsBlockerService.IsBlockedValue(null) &&
                  !SystemToolsBlockerService.IsBlockedValue(0) &&
                  !SystemToolsBlockerService.IsBlockedValue("0") &&
                  !SystemToolsBlockerService.IsBlockedValue(new byte[] { 0 }),
                  "IsBlockedValue properly evaluates integers, longs, strings, byte arrays, and falsy values");

            // Verify Cross-Thread EmergencyUnlockSystemTools invocation
            sysToolsCheckBox.IsChecked = true; Pump();
            Check(mockBlocker.IsEnabled, "Blocker re-armed before cross-thread test");
            Task.Run(() => window.EmergencyUnlockSystemTools()).Wait(); Pump();
            Check(!mockBlocker.IsEnabled && sysToolsCheckBox.IsChecked == false,
                  "EmergencyUnlockSystemTools invoked across thread boundaries executes safely via Dispatcher");
        }
        finally
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(testBase, throwOnMissingSubKey: false); } catch { }
            SystemToolsBlockerService.ForceUnlockAllTools();
        }

        // Verify Antigravity & Antigravity IDE notification guard service and UI switch
        var tempGeminiConfig = Path.Combine(Temp, "gemini-config.json");
        File.WriteAllText(tempGeminiConfig, "{\"userSettings\":{\"themeMode\":\"THEME_MODE_DARK\",\"enableSoundsForSpecialEvents\":true}}");
        var tempIdeDir = Path.Combine(Temp, "Antigravity IDE", "User");
        Directory.CreateDirectory(tempIdeDir);
        var tempIdeSettings = Path.Combine(tempIdeDir, "settings.json");
        File.WriteAllText(tempIdeSettings, "{\"workbench.iconTheme\":\"material-icon-theme\"}");

        Check(AntigravityNotificationsService.UpdateGeminiSharedConfig(tempGeminiConfig, mute: true), "Gemini config updates when muting notifications");
        var geminiMuted = JObject.Parse(File.ReadAllText(tempGeminiConfig));
        Check(geminiMuted["userSettings"]!["themeMode"]!.Value<string>() == "THEME_MODE_DARK" &&
              geminiMuted["userSettings"]![AntigravityNotificationsService.EnableNotificationsKey]!.Value<bool>() == false &&
              geminiMuted["userSettings"]![AntigravityNotificationsService.EnableSoundsKey]!.Value<bool>() == false,
              "Muting sets enableNotificationsForSpecialEvents=false and enableSoundsForSpecialEvents=false while preserving other userSettings");

        Check(AntigravityNotificationsService.UpdateIdeSettingsJson(tempIdeSettings, mute: true), "Antigravity IDE settings update when muting notifications");
        var ideMuted = JObject.Parse(File.ReadAllText(tempIdeSettings));
        Check(ideMuted["workbench.iconTheme"]!.Value<string>() == "material-icon-theme" &&
              ideMuted[AntigravityNotificationsService.DoNotDisturbKey]!.Value<bool>() == true,
              "Muting enables Do Not Disturb in Antigravity IDE while preserving existing settings");

        Check(AntigravityNotificationsService.UpdateGeminiSharedConfig(tempGeminiConfig, mute: false) &&
              AntigravityNotificationsService.UpdateIdeSettingsJson(tempIdeSettings, mute: false),
              "Unmuting restores Gemini config and Antigravity IDE settings");
        var geminiRestored = JObject.Parse(File.ReadAllText(tempGeminiConfig));
        var ideRestored = JObject.Parse(File.ReadAllText(tempIdeSettings));
        Check(geminiRestored["userSettings"]![AntigravityNotificationsService.EnableNotificationsKey]!.Value<bool>() == true &&
              geminiRestored["userSettings"]![AntigravityNotificationsService.EnableSoundsKey]!.Value<bool>() == true &&
              ideRestored[AntigravityNotificationsService.DoNotDisturbKey] == null,
              "Unmuting restores special event notifications and removes IDE Do Not Disturb override");

        var agyCalls = new List<bool>();
        var agyField = typeof(MainWindow).GetField("_antigravityNotifications", flags)!;
        ((IDisposable)agyField.GetValue(window)!).Dispose();
        var mockAgyService = new AntigravityNotificationsService(mute => { agyCalls.Add(mute); return true; });
        agyField.SetValue(window, mockAgyService);

        var agyCheckBox = (CheckBox)window.FindName("MuteAntigravityNotificationsCheckBox");
        var agyStatusText = (TextBlock)window.FindName("AntigravityNotificationsStatusText");
        Check(agyCheckBox.IsChecked == false, "Antigravity notification guard is off by default");

        agyCheckBox.IsChecked = true; Pump();
        Check(mockAgyService.IsMuted && agyCalls.LastOrDefault() == true, "Enabling switch activates Antigravity notification mute");
        var cfgAfterAgyMute = new ConfigService(configPath); cfgAfterAgyMute.Load();
        Check(cfgAfterAgyMute.MuteAntigravityNotifications, "Enabling Antigravity notification mute persists to config");
        Check(agyStatusText.Text.Contains("Muted") || agyStatusText.Text.Contains("Đã tắt thông báo"), "Antigravity notification status text reflects muted state");

        agyCheckBox.IsChecked = false; Pump();
        var cfgAfterAgyUnmute = new ConfigService(configPath); cfgAfterAgyUnmute.Load();
        Check(!mockAgyService.IsMuted && !cfgAfterAgyUnmute.MuteAntigravityNotifications && agyCalls.LastOrDefault() == false, "Disabling switch restores Antigravity notifications and persists to config");
        Check(agyStatusText.Text.Contains("Active") || agyStatusText.Text.Contains("Đang bật thông báo"), "Antigravity notification status text reflects active state");

        // Verify Cursor Shield service, Win32 constants, and UI controls
        Check(CursorShieldService.SPI_SETCURSORS == 0x0057 &&
              CursorShieldService.SPIF_UPDATEINIFILE == 0x01 &&
              CursorShieldService.SPIF_SENDCHANGE == 0x02,
              "CursorShieldService constants match Win32 SPI_SETCURSORS, SPIF_UPDATEINIFILE, SPIF_SENDCHANGE");
        Check(CursorShieldService.RestoreAllCursors(), "CursorShieldService.RestoreAllCursors executes Win32 reload successfully");

        var cursorShieldCheckBox = (CheckBox)window.FindName("EnableCursorShieldCheckBox");
        var cursorShieldStatusText = (TextBlock)window.FindName("CursorShieldStatusText");
        Check(cursorShieldCheckBox.IsChecked == false, "Cursor shield toggle is off by default");

        cursorShieldCheckBox.IsChecked = true; Pump();
        var cfgAfterCursorShield = new ConfigService(configPath); cfgAfterCursorShield.Load();
        Check(cfgAfterCursorShield.EnableCursorShield, "Enabling cursor shield switch persists to config");
        Check(cursorShieldStatusText.Text.Contains("Shielded") || cursorShieldStatusText.Text.Contains("Đang bảo vệ"), "Cursor shield status text reflects active state");

        // Verify manual restore button triggers RestoreSystemCursorsManual
        window.RestoreSystemCursorsManual(); Pump();
        Check(CursorShieldService.RestoreAllCursors(), "RestoreSystemCursorsManual executed cleanly");

        cursorShieldCheckBox.IsChecked = false; Pump();
        var cfgAfterCursorShieldOff = new ConfigService(configPath); cfgAfterCursorShieldOff.Load();
        Check(!cfgAfterCursorShieldOff.EnableCursorShield, "Disabling cursor shield switch persists to config");
        Check(cursorShieldStatusText.Text.Contains("Normal") || cursorShieldStatusText.Text.Contains("Bình thường"), "Cursor shield status text reflects normal state");

        // Verify protection state decoupling:
        var cursorShieldField = typeof(MainWindow).GetField("_cursorShield", flags)!;
        var cursorShieldInstance = (CursorShieldService)cursorShieldField.GetValue(window)!;
        var appsList = (ObservableCollection<GuardedApp>)typeof(MainWindow).GetField("_apps", flags)!.GetValue(window)!;
        var testApp = appsList.First();

        // 1. When app protection is active but EnableCursorShield is false, cursor shield must NOT be active
        testApp.IsProtected = true;
        cursorShieldCheckBox.IsChecked = false; Pump();
        typeof(MainWindow).GetMethod("UpdateCursorShieldForProtectionState", flags)!.Invoke(window, null);
        Check(!cursorShieldInstance.IsActive, "Cursor shield is NOT active when protection is active but EnableCursorShield is false");

        // 2. When both app protection and EnableCursorShield are true, cursor shield activates
        cursorShieldCheckBox.IsChecked = true; Pump();
        typeof(MainWindow).GetMethod("UpdateCursorShieldForProtectionState", flags)!.Invoke(window, null);
        Check(cursorShieldInstance.IsActive, "Cursor shield is active when both protection and EnableCursorShield are enabled");

        // 3. When EnableCursorShield is unticked while protection remains active, cursor shield immediately deactivates
        cursorShieldCheckBox.IsChecked = false; Pump();
        typeof(MainWindow).GetMethod("UpdateCursorShieldForProtectionState", flags)!.Invoke(window, null);
        Check(!cursorShieldInstance.IsActive, "Cursor shield deactivates immediately when toggle is unticked");

        // 4. Clean up testApp protection state
        testApp.IsProtected = false;
        typeof(MainWindow).GetMethod("UpdateCursorShieldForProtectionState", flags)!.Invoke(window, null);
        Check(!cursorShieldInstance.IsActive, "Cursor shield is deactivated when app protection is cleared");

        // 5. When both protection and EnableCursorShield are active, unticking the guarded app checkbox deactivates cursor shield
        testApp.IsProtected = true;
        cursorShieldCheckBox.IsChecked = true; Pump();
        typeof(MainWindow).GetMethod("UpdateCursorShieldForProtectionState", flags)!.Invoke(window, null);
        Check(cursorShieldInstance.IsActive, "Cursor shield is active before app untick");
        var appCb = new CheckBox { Tag = testApp, IsChecked = false };
        typeof(MainWindow).GetMethod("AppEnabledCheckBox_Click", flags)!.Invoke(window, new object[] { appCb, new RoutedEventArgs() });
        Check(!cursorShieldInstance.IsActive, "Cursor shield deactivates when app protection ends via AppEnabledCheckBox untick");
        testApp.IsEnabled = true;

        // 6. Verify Cursor Shield and Global Cursor Cloak coexistence (preventing double cursor in OBS)
        var cursorProtection = (CursorCaptureProtectionService)typeof(MainWindow).GetField("_cursorCaptureProtection", flags)!.GetValue(window)!;
        testApp.IsProtected = true;
        cursorShieldCheckBox.IsChecked = true; Pump();
        typeof(MainWindow).GetMethod("UpdateCursorShieldForProtectionState", flags)!.Invoke(window, null);
        Check(cursorShieldInstance.IsActive, "Cursor shield is active before global cloak is engaged");

        // Engage Global Cursor Cloak -> CursorShield must automatically deactivate to avoid double cursor
        cursorProtection.StartGlobalCursorCloak(); Pump();
        typeof(MainWindow).GetMethod("UpdateCursorShieldForProtectionState", flags)!.Invoke(window, null);
        Check(!cursorShieldInstance.IsActive, "Cursor shield deactivates when Global Cursor Cloak is active (preventing double cursor in OBS)");

        // Disengage Global Cursor Cloak -> CursorShield must reactivate
        cursorProtection.StopGlobalCursorCloak(); Pump();
        typeof(MainWindow).GetMethod("UpdateCursorShieldForProtectionState", flags)!.Invoke(window, null);
        Check(cursorShieldInstance.IsActive, "Cursor shield reactivates when Global Cursor Cloak is stopped");

        // Clean up testApp protection state
        testApp.IsProtected = false;
        typeof(MainWindow).GetMethod("UpdateCursorShieldForProtectionState", flags)!.Invoke(window, null);
        Check(!cursorShieldInstance.IsActive, "Cursor shield is deactivated when test app protection ends");

        // Verify AI Traffic Shield UI controls and state
        var aiTrafficCheckBox = (CheckBox)window.FindName("EnableAiTrafficShieldCheckBox");
        var aiTrafficProxyText = (TextBlock)window.FindName("AiTrafficProxyStatusText");
        var aiTrafficCertText = (TextBlock)window.FindName("AiTrafficCertStatusText");
        var aiTrafficSnifferText = (TextBlock)window.FindName("AiTrafficSnifferStatusText");
        var btnPurgeProxy = (Button)window.FindName("BtnPurgeProxy");
        var btnScanAiTraffic = (Button)window.FindName("BtnScanAiTraffic");

        Check(aiTrafficCheckBox != null, "EnableAiTrafficShieldCheckBox exists");
        Check(aiTrafficProxyText != null, "AiTrafficProxyStatusText exists");
        Check(aiTrafficCertText != null, "AiTrafficCertStatusText exists");
        Check(aiTrafficSnifferText != null, "AiTrafficSnifferStatusText exists");
        Check(btnPurgeProxy != null, "BtnPurgeProxy exists");
        Check(btnScanAiTraffic != null, "BtnScanAiTraffic exists");

        Check(aiTrafficCheckBox!.IsChecked == true, "AI Traffic Shield is enabled by default");
        Check(!string.IsNullOrWhiteSpace(aiTrafficProxyText!.Text), "AI Traffic Proxy status text is populated");
        Check(!string.IsNullOrWhiteSpace(aiTrafficCertText!.Text), "AI Traffic Cert status text is populated");
        Check(!string.IsNullOrWhiteSpace(aiTrafficSnifferText!.Text), "AI Traffic Sniffer status text is populated");

        // Toggle off
        aiTrafficCheckBox.IsChecked = false; Pump();
        var cfgAfterAiOff = new ConfigService(configPath); cfgAfterAiOff.Load();
        Check(!cfgAfterAiOff.EnableAiTrafficShield, "Disabling AI Traffic Shield persists to config");

        // Toggle back on
        aiTrafficCheckBox.IsChecked = true; Pump();
        var cfgAfterAiOn = new ConfigService(configPath); cfgAfterAiOn.Load();
        Check(cfgAfterAiOn.EnableAiTrafficShield, "Enabling AI Traffic Shield persists to config");

        // Test manual Purge button click handler
        typeof(MainWindow).GetMethod("BtnPurgeProxy_Click", flags)!.Invoke(window, new object[] { btnPurgeProxy!, new RoutedEventArgs() });
        Pump();

        // Test manual Scan button click handler
        typeof(MainWindow).GetMethod("BtnScanAiTraffic_Click", flags)!.Invoke(window, new object[] { btnScanAiTraffic!, new RoutedEventArgs() });
        var scanTimer = System.Diagnostics.Stopwatch.StartNew();
        while (!btnScanAiTraffic!.IsEnabled && scanTimer.Elapsed < TimeSpan.FromSeconds(5))
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            System.Threading.Thread.Sleep(10);
        }
        Check(btnScanAiTraffic.IsEnabled, "BtnScanAiTraffic remains enabled after scan completion");

        // Verify Virtual Desktop Hotkey Shield Switch is exclusively in Settings view (Hotkeys & Reference) and removed from Threats view (Privacy & Security)
        var vdThreatsCb = (CheckBox?)window.FindName("BlockVirtualDesktopHotkeysCheckBox");
        var vdSettingsCb = (CheckBox?)window.FindName("BlockVirtualDesktopHotkeysSettingsCheckBox");
        var vdThreatsStatus = (TextBlock?)window.FindName("VirtualDesktopHotkeysStatusText");
        var vdSettingsStatus = (TextBlock?)window.FindName("VirtualDesktopHotkeysSettingsStatusText");
        Check(vdThreatsCb == null, "Virtual Desktop hotkeys switch is removed from Threats (Privacy & Security) view");
        Check(vdThreatsStatus == null, "Virtual Desktop hotkeys status text is removed from Threats view");
        Check(vdSettingsCb != null, "Virtual Desktop hotkeys switch control exists in Settings (Hotkeys & Reference) view");
        Check(vdSettingsCb!.IsChecked == false, "Virtual Desktop hotkeys switch is off by default");

        // Toggle ON in Settings view -> verify hotkeys service updates, status text updates, and config persists
        var desktopAvailText = (TextBlock)window.FindName("DesktopAvailText");
        var hotkeysService = (HotkeyService)typeof(MainWindow).GetField("_hotkeys", flags)!.GetValue(window)!;
        vdSettingsCb!.IsChecked = true; Pump();
        Check(hotkeysService.BlockVirtualDesktopHotkeys, "HotkeyService BlockVirtualDesktopHotkeys is enabled");
        var cfgAfterVdOn = new ConfigService(configPath); cfgAfterVdOn.Load();
        Check(cfgAfterVdOn.BlockVirtualDesktopHotkeys, "BlockVirtualDesktopHotkeys is saved to config");
        Check(vdSettingsStatus != null && vdSettingsStatus.Text.Length > 0, "Status text updated in Settings view");
        Check(desktopAvailText != null && (desktopAvailText.Text.Contains("Hotkeys locked") || desktopAvailText.Text.Contains("khóa phím tắt")), "DesktopAvailText reflects hotkeys locked state in header");

        // Toggle OFF in Settings view -> verify hotkeys service updates and config persists
        vdSettingsCb!.IsChecked = false; Pump();
        Check(!hotkeysService.BlockVirtualDesktopHotkeys, "HotkeyService BlockVirtualDesktopHotkeys is disabled");
        var cfgAfterVdOff = new ConfigService(configPath); cfgAfterVdOff.Load();
        Check(!cfgAfterVdOff.BlockVirtualDesktopHotkeys, "BlockVirtualDesktopHotkeys off is saved to config");
        Check(desktopAvailText != null && (desktopAvailText.Text.Contains("VD ready") || desktopAvailText.Text.Contains("sẵn sàng") || desktopAvailText.Text.Contains("ready")), "DesktopAvailText reflects normal ready state in header");

        // Verify all Privacy & Security and Live Captions option toggles use the modern Switch style instead of tick checkboxes
        var switchStyle = (Style)window.Resources["ToggleSwitchWithLabelStyle"];
        foreach (var switchName in new[]
        {
            "CaptureGuardCheckBox",
            "CaptureMultiPurposeCheckBox",
            "BlockRaiaHostCheckBox",
            "BlockPortalHostCheckBox",
            "BlockCameraAccessCheckBox",
            "BlockMicrophoneAccessCheckBox",
            "BlockSystemToolsCheckBox",
            "EnableCursorShieldCheckBox",
            "EnableAiTrafficShieldCheckBox",
            "BlockVirtualDesktopHotkeysSettingsCheckBox",
            "MuteAntigravityNotificationsCheckBox",
            "PassiveFocusCheckBox",
            "TranscriptFollowCheckBox",
            "CaptionSingleLineCheckBox",
            "CaptionClickThroughCheckBox",
            "CaptionCaptureCheckBox"
        })
        {
            var cb = (CheckBox)window.FindName(switchName);
            Check(ReferenceEquals(cb.Style, switchStyle), $"Toggle control {switchName} uses ToggleSwitchWithLabelStyle");
        }

        // Verify CaptionOverlayWindow capture protection and periodic reinforcement via ProtectSelfFromCapture
        var captionCaptureToggle = (CheckBox)window.FindName("CaptionCaptureCheckBox");
        captionCaptureToggle.IsChecked = true;
        typeof(MainWindow).GetMethod("EnsureCaptionOverlay", flags)!.Invoke(window, null);
        var overlayObj = (CaptionOverlayWindow)typeof(MainWindow).GetField("_captionOverlay", flags)!.GetValue(window)!;
        new System.Windows.Interop.WindowInteropHelper(overlayObj).EnsureHandle();
        typeof(MainWindow).GetMethod("ProtectSelfFromCapture", flags)!.Invoke(window, null);
        Check(overlayObj.CaptureResult != null && overlayObj.CaptureResult.Mode != CaptureAffinityMode.Unverified,
            "ProtectSelfFromCapture reinforces and verifies capture exclusion on CaptionOverlayWindow");
        overlayObj.Close();

        // Verify Running Explorer toolbar buttons ("Refresh" & "Add Selected") retain their icons when Content is dynamically bound
        var btnRefreshApps = (Button)window.FindName("BtnRefreshApps");
        var btnAddApp = (Button)window.FindName("BtnAddApp");
        Check(StreamGuard.Controls.Icon.GetData(btnRefreshApps) != null, "Refresh button has UiIconRefresh attached so its icon persists across Content updates");
        Check(StreamGuard.Controls.Icon.GetData(btnAddApp) != null, "Add Selected button has UiIconPlus attached so its icon persists across Content updates");

        // Verify custom app icon, ProMax tag in sidebar, and v1.9.4 version footer at the bottom
        var sidebarLogoImage = (System.Windows.Controls.Image)window.FindName("SidebarLogoImage");
        Check(sidebarLogoImage != null && sidebarLogoImage.Source != null, "Sidebar logo displays custom application icon");
        var versionTag = (TextBlock)window.FindName("VersionText");
        Check(versionTag != null && versionTag.Text == "ProMax", "Sidebar brand tag displays ProMax");
        var versionFooter = (TextBlock)window.FindName("AppVersionFooterText");
        Check(versionFooter != null && versionFooter.Text == "v1.9.4", "Bottom footer displays current application version code v1.9.4");

        // Verify Snipping Tool hotkey controls and default bindings
        var txtSnip = (TextBox)window.FindName("TxtSnippingHotkey");
        var txtSnipEdit = (TextBox)window.FindName("TxtSnippingHotkeyEdit");
        var txtSnipSettings = (TextBlock)window.FindName("TxtSnippingHotkeySettings");
        Check(txtSnip != null && txtSnip.Text == "Ctrl+Alt+S", "Privacy tab snipping hotkey text defaults to Ctrl+Alt+S");
        Check(txtSnipEdit != null && txtSnipEdit.Text == "Ctrl+Alt+S", "Settings tab snipping hotkey edit text defaults to Ctrl+Alt+S");
        Check(txtSnipSettings != null && txtSnipSettings.Text == "Ctrl + Alt + S", "Cheat sheet displays formatted Ctrl + Alt + S");

        // Verify dynamic rebind and persistence
        var commitSnipMethod = typeof(MainWindow).GetMethod("CommitSnippingHotkey", flags)!;
        commitSnipMethod.Invoke(window, new object[] { "Ctrl+Alt+D" }); Pump();
        Check(txtSnip!.Text == "Ctrl+Alt+D", "Privacy tab snipping hotkey updates after rebind");
        Check(txtSnipEdit!.Text == "Ctrl+Alt+D", "Settings tab snipping hotkey updates after rebind");
        Check(txtSnipSettings!.Text == "Ctrl + Alt + D", "Cheat sheet updates formatted snipping hotkey");
        var cfgAfterSnipRebind = new ConfigService(configPath); cfgAfterSnipRebind.Load();
        Check(cfgAfterSnipRebind.SnippingHotkey == "Ctrl+Alt+D", "Rebound snipping hotkey persists to config");

        var btnStartSnipping = (Button)window.FindName("BtnStartSnipping");
        var btnQuickSnip = (Button)window.FindName("BtnQuickSnip");
        Check(btnStartSnipping.Content.ToString() == "Take Screenshot (Ctrl+Alt+D)", $"Start button text in English after rebind: {btnStartSnipping.Content}");
        Check(btnQuickSnip.ToolTip.ToString() == "Protected Screen Snip (Ctrl+Alt+D)", $"Quick snip tooltip in English after rebind: {btnQuickSnip.ToolTip}");

        L.SetLanguage("vi");
        typeof(MainWindow).GetMethod("SyncSnippingHotkeyUi", flags)!.Invoke(window, null);
        Check(btnStartSnipping.Content.ToString() == "Chụp màn hình (Ctrl+Alt+D)", $"Start button text in Vietnamese after rebind: {btnStartSnipping.Content}");
        Check(btnQuickSnip.ToolTip.ToString() == "Chụp màn hình bảo vệ (Ctrl+Alt+D)", $"Quick snip tooltip in Vietnamese after rebind: {btnQuickSnip.ToolTip}");

        L.SetLanguage("en");
        typeof(MainWindow).GetMethod("SyncSnippingHotkeyUi", flags)!.Invoke(window, null);

        // Restore default
        commitSnipMethod.Invoke(window, new object[] { "Ctrl+Alt+S" }); Pump();
        Check(txtSnip!.Text == "Ctrl+Alt+S", "Restored default snipping hotkey");
        Check(btnStartSnipping.Content.ToString() == "Take Screenshot (Ctrl+Alt+S)", "Start button text restored to default in English");

        // Verify Quick Clear Vocabulary button and Shift+S+G chord toggle flow
        var txtKeywords = (TextBox)window.FindName("CaptionCustomKeywordsTextBox");
        var btnClearKeywords = (Button)window.FindName("BtnCaptionClearKeywords");
        Check(txtKeywords != null && btnClearKeywords != null, "CaptionCustomKeywordsTextBox and BtnCaptionClearKeywords exist");
        txtKeywords!.Text = "TensorFlow, PyTorch"; Pump();
        Check(btnClearKeywords!.IsEnabled == true, "BtnCaptionClearKeywords enabled when keywords text box has content");
        var clearClickMethod = typeof(MainWindow).GetMethod("BtnCaptionClearKeywords_Click", flags)!;
        clearClickMethod.Invoke(window, new object[] { btnClearKeywords, new RoutedEventArgs() }); Pump();
        Check(txtKeywords.Text == string.Empty, "Quick clear button clears custom keywords text");
        Check(btnClearKeywords.IsEnabled == false, "BtnCaptionClearKeywords disabled when keywords text box is empty");

        // Verify Shift+S+G toggle: 1st press opens/restores, 2nd press minimizes and hides
        // 1. Initial state (hidden/background) -> 1st press opens window
        window.ToggleDriverBoosterFromChord(); Pump();
        Check(window.IsVisible && window.WindowState == WindowState.Normal, "Shift+S+G 1st press opens and restores Driver Booster from background");
        // 2. Open state -> 2nd press hides window without setting WindowState=Minimized in PassiveFocusMode (avoids SW_RESTORE activation in Fullscreen)
        window.ToggleDriverBoosterFromChord(); Pump();
        Check(!window.IsVisible && window.WindowState == WindowState.Normal, "Shift+S+G 2nd press automatically hides Driver Booster without Minimized state in PassiveFocusMode");
        // 3. Minimized/hidden state -> subsequent press opens window again
        window.ToggleDriverBoosterFromChord(); Pump();
        Check(window.IsVisible && window.WindowState == WindowState.Normal, "Shift+S+G subsequent press opens and restores Driver Booster again");

        // Verify Danger Zone & Uninstallation Confirmation Modal (Confirm / Cancel)
        var dangerModal = (Grid)window.FindName("DangerConfirmModalOverlay");
        var dangerTitle = (TextBlock)window.FindName("DangerConfirmTitleText");
        var dangerMessage = (TextBlock)window.FindName("DangerConfirmMessageText");
        var btnDangerCancel = (Button)window.FindName("BtnDangerConfirmCancel");
        var btnDangerAccept = (Button)window.FindName("BtnDangerConfirmAccept");
        var btnReset = (Button)window.FindName("BtnResetAppData");
        var btnUninstall = (Button)window.FindName("BtnCompleteUninstall");
        Check(dangerModal != null && dangerTitle != null && dangerMessage != null && btnDangerCancel != null && btnDangerAccept != null,
            "Danger Zone confirmation modal controls exist in MainWindow");
        Check(dangerModal!.Visibility == Visibility.Collapsed, "Danger Zone confirmation modal starts collapsed");

        // Clicking Factory Reset opens confirmation modal without executing immediately
        var resetClick = typeof(MainWindow).GetMethod("BtnResetAppData_Click", flags)!;
        resetClick.Invoke(window, new object[] { btnReset!, new RoutedEventArgs() }); Pump();
        Check(dangerModal.Visibility == Visibility.Visible, "Clicking Factory Reset opens confirmation modal");
        Check(dangerTitle!.Text == LocalizationService.GetString("Settings.ConfirmResetTitle", "Confirm Factory Reset"),
            "Factory Reset modal displays Confirm Factory Reset title");
        Check(dangerMessage!.Text == LocalizationService.GetString("Settings.ConfirmResetMessage", ""),
            "Factory Reset modal displays warning message");

        // Clicking Cancel closes modal safely
        var cancelClick = typeof(MainWindow).GetMethod("BtnDangerConfirmCancel_Click", flags)!;
        cancelClick.Invoke(window, new object[] { btnDangerCancel!, new RoutedEventArgs() }); Pump();
        Check(dangerModal.Visibility == Visibility.Collapsed, "Clicking Cancel closes Danger Zone confirmation modal");

        // Clicking Complete Uninstall opens confirmation modal without executing immediately
        var uninstallClick = typeof(MainWindow).GetMethod("BtnCompleteUninstall_Click", flags)!;
        uninstallClick.Invoke(window, new object[] { btnUninstall!, new RoutedEventArgs() }); Pump();
        Check(dangerModal.Visibility == Visibility.Visible, "Clicking Complete Uninstall opens confirmation modal");
        Check(dangerTitle.Text == LocalizationService.GetString("Settings.ConfirmUninstallTitle", "Confirm Complete Uninstall"),
            "Complete Uninstall modal displays Confirm Complete Uninstall title");
        window.CloseDangerConfirmModal(); Pump();
        Check(dangerModal.Visibility == Visibility.Collapsed, "CloseDangerConfirmModal closes modal safely");

        // Confirm button executes pending action once and closes modal
        int confirmCalls = 0;
        window.ShowDangerConfirmModal("Test Title", "Test Message", "Confirm Test", () => confirmCalls++); Pump();
        Check(dangerModal.Visibility == Visibility.Visible && btnDangerAccept!.Content.ToString() == "Confirm Test",
            "ShowDangerConfirmModal sets custom confirm label and displays modal");
        var acceptClick = typeof(MainWindow).GetMethod("BtnDangerConfirmAccept_Click", flags)!;
        acceptClick.Invoke(window, new object[] { btnDangerAccept!, new RoutedEventArgs() }); Pump();
        Check(dangerModal.Visibility == Visibility.Collapsed && confirmCalls == 1,
            "Clicking Confirm executes pending action once and closes modal");

        // Hiding window via Shift+S+G clears any open confirmation modal
        int staleCalls = 0;
        window.ShowDangerConfirmModal("Stale", "Stale", "Confirm", () => staleCalls++); Pump();
        window.ToggleDriverBoosterFromChord(); Pump();
        Check(dangerModal.Visibility == Visibility.Collapsed && staleCalls == 0,
            "HideToTray automatically dismisses open Danger Zone confirmation modal");
    }

    private sealed class TestCurtain : IPrivacyCurtain
    {
        public bool IsVisible { get; private set; }
        public bool Complete = true;
        public bool CoversAllTargetScreens => IsVisible && Complete;
        public void Show() => IsVisible = true;
        public void Hide() => IsVisible = false;
    }

    private sealed class BindingErrors : TraceListener
    {
        public List<string> Messages { get; } = new();
        public override void Write(string? message) { if (message != null) Messages.Add(message); }
        public override void WriteLine(string? message) { if (message != null) Messages.Add(message); }
    }

}
