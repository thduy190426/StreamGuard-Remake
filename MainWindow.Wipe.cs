using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using StreamGuard.Localization;
using StreamGuard.Models;
using StreamGuard.Services;
using WpfApplication = System.Windows.Application;

namespace StreamGuard;

public partial class MainWindow
{
    private Action? _pendingDangerAction;

    internal void ShowDangerConfirmModal(string title, string message, string confirmButtonText, Action onConfirm)
    {
        if (_cleanupDone || DangerConfirmModalOverlay == null)
            return;

        _pendingDangerAction = onConfirm;
        if (DangerConfirmTitleText != null)
            DangerConfirmTitleText.Text = title;
        if (DangerConfirmMessageText != null)
            DangerConfirmMessageText.Text = message;
        if (BtnDangerConfirmAccept != null)
            BtnDangerConfirmAccept.Content = confirmButtonText;

        DangerConfirmModalOverlay.Visibility = Visibility.Visible;
    }

    internal void CloseDangerConfirmModal()
    {
        _pendingDangerAction = null;
        if (DangerConfirmModalOverlay != null)
            DangerConfirmModalOverlay.Visibility = Visibility.Collapsed;
    }

    private void BtnDangerConfirmCancel_Click(object sender, RoutedEventArgs e) => CloseDangerConfirmModal();

