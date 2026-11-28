using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StreamGuard.Models;
using System;
using System.Collections.Generic;
using System.IO;

namespace StreamGuard.Services
{
    public class ConfigService
    {
        private const int MaximumConfigBytes = 4 * 1024 * 1024;
        private readonly string ConfigPath;
        internal string RecoveryJournalPath => Path.ChangeExtension(ConfigPath, ".recovery.json");
        internal string CaptionSettingsPath => Path.ChangeExtension(ConfigPath, ".captions.json");
        internal string CaptionTranscriptDirectory => Path.Combine(Path.GetDirectoryName(ConfigPath)!, "Captions", "transcripts");
        private readonly object _gate = new();
        public string? LastLoadError { get; private set; }
        public bool LoadedFromBackup { get; private set; }

        public ConfigService(string? configPath = null)
        {
            ConfigPath = Path.GetFullPath(configPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "StreamGuard", "config.json"));
        }

        public bool BlockKnownWebsites { get; private set; } = false;
        public bool BlockRaiaWebsite { get; private set; } = false;
        public bool BlockPortalWebsite { get; private set; } = false;
        public bool BlockCameraAccess { get; private set; } = true;
        public bool BlockMicrophoneAccess { get; private set; } = true;
        public bool PassiveFocusMode { get; private set; } = true;
        public string Language { get; private set; } = "en";
        public string ThemeMode { get; private set; } = ThemeService.DarkMode;
        public bool CaptureGuardEnabled { get; private set; } = true;
        public bool CaptureGuardIncludeMultiPurpose { get; private set; }
        public bool AllowSelfScreenCapture { get; private set; } = false;
        public bool InAppCursorCloakEnabled { get; private set; } = true;
        public bool GlobalCursorCloakEnabled { get; private set; } = false;
        public bool BlockSystemTools { get; private set; } = false;
        public bool MuteAntigravityNotifications { get; private set; } = false;
        public string ScreenshotDirectory { get; private set; } = "";
        public bool ScreenshotAutoCopyClipboard { get; private set; } = true;
        public bool ScreenshotExcludeFromCapture { get; private set; } = true;
        public bool ScreenshotOpenEditorAfterCapture { get; private set; } = true;
        public bool EnableCursorShield { get; private set; } = false;
        public bool EnableAiTrafficShield { get; private set; } = true;
        public string SnippingHotkey { get; private set; } = "Ctrl+Alt+S";
        public bool BlockVirtualDesktopHotkeys { get; private set; } = false;
        public bool AutoStartShield { get; private set; } = false;
        public bool EnableSystemTrayIcon { get; private set; } = false;

        public List<GuardedAppConfig> Load()
        {
            lock (_gate)
            {
                Apply(new StreamGuardConfig());
                LastLoadError = null;
                LoadedFromBackup = false;
                foreach (var path in new[] { ConfigPath, ConfigPath + ".bak" })
                {
                    if (!File.Exists(path)) continue;
                    try
                    {
                        var config = Parse(ReadConfiguration(path));
                        var apps = SafeApps(config.Apps);
                        Apply(config);
                        LoadedFromBackup = path != ConfigPath;
                        return apps;
                    }
                    catch (Exception error) { LastLoadError ??= error.Message; }
                }
                return new();
            }
        }

        private static string ReadConfiguration(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumConfigBytes) throw new JsonException("Configuration is too large.");
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        private static StreamGuardConfig Parse(string json)
        {
            var token = JToken.Parse(json);
            if (token is JArray) return new StreamGuardConfig { Apps = token.ToObject<List<GuardedAppConfig>>() ?? new() };
            if (token is not JObject obj) throw new JsonException("Configuration must be an object or an application array.");
            var config = obj.ToObject<StreamGuardConfig>() ?? new();
            // The aggregate flag is a migration fallback, never an override of an explicit per-site choice.
            if (!obj.ContainsKey(nameof(StreamGuardConfig.BlockRaiaWebsite))) config.BlockRaiaWebsite = config.BlockKnownWebsites;
            config.BlockKnownWebsites = config.BlockRaiaWebsite || config.BlockPortalWebsite;
            return config;
        }

