using StreamGuard.Localization;
using StreamGuard.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace StreamGuard;

public partial class MainWindow
{
    private readonly CaptureRiskService _captureRiskService = new();
    private readonly WindowCaptureProtectionService _windowCaptureProtection = new();
    private readonly IPrivacyCurtain _capturePrivacyCurtain;
    private readonly DispatcherTimer _captureRiskTimer = new();
    private CaptureAssessment _captureAssessment = CaptureAssessment.Unknown;
    private CaptureAffinityResult? _lastCaptureAffinity;
    private bool? _lastAllowSelfScreenCapture;
    private bool _captureScanInProgress;
    private bool _captureGuardPaused;
    private bool _captureCurtainHotkeyReady;
    private bool _isWindowMinimized;

    private void InitializeCaptureGuard()
    {
        _captureRiskTimer.Interval = CaptureRiskService.ActiveInterval;
        _captureRiskTimer.Tick += async (_, _) => await ScanCaptureRiskAsync();
        UpdateCaptureGuardState();
    }

    private void StartCaptureRiskMonitoring()
    {
        _captureRiskTimer.Start();
        _ = ScanCaptureRiskAsync();
    }

    private void StopCaptureRiskMonitoring()
    {
        _captureRiskTimer.Stop();
        _capturePrivacyCurtain.Hide();
    }

    private async Task ScanCaptureRiskAsync()
    {
        if (_captureScanInProgress || _cleanupDone) return;
        _captureScanInProgress = true;
        BtnScanCaptureTools.IsEnabled = false;
        try
        {
            var isShieldEnabled = EnableAiTrafficShieldCheckBox?.IsChecked ?? _config.EnableAiTrafficShield;
            var (assessment, aiAssessment, autoPurged) = await Task.Run(() =>
            {
                var ca = _captureRiskService.Scan();
                var proxyStatus = _aiTrafficProtection.CheckProxyStatus();
                var purged = false;
                if (isShieldEnabled && proxyStatus.IsRogueProxy)
                {
                    purged = _aiTrafficProtection.PurgeProxySettings();
                    if (purged)
                    {
                        proxyStatus = _aiTrafficProtection.CheckProxyStatus();
                    }
                }
                var certs = _aiTrafficProtection.ScanRogueCertificates(forceRefresh: false);
                var hosts = _aiTrafficProtection.CheckAiHostsProtection();
                if (isShieldEnabled && hosts.IsTampered)
                {
                    var hostsPurged = _aiTrafficProtection.PurgeAiHostsHijacks();
                    if (hostsPurged)
                    {
                        hosts = _aiTrafficProtection.CheckAiHostsProtection(forceRefresh: true);
                        purged = true;
                    }
                }
                var envProxy = _aiTrafficProtection.CheckEnvironmentProxyStatus();
                if (isShieldEnabled && (envProxy.HasRogueEnvVars || envProxy.HasTlsBypassOrCustomCa))
                {
                    if (_aiTrafficProtection.PurgeEnvironmentProxySettings())
                    {
                        envProxy = _aiTrafficProtection.CheckEnvironmentProxyStatus();
                        purged = true;
                    }
                }
                var winHttpProxy = _aiTrafficProtection.CheckWinHttpProxy();
                var sniffers = ca.Tools.Where(t => t.Kind == CaptureToolKind.Sniffer).ToArray();
                var ai = new AiTrafficAssessment(proxyStatus, certs, sniffers, hosts, envProxy, winHttpProxy);
                return (ca, ai, purged);
            });

            if (_cleanupDone || Dispatcher.HasShutdownStarted) return;
            _captureAssessment = assessment;
            var hasRisk = !assessment.ScanSucceeded || assessment.Tools.Count > 0 || (aiAssessment != null && !aiAssessment.IsAllSafe);
            _captureRiskTimer.Interval = CaptureRiskService.DetermineAdaptiveInterval(_isWindowMinimized, hasRisk);
            if (!assessment.ScanSucceeded)
                LocalizationService.BindKey(CaptureToolsText, TextBlock.TextProperty, "Capture.ScanIncomplete");
            else if (assessment.Tools.Count == 0)
                LocalizationService.BindKey(CaptureToolsText, TextBlock.TextProperty, "Capture.NoKnownTools");
            else
                LocalizationService.BindText(CaptureToolsText, TextBlock.TextProperty,
                    $"Recognized capture-capable tools present: {string.Join(", ", assessment.Tools.Select(tool => tool.Name))}");
            UpdateCaptureGuardState();

            if (autoPurged)
            {
                UpdateStatus(LocalizationService.GetString("AiTraffic.AutoPurgedBanner", "Rogue proxy intercepted and purged automatically to protect AI traffic!"), true);
            }
            UpdateAiTrafficShieldUiState(isShieldEnabled, assessment, aiAssessment);
        }
        catch
        {
            if (!_cleanupDone && !Dispatcher.HasShutdownStarted)
            {
                _captureAssessment = CaptureAssessment.Unknown;
                LocalizationService.BindKey(CaptureToolsText, TextBlock.TextProperty, "Capture.ScanIncomplete");
                UpdateCaptureGuardState();
            }
        }
        finally
        {
            _captureScanInProgress = false;
            if (!_cleanupDone) BtnScanCaptureTools.IsEnabled = true;
        }
    }

