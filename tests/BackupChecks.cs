using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StreamGuard.Localization;
using StreamGuard.Models;
using StreamGuard.Services;

namespace StreamGuard.Tests;

internal static class BackupChecks
{
    internal static void Run(Action<bool, string> check, string temporaryRoot)
    {
        var directory = Path.Combine(temporaryRoot, "backup-audit");
        Directory.CreateDirectory(directory);

        // 1. Basic Bundle Creation & Defaults
        var bundle = BackupService.CreateBundle(null, null);
        check(bundle != null, "BackupService creates non-null bundle from null inputs");
        check(bundle!.App == "Driver Booster", "Default bundle app identifier is Driver Booster");
        check(bundle.Config != null && bundle.Captions != null, "Default bundle includes initialized Config and Captions");
        check(!bundle.Config!.AutoStartShield, "Default bundle config AutoStartShield is false");

        // 2. Full Roundtrip Serialization with Rich Data
        var testApps = new List<GuardedAppConfig>
        {
            new() { Name = "Trading Platform", ProcessName = "trading", ExecutablePath = "C:\\Apps\\trading.exe", IsEnabled = true, SecureHotkey = "Ctrl+Alt+1" },
            new() { Name = "Secret Notes", ProcessName = "notepad", ExecutablePath = "C:\\Windows\\notepad.exe", IsEnabled = false, SecureHotkey = "Ctrl+Alt+2" }
        };

        var config = new StreamGuardConfig
        {
            Apps = testApps,
            BlockRaiaWebsite = true,
            BlockPortalWebsite = true,
            BlockCameraAccess = false,
            BlockMicrophoneAccess = true,
            PassiveFocusMode = true,
            Language = "vi",
            CaptureGuardEnabled = true,
            CaptureGuardIncludeMultiPurpose = true,
            BlockSystemTools = true,
            MuteAntigravityNotifications = true,
            ScreenshotDirectory = "C:\\Screenshots",
            ScreenshotAutoCopyClipboard = true,
            ScreenshotExcludeFromCapture = true,
            InAppCursorCloakEnabled = true,
            GlobalCursorCloakEnabled = true,
            EnableCursorShield = true,
            EnableAiTrafficShield = true,
            SnippingHotkey = "Ctrl+Shift+S",
            BlockVirtualDesktopHotkeys = true,
            AutoStartShield = true
        };

        var captions = new CaptionSettings
        {
            FontSize = 24,
            SingleLine = true,
            ExcludeFromCapture = true,
            EnableTranslation = true,
            TranslationMode = "VietnameseOnly",
            CustomKeywords = "Antigravity, Driver Booster, StreamGuard, Security, Encryption"
        };

        var populatedBundle = BackupService.CreateBundle(config, captions);
        var json = BackupService.SerializeBundle(populatedBundle);
        check(!string.IsNullOrWhiteSpace(json), "Serialized bundle JSON is non-empty");

        check(BackupService.TryParseBundle(json, out var parsedBundle, out var parseError), $"TryParseBundle parses valid bundle JSON: {parseError}");
        check(parsedBundle != null, "Parsed bundle is non-null");
        check(parsedBundle!.Config.AutoStartShield, "AutoStartShield preserved in bundle roundtrip");
        check(parsedBundle.Config.InAppCursorCloakEnabled, "InAppCursorCloakEnabled preserved in bundle roundtrip");
        check(parsedBundle.Config.GlobalCursorCloakEnabled, "GlobalCursorCloakEnabled preserved in bundle roundtrip");
        check(parsedBundle.Config.Apps.Count == 2, "Apps count matches in roundtrip");
        check(parsedBundle.Config.Apps[0].Name == "Trading Platform" && parsedBundle.Config.Apps[0].SecureHotkey == "Ctrl+Alt+1", "App properties preserved in roundtrip");
        check(parsedBundle.Config.BlockRaiaWebsite && parsedBundle.Config.BlockPortalWebsite, "Block websites preserved in roundtrip");
        check(!parsedBundle.Config.BlockCameraAccess && parsedBundle.Config.BlockMicrophoneAccess, "Camera/Mic settings preserved in roundtrip");
        check(parsedBundle.Config.Language == "vi", "Language preserved in roundtrip");
        check(parsedBundle.Config.BlockVirtualDesktopHotkeys, "Virtual desktop hotkey shield preserved in roundtrip");
        check(parsedBundle.Captions.FontSize == 24, "Caption font size preserved in roundtrip");
        check(parsedBundle.Captions.TranslationMode == "VietnameseOnly", "Caption translation mode preserved in roundtrip");
        check(parsedBundle.Captions.CustomKeywords.Contains("Security"), "Caption custom keywords preserved in roundtrip");

        // 3. File Export & Import
        var filePath = Path.Combine(directory, "StreamGuard_Backup_Test.json");
        check(BackupService.TryExportToFile(filePath, populatedBundle, out var exportError), $"TryExportToFile succeeds: {exportError}");
        check(File.Exists(filePath), "Export file physically exists on disk");

        check(BackupService.TryImportFromFile(filePath, out var importedBundle, out var importError), $"TryImportFromFile succeeds: {importError}");
        check(importedBundle != null, "Imported bundle is non-null");
        check(importedBundle!.Config.AutoStartShield == true, "Imported bundle contains AutoStartShield = true");
        check(importedBundle.Config.Apps.Count == 2, "Imported bundle contains all 2 apps");

        // Overwrite existing file atomically
        check(BackupService.TryExportToFile(filePath, populatedBundle, out _), "Export file overwrites cleanly");
        check(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "No temporary file leaks after file export");

        // 4. Schema Compatibility: Direct StreamGuardConfig format
        var directConfigJson = JsonConvert.SerializeObject(new StreamGuardConfig
        {
            Language = "vi",
            AutoStartShield = true,
            BlockCameraAccess = false,
            Apps = new List<GuardedAppConfig>
            {
                new() { Name = "Direct Config App", ProcessName = "directapp", IsEnabled = true }
            }
        });
        check(BackupService.TryParseBundle(directConfigJson, out var directParsed, out var directError), $"TryParseBundle parses raw StreamGuardConfig: {directError}");
        check(directParsed != null && directParsed.Config.AutoStartShield && directParsed.Config.Apps.Count == 1, "Raw StreamGuardConfig parsed successfully");
        check(directParsed!.Captions != null, "Default captions synthesized for raw StreamGuardConfig");

        // 5. Schema Compatibility: Legacy apps array format
        var arrayJson = "[{\"Name\":\"ArrayApp\",\"ProcessName\":\"arrayproc\",\"SecureHotkey\":\"Ctrl+Alt+5\"}]";
        check(BackupService.TryParseBundle(arrayJson, out var arrayParsed, out var arrayError), $"TryParseBundle parses apps array: {arrayError}");
        check(arrayParsed != null && arrayParsed.Config.Apps.Count == 1 && arrayParsed.Config.Apps[0].Name == "ArrayApp", "Legacy apps array parsed successfully");

        // 6. Error Handling & Security Rejections
        check(!BackupService.TryParseBundle("", out _, out var emptyErr) && !string.IsNullOrEmpty(emptyErr), "Rejects empty JSON");
        check(!BackupService.TryParseBundle("{ invalid json", out _, out var corruptErr) && !string.IsNullOrEmpty(corruptErr), "Rejects corrupt JSON");
        check(!BackupService.TryParseBundle("{\"Unrelated\":123}", out _, out var schemaErr) && !string.IsNullOrEmpty(schemaErr), "Rejects unrecognized JSON schema");
        check(!BackupService.TryImportFromFile(Path.Combine(directory, "nonexistent.json"), out _, out _), "Rejects non-existent file path");

        // 7. Security: Rejection of Protected System Identities, Deduplication, and .exe Normalization
        var dangerousApps = new List<GuardedAppConfig>
        {
            new() { Name = "Safe App", ProcessName = "safeapp" },
            new() { Name = "Safe App Duplicate", ProcessName = "safeapp" },
            new() { Name = "Notepad Executable", ProcessName = "notepad.exe" },
            new() { Name = "Malicious Trojan", ProcessName = "svchost.exe" },
            new() { Name = "Malicious Hijack", ProcessName = "csrss" },
            new() { Name = "Task Manager Hook", ProcessName = "taskmgr" }
        };
        var sanitized = BackupService.SanitizeApps(dangerousApps);
        check(sanitized.Count == 2, "SanitizeApps strips protected system processes and deduplicates by process name");
        check(sanitized[0].ProcessName == "safeapp", "First safe app preserved");
        check(sanitized[1].ProcessName == "notepad", "Process name ending in .exe is normalized to base name without .exe");

        // 8. Security: Reject oversized payloads (> 10MB)
        var hugeBuilder = new StringBuilder();
        hugeBuilder.Append("{\"Config\":{\"Apps\":[],\"ScreenshotDirectory\":\"");
        hugeBuilder.Append(new string('X', BackupService.MaximumBackupBytes + 1024));
        hugeBuilder.Append("\"}}");
        var hugeJson = hugeBuilder.ToString();
        check(!BackupService.TryParseBundle(hugeJson, out _, out var sizeErr) && sizeErr!.Contains("10 MB"), "Rejects oversized JSON payload > 10MB");

        // 9. Case-insensitive parsing and legacy BlockKnownWebsites fallback
        var camelCaseJson = "{\"config\":{\"apps\":[{\"processName\":\"camelapp\"}],\"blockKnownWebsites\":true,\"language\":\"vi\"}}";
        check(BackupService.TryParseBundle(camelCaseJson, out var camelBundle, out _), "TryParseBundle handles case-insensitive JSON schema");
        check(camelBundle != null && camelBundle.Config.BlockRaiaWebsite, "BlockKnownWebsites falls back to BlockRaiaWebsite when BlockRaiaWebsite key is absent");
        check(camelBundle!.Config.Apps.Count == 1 && camelBundle.Config.Apps[0].ProcessName == "camelapp", "Case-insensitive apps array parsed");
    }