        private void Apply(StreamGuardConfig config)
        {
            BlockKnownWebsites = config.BlockRaiaWebsite || config.BlockPortalWebsite;
            BlockRaiaWebsite = config.BlockRaiaWebsite;
            BlockPortalWebsite = config.BlockPortalWebsite;
            BlockCameraAccess = config.BlockCameraAccess;
            BlockMicrophoneAccess = config.BlockMicrophoneAccess;
            PassiveFocusMode = config.PassiveFocusMode;
            Language = Localization.LocalizationService.NormalizeLanguage(config.Language);
            ThemeMode = ThemeService.NormalizeTheme(config.ThemeMode);
            CaptureGuardEnabled = config.CaptureGuardEnabled;
            CaptureGuardIncludeMultiPurpose = config.CaptureGuardIncludeMultiPurpose;
            AllowSelfScreenCapture = config.AllowSelfScreenCapture;
            InAppCursorCloakEnabled = config.InAppCursorCloakEnabled;
            GlobalCursorCloakEnabled = config.GlobalCursorCloakEnabled;
            BlockSystemTools = config.BlockSystemTools;
            MuteAntigravityNotifications = config.MuteAntigravityNotifications;
            ScreenshotDirectory = config.ScreenshotDirectory ?? "";
            ScreenshotAutoCopyClipboard = config.ScreenshotAutoCopyClipboard;
            ScreenshotExcludeFromCapture = config.ScreenshotExcludeFromCapture;
            ScreenshotOpenEditorAfterCapture = config.ScreenshotOpenEditorAfterCapture;
            EnableCursorShield = config.EnableCursorShield;
            EnableAiTrafficShield = config.EnableAiTrafficShield;
            SnippingHotkey = string.IsNullOrWhiteSpace(config.SnippingHotkey) ? "Ctrl+Alt+S" : config.SnippingHotkey;
            BlockVirtualDesktopHotkeys = config.BlockVirtualDesktopHotkeys;
            AutoStartShield = config.AutoStartShield;
            EnableSystemTrayIcon = config.EnableSystemTrayIcon;
        }

        private static List<GuardedAppConfig> SafeApps(IEnumerable<GuardedAppConfig>? apps) =>
            (apps ?? Array.Empty<GuardedAppConfig>())
                .Where(app => app != null && !ProcessSafetyService.IsProtectedIdentity(app.ProcessName, app.ExecutablePath))
                .Select(app => new GuardedAppConfig
                {
                    Name = string.IsNullOrWhiteSpace(app.Name) ? app.ProcessName : app.Name,
                    ProcessName = app.ProcessName.Trim(), ExecutablePath = app.ExecutablePath ?? "",
                    SecureHotkey = app.SecureHotkey ?? "", IsEnabled = app.IsEnabled, IsHotkeyDisabled = app.IsHotkeyDisabled
                }).ToList();