    private void UpdateVerifiedCaptureProtection()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var allowSelfCapture = AllowSelfScreenCaptureCheckBox?.IsChecked ?? _config.AllowSelfScreenCapture;

        if (allowSelfCapture)
        {
            if (hwnd != IntPtr.Zero)
            {
                try { SetWindowDisplayAffinity(hwnd, WDA_NONE); } catch { }
            }

            if (_lastAllowSelfScreenCapture == true) return;
            _lastAllowSelfScreenCapture = true;
            _lastCaptureAffinity = null;

            LocalizationService.BindKey(CaptureBadgeText, TextBlock.TextProperty, "Capture.BadgeCaptureAllowed");
            LocalizationService.BindKey(CaptureStatusBadge, FrameworkElement.ToolTipProperty, "Capture.AllowSelfScreenCaptureTip");
            CaptureStatusDot.Fill = (System.Windows.Media.Brush)FindResource("InfoBrush");
            if (CaptureStatusBadge != null)
            {
                CaptureStatusBadge.Background = (System.Windows.Media.Brush)FindResource("BlueBgSubtleBrush");
                CaptureStatusBadge.BorderBrush = (System.Windows.Media.Brush)FindResource("InfoBrush");
                if (CaptureBadgeText != null) CaptureBadgeText.Foreground = (System.Windows.Media.Brush)FindResource("InfoBrush");
            }
            LocalizationService.BindKey(CaptureAffinityText, TextBlock.TextProperty, "Capture.AffinityCaptureAllowed");
            return;
        }

