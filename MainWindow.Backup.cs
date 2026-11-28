using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using StreamGuard.Localization;
using StreamGuard.Models;
using StreamGuard.Services;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;

namespace StreamGuard;

public partial class MainWindow
{
    private DispatcherTimer? _settingsToastTimer;

    private void AutoStartShieldCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingConfig) return;
        var isChecked = (sender as WpfCheckBox)?.IsChecked == true;
        _isLoadingConfig = true;
        try
        {
            if (AutoStartShieldCheckBox != null)
                AutoStartShieldCheckBox.IsChecked = isChecked;
            if (AutoStartShieldQuickCheckBox != null)
                AutoStartShieldQuickCheckBox.IsChecked = isChecked;
        }
        finally
        {
            _isLoadingConfig = false;
        }

        UpdateAutoStartShieldUiState(isChecked);
        SaveConfig();
        var statusKey = isChecked ? "Settings.AutoStartShieldStatusOn" : "Settings.AutoStartShieldStatusOff";
        UpdateStatus(LocalizationService.GetString(statusKey, isChecked
            ? "Active: Guarded windows and capture cloak will engage automatically when Driver Booster opens."
            : "Inactive: Shield stays idle at launch until you click Start Protecting."), isChecked);
    }

    private void UpdateAutoStartShieldUiState(bool enabled)
    {
        if (AutoStartShieldStatusText != null)
        {
            LocalizationService.BindKey(AutoStartShieldStatusText, TextBlock.TextProperty,
                enabled ? "Settings.AutoStartShieldStatusOn" : "Settings.AutoStartShieldStatusOff");
        }
    }

    private void BtnExportSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = LocalizationService.GetString("Settings.ExportTitle", "Export Backup Configuration"),
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                FileName = $"StreamGuard_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.json",
                DefaultExt = ".json"
            };

            if (sfd.ShowDialog(this) == true)
            {
                var appsList = _apps.Select(a => new GuardedAppConfig
                {
                    Name = a.Name,
                    ProcessName = a.ProcessName,
                    ExecutablePath = a.ExecutablePath,
                    IsEnabled = a.IsEnabled,
                    SecureHotkey = a.SecureHotkey,
                    IsHotkeyDisabled = a.IsHotkeyDisabled
                }).ToList();

                var currentConfig = new StreamGuardConfig
                {
                    Apps = appsList,
                    BlockKnownWebsites = (BlockRaiaHostCheckBox?.IsChecked ?? _config.BlockRaiaWebsite) || (BlockPortalHostCheckBox?.IsChecked ?? _config.BlockPortalWebsite),
                    BlockRaiaWebsite = BlockRaiaHostCheckBox?.IsChecked ?? _config.BlockRaiaWebsite,
                    BlockPortalWebsite = BlockPortalHostCheckBox?.IsChecked ?? _config.BlockPortalWebsite,
                    BlockCameraAccess = BlockCameraAccessCheckBox?.IsChecked ?? _config.BlockCameraAccess,
                    BlockMicrophoneAccess = BlockMicrophoneAccessCheckBox?.IsChecked ?? _config.BlockMicrophoneAccess,
                    PassiveFocusMode = PassiveFocusCheckBox?.IsChecked ?? _config.PassiveFocusMode,
                    Language = LocalizationService.Current.Language,
                    ThemeMode = Services.ThemeService.Instance.CurrentTheme,
                    CaptureGuardEnabled = CaptureGuardCheckBox?.IsChecked ?? _config.CaptureGuardEnabled,
                    CaptureGuardIncludeMultiPurpose = CaptureMultiPurposeCheckBox?.IsChecked ?? _config.CaptureGuardIncludeMultiPurpose,
                    AllowSelfScreenCapture = AllowSelfScreenCaptureCheckBox?.IsChecked ?? _config.AllowSelfScreenCapture,
                    InAppCursorCloakEnabled = InAppCursorCloakCheckBox?.IsChecked ?? _config.InAppCursorCloakEnabled,
                    GlobalCursorCloakEnabled = GlobalCursorCloakCheckBox?.IsChecked ?? _config.GlobalCursorCloakEnabled,
                    BlockSystemTools = BlockSystemToolsCheckBox?.IsChecked ?? _config.BlockSystemTools,
                    MuteAntigravityNotifications = MuteAntigravityNotificationsCheckBox?.IsChecked ?? _config.MuteAntigravityNotifications,
                    ScreenshotDirectory = TxtScreenshotDir?.Text ?? _config.ScreenshotDirectory,
                    ScreenshotAutoCopyClipboard = ScreenshotClipboardCheckbox?.IsChecked ?? _config.ScreenshotAutoCopyClipboard,
                    ScreenshotExcludeFromCapture = ScreenshotExcludeCheckbox?.IsChecked ?? _config.ScreenshotExcludeFromCapture,
                    ScreenshotOpenEditorAfterCapture = ScreenshotOpenEditorCheckbox?.IsChecked ?? _config.ScreenshotOpenEditorAfterCapture,
                    EnableCursorShield = EnableCursorShieldCheckBox?.IsChecked ?? _config.EnableCursorShield,
                    EnableAiTrafficShield = EnableAiTrafficShieldCheckBox?.IsChecked ?? _config.EnableAiTrafficShield,
                    SnippingHotkey = string.IsNullOrWhiteSpace(_snippingHotkey) ? (string.IsNullOrWhiteSpace(_config.SnippingHotkey) ? "Ctrl+Alt+S" : _config.SnippingHotkey) : _snippingHotkey,
                    BlockVirtualDesktopHotkeys = BlockVirtualDesktopHotkeysSettingsCheckBox?.IsChecked ?? _config.BlockVirtualDesktopHotkeys,
                    AutoStartShield = AutoStartShieldCheckBox?.IsChecked ?? AutoStartShieldQuickCheckBox?.IsChecked ?? _config.AutoStartShield,
                    EnableSystemTrayIcon = SystemTrayIconCheckBox?.IsChecked ?? _config.EnableSystemTrayIcon
                };

                if (CaptionCustomKeywordsTextBox != null)
                {
                    _captionSettings.CustomKeywords = CaptionCustomKeywordsTextBox.Text?.Trim() ?? "";
                }
                var bundle = BackupService.CreateBundle(currentConfig, _captionSettings);
                if (BackupService.TryExportToFile(sfd.FileName, bundle, out var error))
                {
                    var filename = Path.GetFileName(sfd.FileName);
                    var template = LocalizationService.GetString("Settings.ExportSuccess", "Configuration successfully exported to '{0}'.");
                    var message = string.Format(template, filename);
                    UpdateStatus(message, true);
                    ShowSettingsToast(message, isSuccess: true);
                }
                else
                {
                    var template = LocalizationService.GetString("Settings.ExportError", "Failed to export configuration: {0}");
                    var message = string.Format(template, error ?? "Unknown error");
                    UpdateStatus(message, false);
                    ShowSettingsToast(message, isSuccess: false);
                }
            }
        }
        catch (Exception ex)
        {
            var template = LocalizationService.GetString("Settings.ExportError", "Failed to export configuration: {0}");
            var message = string.Format(template, ex.Message);
            UpdateStatus(message, false);
            ShowSettingsToast(message, isSuccess: false);
        }
    }

    private void BtnImportSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = LocalizationService.GetString("Settings.ImportTitle", "Restore Configuration from File"),
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = ".json"
            };

            if (ofd.ShowDialog(this) == true)
            {
                if (BackupService.TryImportFromFile(ofd.FileName, out var bundle, out var error) && bundle != null)
                {
                    ApplyRestoredBundle(bundle);
                    var count = _apps.Count;
                    var template = LocalizationService.GetString("Settings.ImportSuccess", "Configuration successfully restored! {0} app(s) and preferences updated.");
                    var message = string.Format(template, count);
                    UpdateStatus(message, true);
                    ShowSettingsToast(message, isSuccess: true);
                }
                else
                {
                    var template = LocalizationService.GetString("Settings.ImportError", "Failed to restore configuration: {0}");
                    var message = string.Format(template, error ?? LocalizationService.GetString("Settings.ImportInvalidFile", "The selected file is not a valid Driver Booster configuration backup."));
                    UpdateStatus(message, false);
                    ShowSettingsToast(message, isSuccess: false);
                }
            }
        }
        catch (Exception ex)
        {
            var template = LocalizationService.GetString("Settings.ImportError", "Failed to restore configuration: {0}");
            var message = string.Format(template, ex.Message);
            UpdateStatus(message, false);
            ShowSettingsToast(message, isSuccess: false);
        }
    }

    private void ApplyRestoredBundle(BackupBundle bundle)
    {
        var wasProtecting = _isProtecting;
        if (_isProtecting)
        {
            StopProtecting();
        }

        var config = bundle.Config;
        var captions = bundle.Captions;

        _isLoadingConfig = true;
        try
        {
            // 1. Guarded apps list
            _apps.Clear();
            foreach (var c in config.Apps)
            {
                _apps.Add(new GuardedApp
                {
                    Name = c.Name,
                    ProcessName = c.ProcessName,
                    ExecutablePath = c.ExecutablePath,
                    IsEnabled = c.IsEnabled,
                    SecureHotkey = c.SecureHotkey,
                    IsHotkeyDisabled = c.IsHotkeyDisabled,
                    Status = string.IsNullOrWhiteSpace(c.ExecutablePath)
                        ? "Queued: visible"
                        : "Queued: secure launch ready"
                });
            }

            // 2. Privacy & Threat Shield toggles
            if (BlockRaiaHostCheckBox != null) BlockRaiaHostCheckBox.IsChecked = config.BlockRaiaWebsite;
            if (BlockPortalHostCheckBox != null) BlockPortalHostCheckBox.IsChecked = config.BlockPortalWebsite;
            if (BlockCameraAccessCheckBox != null) BlockCameraAccessCheckBox.IsChecked = config.BlockCameraAccess;
            if (BlockMicrophoneAccessCheckBox != null) BlockMicrophoneAccessCheckBox.IsChecked = config.BlockMicrophoneAccess;
            if (PassiveFocusCheckBox != null) PassiveFocusCheckBox.IsChecked = config.PassiveFocusMode;
            _passiveFocusMode = config.PassiveFocusMode;

            if (CaptureGuardCheckBox != null) CaptureGuardCheckBox.IsChecked = config.CaptureGuardEnabled;
            if (CaptureMultiPurposeCheckBox != null) CaptureMultiPurposeCheckBox.IsChecked = config.CaptureGuardIncludeMultiPurpose;
            if (AllowSelfScreenCaptureCheckBox != null) AllowSelfScreenCaptureCheckBox.IsChecked = config.AllowSelfScreenCapture;
            UpdateVerifiedCaptureProtection();

            if (InAppCursorCloakCheckBox != null) InAppCursorCloakCheckBox.IsChecked = config.InAppCursorCloakEnabled;
            if (GlobalCursorCloakCheckBox != null) GlobalCursorCloakCheckBox.IsChecked = config.GlobalCursorCloakEnabled;
            _cursorCaptureProtection.IsInAppCloakEnabled = config.InAppCursorCloakEnabled;
            if (config.GlobalCursorCloakEnabled)
            {
                _cursorCaptureProtection.StartGlobalCursorCloak();
            }
            else
            {
                _cursorCaptureProtection.StopGlobalCursorCloak();
            }
            UpdateCursorCloakUiState();

            if (BlockSystemToolsCheckBox != null) BlockSystemToolsCheckBox.IsChecked = config.BlockSystemTools;
            UpdateSystemToolsUiState(config.BlockSystemTools);

            if (MuteAntigravityNotificationsCheckBox != null) MuteAntigravityNotificationsCheckBox.IsChecked = config.MuteAntigravityNotifications;
            UpdateAntigravityNotificationsUiState(config.MuteAntigravityNotifications);

            if (EnableCursorShieldCheckBox != null) EnableCursorShieldCheckBox.IsChecked = config.EnableCursorShield;
            UpdateCursorShieldUiState(config.EnableCursorShield);

            if (EnableAiTrafficShieldCheckBox != null) EnableAiTrafficShieldCheckBox.IsChecked = config.EnableAiTrafficShield;
            UpdateAiTrafficShieldUiState(config.EnableAiTrafficShield);

            if (TxtScreenshotDir != null) TxtScreenshotDir.Text = config.ScreenshotDirectory ?? "";
            if (ScreenshotClipboardCheckbox != null) ScreenshotClipboardCheckbox.IsChecked = config.ScreenshotAutoCopyClipboard;
            if (ScreenshotExcludeCheckbox != null) ScreenshotExcludeCheckbox.IsChecked = config.ScreenshotExcludeFromCapture;
            if (ScreenshotOpenEditorCheckbox != null) ScreenshotOpenEditorCheckbox.IsChecked = config.ScreenshotOpenEditorAfterCapture;

            if (BlockVirtualDesktopHotkeysSettingsCheckBox != null) BlockVirtualDesktopHotkeysSettingsCheckBox.IsChecked = config.BlockVirtualDesktopHotkeys;
            _hotkeys.BlockVirtualDesktopHotkeys = config.BlockVirtualDesktopHotkeys;
            UpdateVirtualDesktopHotkeysUiState(config.BlockVirtualDesktopHotkeys);

            if (AutoStartShieldCheckBox != null) AutoStartShieldCheckBox.IsChecked = config.AutoStartShield;
            if (AutoStartShieldQuickCheckBox != null) AutoStartShieldQuickCheckBox.IsChecked = config.AutoStartShield;
            UpdateAutoStartShieldUiState(config.AutoStartShield);

            if (SystemTrayIconCheckBox != null) SystemTrayIconCheckBox.IsChecked = config.EnableSystemTrayIcon;
            ApplySystemTrayIconPreference(config.EnableSystemTrayIcon);

            // Snipping hotkey
            _snippingHotkey = string.IsNullOrWhiteSpace(config.SnippingHotkey) ? "Ctrl+Alt+S" : config.SnippingHotkey;
            _currentSnippingHotkey = _snippingHotkey;
            SyncSnippingHotkeyUi();
            RegisterSnippingHotkey();

            // Language & Theme
            var lang = LocalizationService.NormalizeLanguage(config.Language);
            LocalizationService.Current.SetLanguage(lang);
            if (LanguageComboBox != null) LanguageComboBox.SelectedValue = lang;
            ApplyAndSyncTheme(config.ThemeMode, save: false, showToast: false);
            RefreshTrayLanguage();
            UpdateAntigravityInstallerUiState();
            RefreshDiagnosticsLanguage();

            // 3. Captions (update in-place to keep _captionOverlay reference coherent)
            var targetCaptions = captions ?? new CaptionSettings();
            targetCaptions.Normalize();
            _captionSettings.FontSize = targetCaptions.FontSize;
            _captionSettings.SingleLine = targetCaptions.SingleLine;
            _captionSettings.ExcludeFromCapture = targetCaptions.ExcludeFromCapture;
            _captionSettings.Left = targetCaptions.Left;
            _captionSettings.Top = targetCaptions.Top;
            _captionSettings.EnableTranslation = targetCaptions.EnableTranslation;
            _captionSettings.TranslationMode = targetCaptions.TranslationMode;
            _captionSettings.TranslationHotkey = targetCaptions.TranslationHotkey;
            _captionSettings.CustomKeywords = targetCaptions.CustomKeywords;
            _captionSettings.Normalize();
            _captionVocabulary.UpdateKeywords(_captionSettings.CustomKeywords);
            _captionSettings.Save(_config.CaptionSettingsPath);

            if (CaptionSingleLineCheckBox != null) CaptionSingleLineCheckBox.IsChecked = _captionSettings.SingleLine;
            if (CaptionCaptureCheckBox != null) CaptionCaptureCheckBox.IsChecked = _captionSettings.ExcludeFromCapture;
            if (CaptionTranslationCheckBox != null) CaptionTranslationCheckBox.IsChecked = _captionSettings.EnableTranslation;
            if (CaptionModeBilingualRadio != null) CaptionModeBilingualRadio.IsChecked = _captionSettings.TranslationMode == "Bilingual";
            if (CaptionModeVietnameseRadio != null) CaptionModeVietnameseRadio.IsChecked = _captionSettings.TranslationMode == "VietnameseOnly";
            if (CaptionCustomKeywordsTextBox != null) CaptionCustomKeywordsTextBox.Text = _captionSettings.CustomKeywords;
            UpdateCaptionVocabularyButtonsState();
            UpdateCaptionCaptureStatus();
            _captionOverlay?.ApplySettings();
            _captionOverlay?.ResetPosition(useSaved: true);
        }
        finally
        {
            _isLoadingConfig = false;
        }

        UpdateEmptyState();
        EnsureDefaultAppHotkeys();
        RebuildAppHotkeys();
        SaveConfig();

        // Enforce network / hardware blocks immediately if changed
        _ = ApplyWebsiteBlockPreferenceAsync(
            BlockRaiaHostCheckBox?.IsChecked == true,
            BlockPortalHostCheckBox?.IsChecked == true,
            saveConfig: false,
            showProgress: false);
        _ = ApplyDevicePrivacyPreferenceAsync(
            BlockCameraAccessCheckBox?.IsChecked == true,
            BlockMicrophoneAccessCheckBox?.IsChecked == true,
            saveConfig: false,
            showProgress: false);
        ApplySystemToolsPreference(
            BlockSystemToolsCheckBox?.IsChecked == true,
            saveConfig: false,
            updateBanner: false);
        ApplyAntigravityNotificationsPreference(
            MuteAntigravityNotificationsCheckBox?.IsChecked == true,
            saveConfig: false,
            updateBanner: false);

        if (wasProtecting)
        {
            StartProtecting();
        }
    }

    private void ShowSettingsToast(string message, bool isSuccess = true)
    {
        if (SettingsToast == null || TxtSettingsToast == null)
        {
            UpdateStatus(message, isSuccess);
            return;
        }

        TxtSettingsToast.Text = message;
        if (isSuccess)
        {
            SettingsToast.Background = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#052e23"));
            SettingsToast.BorderBrush = (WpfBrush)FindResource("SuccessBrush");
            TxtSettingsToast.Foreground = (WpfBrush)FindResource("SuccessBrush");
            if (SettingsToastIcon != null)
            {
                SettingsToastIcon.Data = (Geometry)FindResource("UiIconCheck");
                SettingsToastIcon.Stroke = (WpfBrush)FindResource("SuccessBrush");
            }
        }
        else
        {
            SettingsToast.Background = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#2a0f14"));
            SettingsToast.BorderBrush = (WpfBrush)FindResource("DangerBrush");
            TxtSettingsToast.Foreground = (WpfBrush)FindResource("DangerBrush");
            if (SettingsToastIcon != null)
            {
                SettingsToastIcon.Data = (Geometry)FindResource("UiIconAlert");
                SettingsToastIcon.Stroke = (WpfBrush)FindResource("DangerBrush");
            }
        }

        SettingsToast.Visibility = Visibility.Visible;
        _settingsToastTimer?.Stop();
        _settingsToastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _settingsToastTimer.Tick += (s, args) =>
        {
            _settingsToastTimer?.Stop();
            SettingsToast.Visibility = Visibility.Collapsed;
        };
        _settingsToastTimer.Start();
    }
}