        public bool Save(
            IEnumerable<GuardedApp> apps,
            bool blockKnownWebsites = false,
            bool blockRaiaWebsite = false,
            bool blockPortalWebsite = false,
            bool blockCameraAccess = true,
            bool blockMicrophoneAccess = true,
            bool passiveFocusMode = true,
            string? language = null,
            bool? captureGuardEnabled = null,
            bool? captureGuardIncludeMultiPurpose = null,
            bool? inAppCursorCloakEnabled = null,
            bool? globalCursorCloakEnabled = null,
            bool? blockSystemTools = null,
            bool? muteAntigravityNotifications = null,
            string? screenshotDirectory = null,
            bool? screenshotAutoCopyClipboard = null,
            bool? screenshotExcludeFromCapture = null,
            bool? screenshotOpenEditorAfterCapture = null,
            bool? enableCursorShield = null,
            string? snippingHotkey = null,
            bool? enableAiTrafficShield = null,
            bool? blockVirtualDesktopHotkeys = null,
            bool? autoStartShield = null,
            bool? allowSelfScreenCapture = null,
            string? themeMode = null,
            bool? enableSystemTrayIcon = null)
        {
            lock (_gate)
            {
                string? temporaryPath = null;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                    var list = new List<GuardedAppConfig>();
                    foreach (var a in apps.Where(app => !ProcessSafetyService.IsProtectedIdentity(app.ProcessName, app.ExecutablePath)))
                        list.Add(new GuardedAppConfig
                        {
                            Name = a.Name,
                            ProcessName = a.ProcessName,
                            ExecutablePath = a.ExecutablePath,
                            IsEnabled = a.IsEnabled,
                            SecureHotkey = a.SecureHotkey,
                            IsHotkeyDisabled = a.IsHotkeyDisabled
                        });
                    var effectiveRaia = blockRaiaWebsite || (blockKnownWebsites && !blockRaiaWebsite && !blockPortalWebsite);
                    var config = new StreamGuardConfig
                    {
                        Apps = list,
                        BlockKnownWebsites = effectiveRaia || blockPortalWebsite,
                        BlockRaiaWebsite = effectiveRaia,
                        BlockPortalWebsite = blockPortalWebsite,
                        BlockCameraAccess = blockCameraAccess,
                        BlockMicrophoneAccess = blockMicrophoneAccess,
                        PassiveFocusMode = passiveFocusMode,
                        Language = Localization.LocalizationService.NormalizeLanguage(language ?? Language),
                        ThemeMode = ThemeService.NormalizeTheme(themeMode ?? ThemeMode),
                        CaptureGuardEnabled = captureGuardEnabled ?? CaptureGuardEnabled,
                        CaptureGuardIncludeMultiPurpose = captureGuardIncludeMultiPurpose ?? CaptureGuardIncludeMultiPurpose,
                        InAppCursorCloakEnabled = inAppCursorCloakEnabled ?? InAppCursorCloakEnabled,
                        GlobalCursorCloakEnabled = globalCursorCloakEnabled ?? GlobalCursorCloakEnabled,
                        BlockSystemTools = blockSystemTools ?? BlockSystemTools,
                        MuteAntigravityNotifications = muteAntigravityNotifications ?? MuteAntigravityNotifications,
                        ScreenshotDirectory = screenshotDirectory ?? ScreenshotDirectory,
                        ScreenshotAutoCopyClipboard = screenshotAutoCopyClipboard ?? ScreenshotAutoCopyClipboard,
                        ScreenshotExcludeFromCapture = screenshotExcludeFromCapture ?? ScreenshotExcludeFromCapture,
                        ScreenshotOpenEditorAfterCapture = screenshotOpenEditorAfterCapture ?? ScreenshotOpenEditorAfterCapture,
                        EnableCursorShield = enableCursorShield ?? EnableCursorShield,
                        EnableAiTrafficShield = enableAiTrafficShield ?? EnableAiTrafficShield,
                        SnippingHotkey = string.IsNullOrWhiteSpace(snippingHotkey) ? (string.IsNullOrWhiteSpace(SnippingHotkey) ? "Ctrl+Alt+S" : SnippingHotkey) : snippingHotkey,
                        BlockVirtualDesktopHotkeys = blockVirtualDesktopHotkeys ?? BlockVirtualDesktopHotkeys,
                        AutoStartShield = autoStartShield ?? AutoStartShield,
                        AllowSelfScreenCapture = allowSelfScreenCapture ?? AllowSelfScreenCapture,
                        EnableSystemTrayIcon = enableSystemTrayIcon ?? EnableSystemTrayIcon
                    };
                    var json = JsonConvert.SerializeObject(config, Formatting.Indented);
                    if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumConfigBytes) return false;
                    temporaryPath = ConfigPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), leaveOpen: true);
                        writer.Write(json); writer.Flush(); stream.Flush(flushToDisk: true);
                    }
                    if (File.Exists(ConfigPath))
                    {
                        // Keep malformed input recoverable before replacing it with new preferences.
                        var validOriginal = true;
                        try { Parse(ReadConfiguration(ConfigPath)); }
                        catch (JsonException) { validOriginal = false; File.Copy(ConfigPath, ConfigPath + ".corrupt-" + Guid.NewGuid().ToString("N")); }
                        File.Replace(temporaryPath, ConfigPath, validOriginal ? ConfigPath + ".bak" : null, ignoreMetadataErrors: true);
                    }
                    else File.Move(temporaryPath, ConfigPath);
                    Apply(config);
                    return true;
                }
                catch { return false; }
                finally { if (temporaryPath != null) { try { File.Delete(temporaryPath); } catch { } } }
            }
        }
    }

    public class StreamGuardConfig
    {
        public List<GuardedAppConfig> Apps { get; set; } = new();
        public bool BlockKnownWebsites { get; set; } = false;
        public bool BlockRaiaWebsite { get; set; } = false;
        public bool BlockPortalWebsite { get; set; } = false;
        public bool BlockCameraAccess { get; set; } = true;
        public bool BlockMicrophoneAccess { get; set; } = true;
        public bool PassiveFocusMode { get; set; } = true;
        public string Language { get; set; } = "en";
        public string ThemeMode { get; set; } = ThemeService.DarkMode;
        public bool CaptureGuardEnabled { get; set; } = true;
        public bool CaptureGuardIncludeMultiPurpose { get; set; }
        public bool AllowSelfScreenCapture { get; set; } = false;
        public bool InAppCursorCloakEnabled { get; set; } = true;
        public bool GlobalCursorCloakEnabled { get; set; } = false;
        public bool BlockSystemTools { get; set; } = false;
        public bool MuteAntigravityNotifications { get; set; } = false;
        public string ScreenshotDirectory { get; set; } = "";
        public bool ScreenshotAutoCopyClipboard { get; set; } = true;
        public bool ScreenshotExcludeFromCapture { get; set; } = true;
        public bool ScreenshotOpenEditorAfterCapture { get; set; } = true;
        public bool EnableCursorShield { get; set; } = false;
        public bool EnableAiTrafficShield { get; set; } = true;
        public string SnippingHotkey { get; set; } = "Ctrl+Alt+S";
        public bool BlockVirtualDesktopHotkeys { get; set; } = false;
        public bool AutoStartShield { get; set; } = false;
        public bool EnableSystemTrayIcon { get; set; } = false;
    }

    public class GuardedAppConfig
    {
        public string Name { get; set; } = "";
        public string ProcessName { get; set; } = "";
        public string ExecutablePath { get; set; } = "";
        public bool IsEnabled { get; set; } = true;
        public string SecureHotkey { get; set; } = "";
        public bool IsHotkeyDisabled { get; set; }
    }
}