        var result = _windowCaptureProtection.ApplyAndVerify(hwnd);
        if (_lastAllowSelfScreenCapture == false && result == _lastCaptureAffinity) return;
        _lastAllowSelfScreenCapture = false;
        _lastCaptureAffinity = result;
        var key = result.Mode switch
        {
            CaptureAffinityMode.Excluded => "Capture.BadgeVerified",
            CaptureAffinityMode.MonitorOnly => "Capture.BadgeLimited",
            _ => "Capture.BadgeUnverified"
        };
        LocalizationService.BindKey(CaptureBadgeText, TextBlock.TextProperty, key);
        LocalizationService.BindKey(CaptureStatusBadge, FrameworkElement.ToolTipProperty, "Capture.AffinityScope");
        CaptureStatusDot.Fill = result.Mode == CaptureAffinityMode.Excluded
            ? (System.Windows.Media.Brush)FindResource("SuccessBrush")
            : (System.Windows.Media.Brush)FindResource("WarningBrush");
        if (CaptureStatusBadge != null)
        {
            if (result.Mode == CaptureAffinityMode.Excluded)
            {
                CaptureStatusBadge.Background = (System.Windows.Media.Brush)FindResource("SuccessBgSubtleBrush");
                CaptureStatusBadge.BorderBrush = (System.Windows.Media.Brush)FindResource("SuccessBorderBrush");
                if (CaptureBadgeText != null) CaptureBadgeText.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");
            }
            else
            {
                CaptureStatusBadge.Background = (System.Windows.Media.Brush)FindResource("WarningBgSubtleBrush");
                CaptureStatusBadge.BorderBrush = (System.Windows.Media.Brush)FindResource("WarningBorderBrush");
                if (CaptureBadgeText != null) CaptureBadgeText.Foreground = (System.Windows.Media.Brush)FindResource("WarningBrush");
            }
        }
        if (result.Mode == CaptureAffinityMode.Unverified && result.ErrorCode != 0)
            LocalizationService.BindText(CaptureAffinityText, TextBlock.TextProperty,
                $"Capture protection could not be verified (Windows error {result.ErrorCode}).");
        else
            LocalizationService.BindKey(CaptureAffinityText, TextBlock.TextProperty, result.Mode switch
            {
                CaptureAffinityMode.Excluded => "Capture.AffinityVerified",
                CaptureAffinityMode.MonitorOnly => "Capture.AffinityLimited",
                _ => "Capture.AffinityUnverified"
            });
    }

    private void UpdateCaptureGuardState()
    {
        if (CaptureGuardCheckBox == null || _cleanupDone) return;
        var enabled = CaptureGuardCheckBox.IsChecked == true;
        var cover = CaptureRiskService.ShouldCover(enabled, _isProtecting, _captureGuardPaused,
            CaptureMultiPurposeCheckBox.IsChecked == true, _captureAssessment, _captureCurtainHotkeyReady);
        if (cover)
        {
            _capturePrivacyCurtain.Show();
            _cursorCaptureProtection.ReassertOverlayTopmost();
        }
        else _capturePrivacyCurtain.Hide();
        var key = !enabled ? "Capture.GuardOff"
            : !_isProtecting ? "Capture.GuardIdle"
            : !_captureCurtainHotkeyReady ? "Capture.RecoveryUnavailable"
            : _captureGuardPaused ? "Capture.GuardPaused"
            : cover && !_capturePrivacyCurtain.CoversAllTargetScreens ? "Capture.CurtainFailed"
            : cover ? "Capture.CurtainActive"
            : "Capture.GuardWatching";
        LocalizationService.BindKey(CaptureGuardStateText, TextBlock.TextProperty, key);
        BtnResumeCaptureGuard.IsEnabled = enabled && _captureGuardPaused;
        BtnManualCaptureCurtain.IsEnabled = _captureCurtainHotkeyReady;
        UpdateStopButtonState();
    }

    private void AllowSelfScreenCaptureCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingConfig) return;
        var allowed = AllowSelfScreenCaptureCheckBox?.IsChecked == true;
        UpdateVerifiedCaptureProtection();
        SaveConfig();
        UpdateStatus(
            LocalizationService.GetString(
                allowed ? "Capture.SelfCaptureAllowedToast" : "Capture.SelfCaptureProtectedToast",
                allowed
                    ? "Đã cho phép quay/chụp màn hình cửa sổ Driver Booster"
                    : "Đã bật lại ẩn cửa sổ Driver Booster khỏi quay/chụp màn hình"),
            true);
    }

    private void CaptureGuardPreference_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingConfig) return;
        _captureGuardPaused = false;
        var saved = SaveConfig();
        UpdateCaptureGuardState();
        if (!saved)
        {
            UpdateStatus("", false);
            LocalizationService.BindKey(StatusText, TextBlock.TextProperty, "Capture.SaveFailed");
        }
        _ = ScanCaptureRiskAsync();
    }

    private void CursorCloakPreference_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingConfig) return;
        var inApp = InAppCursorCloakCheckBox?.IsChecked ?? true;
        var global = GlobalCursorCloakCheckBox?.IsChecked ?? false;

        _cursorCaptureProtection.IsInAppCloakEnabled = inApp;
        if (global != _cursorCaptureProtection.IsGlobalCloakActive)
        {
            if (global) _cursorCaptureProtection.StartGlobalCursorCloak();
            else _cursorCaptureProtection.StopGlobalCursorCloak();
        }
        UpdateCursorCloakUiState();
        UpdateCursorShieldForProtectionState();
        SaveConfig();
    }

    public void ToggleGlobalCursorCloak()
    {
        if (_cursorCaptureProtection.IsGlobalCloakActive)
        {
            _cursorCaptureProtection.StopGlobalCursorCloak();
            if (GlobalCursorCloakCheckBox != null)
            {
                _isLoadingConfig = true;
                GlobalCursorCloakCheckBox.IsChecked = false;
                _isLoadingConfig = false;
            }
            UpdateStatus(LocalizationService.GetString("Capture.CursorCloakDisabledToast", "Default Windows system cursor restored"), true);
        }
        else
        {
            var started = _cursorCaptureProtection.StartGlobalCursorCloak();
            if (GlobalCursorCloakCheckBox != null)
            {
                _isLoadingConfig = true;
                GlobalCursorCloakCheckBox.IsChecked = started;
                _isLoadingConfig = false;
            }
            if (started)
            {
                UpdateStatus(LocalizationService.GetString("Capture.CursorCloakEnabledToast", "Global stealth cursor cloak enabled (Ctrl+Shift+M to toggle)"), true);
            }
        }
        UpdateCursorCloakUiState();
        UpdateCursorShieldForProtectionState();
        SaveConfig();
    }

    private void UpdateCursorCloakUiState()
    {
        if (GlobalCursorCloakStateText == null) return;
        var active = _cursorCaptureProtection.IsGlobalCloakActive;
        LocalizationService.BindKey(GlobalCursorCloakStateText, TextBlock.TextProperty,
            active ? "Capture.GlobalCursorCloakOn" : "Capture.GlobalCursorCloakOff");
        GlobalCursorCloakStateText.Foreground = active
            ? (System.Windows.Media.Brush)FindResource("SuccessTextBrush")
            : (System.Windows.Media.Brush)FindResource("TextDimBrush");
    }

    private void OnGlobalCloakStateChanged(bool active)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (GlobalCursorCloakCheckBox != null && GlobalCursorCloakCheckBox.IsChecked != active)
            {
                _isLoadingConfig = true;
                GlobalCursorCloakCheckBox.IsChecked = active;
                _isLoadingConfig = false;
            }
            UpdateCursorCloakUiState();
            UpdateCursorShieldForProtectionState();
            if (!active)
            {
                UpdateStatus(LocalizationService.GetString("Capture.CursorCloakDisabledToast", "Default Windows system cursor restored"), true);
            }
        });
    }

    private void OnEmergencyEscapeTriggered()
    {
        Dispatcher.InvokeAsync(() =>
        {
            var msg = LocalizationService.GetString(
                "Capture.CursorCloakDisabledToast",
                "Default Windows system cursor restored");
            UpdateStatus(msg, true);
        });
    }

    private async void BtnScanCaptureTools_Click(object sender, RoutedEventArgs e) => await ScanCaptureRiskAsync();
    private void BtnResumeCaptureGuard_Click(object sender, RoutedEventArgs e)
    {
        _captureGuardPaused = false;
        UpdateCaptureGuardState();
        _ = ScanCaptureRiskAsync();
    }
    private void BtnCaptureCurtain_Click(object sender, RoutedEventArgs e) => TogglePrivacyCurtain();

    public void SetCaptureGuardMinimized(bool minimized)
    {
        _isWindowMinimized = minimized;
        var hasRisk = !_captureAssessment.ScanSucceeded || _captureAssessment.Tools.Count > 0;
        _captureRiskTimer.Interval = CaptureRiskService.DetermineAdaptiveInterval(_isWindowMinimized, hasRisk);
    }

    public void RestoreCaptureGuardActivePolling()
    {
        _isWindowMinimized = false;
        _captureRiskTimer.Interval = CaptureRiskService.ActiveInterval;
        _ = ScanCaptureRiskAsync();
    }
}