    private void DangerConfirmModalBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CloseDangerConfirmModal();
        e.Handled = true;
    }

    private void DangerConfirmModalCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Keep clicks inside the modal card from bubbling to the backdrop and canceling.
        e.Handled = true;
    }

    private void BtnDangerConfirmAccept_Click(object sender, RoutedEventArgs e)
    {
        var action = _pendingDangerAction;
        CloseDangerConfirmModal();
        action?.Invoke();
    }

    private void BtnResetAppData_Click(object sender, RoutedEventArgs e)
    {
        var title = LocalizationService.GetString("Settings.ConfirmResetTitle", "Confirm Factory Reset");
        var message = LocalizationService.GetString("Settings.ConfirmResetMessage",
            "Are you sure you want to wipe all application data, clear cache, screenshots, and reset settings to default? This action cannot be undone.");
        var confirmLabel = LocalizationService.GetString("Settings.ResetDataButton", "Factory Reset & Wipe Data");

        ShowDangerConfirmModal(title, message, confirmLabel, ExecuteFactoryReset);
    }

    private void ExecuteFactoryReset()
    {
        try
        {
            // 1. Stop active protection
            StopProtecting();

            // 2. Restore all windows
            PanicRestoreAll();

            // 3. Close virtual desktop if open
            try { _vdService.RemoveHiddenDesktop(); } catch { }

            // 4. Remove system modifications
            CleanWipeService.RevertAllSystemModifications(out _, _vdService);

            // 5. Terminate background processes
            CleanWipeService.TerminateBackgroundProcesses(Process.GetCurrentProcess().Id);

            // 6. Purge application data (AppData, LocalAppData, temp files, and screenshots per Option B)
            CleanWipeService.PurgeApplicationData(
                includeScreenshots: true,
                customScreenshotDir: _config?.ScreenshotDirectory);

            // 7. Reset application UI and in-memory configuration to factory defaults (Option A)
            ResetApplicationToDefaults();

            var successMsg = LocalizationService.GetString("Settings.ResetSuccess",
                "Factory reset complete. Application data wiped and restored to factory defaults.");
            ShowSettingsToast(successMsg, isSuccess: true);
            UpdateStatus(successMsg, true);
        }
        catch (Exception ex)
        {
            var errMsg = $"Factory reset failed: {ex.Message}";
            ShowSettingsToast(errMsg, isSuccess: false);
            UpdateStatus(errMsg, false);
        }
    }

    private void BtnCompleteUninstall_Click(object sender, RoutedEventArgs e)
    {
        var title = LocalizationService.GetString("Settings.ConfirmUninstallTitle", "Confirm Complete Uninstall");
        var message = LocalizationService.GetString("Settings.ConfirmUninstallMessage",
            "Are you sure you want to completely uninstall Driver Booster, delete all data, screenshots, and remove the executable? This action is permanent and cannot be undone.");
        var confirmLabel = LocalizationService.GetString("Settings.UninstallButton", "Complete Clean Uninstall");

        ShowDangerConfirmModal(title, message, confirmLabel, ExecuteCompleteUninstall);
    }

    private void ExecuteCompleteUninstall()
    {
        try
        {
            var uninstallingMsg = LocalizationService.GetString("Settings.UninstallLaunching",
                "Uninstallation initiated. Closing Driver Booster to complete removal...");
            UpdateStatus(uninstallingMsg, false);

            // 1. Stop active protection
            StopProtecting();

            // 2. Restore all windows
            PanicRestoreAll();

            // 3. Close virtual desktop if open
            try { _vdService.RemoveHiddenDesktop(); } catch { }

            // 4. Revert system modifications
            CleanWipeService.RevertAllSystemModifications(out _, _vdService);

            // 4b. Stop global cursor cloak and restore system cursors
            _cursorCaptureProtection.StopGlobalCursorCloak();
            CursorCaptureProtectionService.RestoreSystemCursors();

            // 5. Identify executable path and current PID
            var exePath = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Driver Booster.exe");
            var currentPid = Process.GetCurrentProcess().Id;
            var isSourceRepo = CleanWipeService.IsSourceRepository(exePath);

            // 6. Launch detached background uninstaller PowerShell script
            var launched = CleanWipeService.LaunchDetachedUninstaller(
                exePath: exePath,
                currentPid: currentPid,
                isSourceRepo: isSourceRepo,
                includeScreenshots: true,
                customScreenshotDir: _config?.ScreenshotDirectory,
                out _);

            if (!launched)
            {
                var launchFailMsg = "Failed to launch uninstaller background script.";
                ShowSettingsToast(launchFailMsg, isSuccess: false);
                UpdateStatus(launchFailMsg, false);
                return;
            }

            // 7. Terminate any child worker processes
            CleanWipeService.TerminateBackgroundProcesses(currentPid);

            // 8. Clean up resources and exit application cleanly
            CleanupAndExit();
            WpfApplication.Current.Shutdown();
        }
        catch (Exception ex)
        {
            var errMsg = $"Uninstall failed to launch: {ex.Message}";
            ShowSettingsToast(errMsg, isSuccess: false);
            UpdateStatus(errMsg, false);
        }
    }

    private void ResetApplicationToDefaults()
    {
        // 1. Clear application list and unregister app hotkeys
        _apps.Clear();
        RebuildAppHotkeys();

        // 2. Clear recovery records in memory
        if (_recovery != null)
        {
            _recovery.Reset();
        }

        // 2b. Close and reset transcript viewer if active
        CloseTranscriptReader();

        // 3. Reset blocker services
        _systemToolsBlocker.SetEnabled(false);
        _threatBlocker.SetWebsiteBlockPreference(false, false);
        _threatBlocker.SetDevicePrivacyPreference(false, false);

        // 3b. Stop global cursor cloak and reset cursor protection
        _cursorCaptureProtection.StopGlobalCursorCloak();
        _cursorCaptureProtection.IsInAppCloakEnabled = true;
        CursorCaptureProtectionService.RestoreSystemCursors();
        CursorShieldService.RestoreAllCursors();

        // 3c. Clear active toasts
        _toastService?.ClearAll();

        // 4. Save clean default config to disk (recreating %APPDATA%\StreamGuard\config.json)
        _config.Save(
            apps: Enumerable.Empty<GuardedApp>(),
            blockKnownWebsites: false,
            blockRaiaWebsite: false,
            blockPortalWebsite: false,
            blockCameraAccess: true,
            blockMicrophoneAccess: true,
            passiveFocusMode: true,
            language: "en",
            captureGuardEnabled: true,
            captureGuardIncludeMultiPurpose: false,
            inAppCursorCloakEnabled: true,
            globalCursorCloakEnabled: false,
            blockSystemTools: false,
            muteAntigravityNotifications: false,
            screenshotDirectory: "",
            screenshotAutoCopyClipboard: true,
            screenshotExcludeFromCapture: true,
            enableCursorShield: false,
            enableAiTrafficShield: true,
            snippingHotkey: "Ctrl+Alt+S",
            blockVirtualDesktopHotkeys: false,
            autoStartShield: false);

        // 5. Reload clean config from disk into all UI controls and settings
        LoadConfig();
    }
}