    internal static void RunUiChecks(MainWindow window, Action<bool, string> check)
    {
        var testBundle = new BackupBundle
        {
            App = "Driver Booster",
            Version = "1.0",
            ExportedAt = DateTime.UtcNow,
            Config = new StreamGuardConfig
            {
                AutoStartShield = true,
                BlockCameraAccess = false,
                BlockMicrophoneAccess = true,
                Language = "vi",
                Apps = new List<GuardedAppConfig>
                {
                    new() { Name = "Restored App 1", ProcessName = "restored1", IsEnabled = true }
                }
            },
            Captions = new CaptionSettings
            {
                FontSize = 26,
                SingleLine = true,
                ExcludeFromCapture = false,
                EnableTranslation = false,
                TranslationMode = "VietnameseOnly",
                CustomKeywords = "RestoredVocabularyTerm"
            }
        };

        var method = typeof(MainWindow).GetMethod("ApplyRestoredBundle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        check(method != null, "ApplyRestoredBundle method exists on MainWindow");
        method!.Invoke(window, new object[] { testBundle });
        check(LocalizationService.Current.Language == "vi", "ApplyRestoredBundle applies Vietnamese language preference");

        // Restore language to English for subsequent UI tests
        testBundle.Config.Language = "en";
        method!.Invoke(window, new object[] { testBundle });
        check(LocalizationService.Current.Language == "en", "ApplyRestoredBundle applies English language preference");

        var autoStartSettings = (System.Windows.Controls.CheckBox)window.FindName("AutoStartShieldCheckBox");
        var autoStartQuick = (System.Windows.Controls.CheckBox)window.FindName("AutoStartShieldQuickCheckBox");
        var blockCamera = (System.Windows.Controls.CheckBox)window.FindName("BlockCameraAccessCheckBox");
        var captionSingleLine = (System.Windows.Controls.CheckBox)window.FindName("CaptionSingleLineCheckBox");
        var captionCapture = (System.Windows.Controls.CheckBox)window.FindName("CaptionCaptureCheckBox");
        var captionTranslation = (System.Windows.Controls.CheckBox)window.FindName("CaptionTranslationCheckBox");
        var captionKeywords = (System.Windows.Controls.TextBox)window.FindName("CaptionCustomKeywordsTextBox");

        check(autoStartSettings.IsChecked == true && autoStartQuick.IsChecked == true, "ApplyRestoredBundle updates AutoStartShield switches");
        check(blockCamera.IsChecked == false, "ApplyRestoredBundle updates BlockCameraAccess checkbox");
        check(captionSingleLine.IsChecked == true, "ApplyRestoredBundle updates CaptionSingleLineCheckBox");
        check(captionCapture.IsChecked == false, "ApplyRestoredBundle updates CaptionCaptureCheckBox");
        check(captionTranslation.IsChecked == false, "ApplyRestoredBundle updates CaptionTranslationCheckBox without UI overwriting");
        check(captionKeywords.Text == "RestoredVocabularyTerm", "ApplyRestoredBundle updates CaptionCustomKeywordsTextBox");

        var toastMethod = typeof(MainWindow).GetMethod("ShowSettingsToast", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        check(toastMethod != null, "ShowSettingsToast method exists on MainWindow");
        toastMethod!.Invoke(window, new object[] { "Test Notification", true });
        var toastBorder = (System.Windows.Controls.Border)window.FindName("SettingsToast");
        check(toastBorder != null && toastBorder.Visibility == System.Windows.Visibility.Visible, "Settings toast becomes visible when triggered");

        // 10. Reserved Hotkeys Verification (ensuring no collision with Captions Translation Ctrl+Alt+T or Chords)
        var isReservedMethod = typeof(MainWindow).GetMethod("IsReservedSystemHotkey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        check(isReservedMethod != null, "IsReservedSystemHotkey method exists");
        check((bool)isReservedMethod!.Invoke(null, new object[] { "Ctrl+Alt+T" })!, "Ctrl+Alt+T is reserved for caption translation");
        check((bool)isReservedMethod!.Invoke(null, new object[] { "Shift+S+G" })!, "Shift+S+G is reserved for window toggle chord");
        check((bool)isReservedMethod!.Invoke(null, new object[] { "Ctrl+Shift+S+G" })!, "Ctrl+Shift+S+G is reserved for emergency kill chord");
        check((bool)isReservedMethod!.Invoke(null, new object[] { "Ctrl+Shift+G" })!, "Ctrl+Shift+G is reserved for visibility toggle");
        check(!(bool)isReservedMethod!.Invoke(null, new object[] { "Ctrl+Alt+1" })!, "Ctrl+Alt+1 is available for apps");

        // 11. Verify ApplyRestoredBundle does not force-protect when wasProtecting was false
        var isProtectingField = typeof(MainWindow).GetField("_isProtecting", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var stopMethod = typeof(MainWindow).GetMethod("StopProtecting", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        check(isProtectingField != null && stopMethod != null, "Protection control reflection fields exist");
        stopMethod!.Invoke(window, null);
        check(!(bool)isProtectingField!.GetValue(window)!, "Shield stopped and idle before testing inactive restore");

        method!.Invoke(window, new object[] { testBundle });
        check(!(bool)isProtectingField.GetValue(window)!, "ApplyRestoredBundle does not prematurely engage shield when wasProtecting is false");
    }
}
