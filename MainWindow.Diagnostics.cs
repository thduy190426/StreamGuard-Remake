using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using StreamGuard.Localization;
using StreamGuard.Models;
using StreamGuard.Services;

namespace StreamGuard;

public partial class MainWindow
{
    private HardwareDiagnosticsResult? _latestDiagnostics;
    private bool _isDiagnosticsRunning;
    private DispatcherTimer? _diagnosticsToastTimer;

    internal async Task LoadHardwareDiagnosticsAsync(bool force = false)
    {
        if (_isDiagnosticsRunning) return;
        if (!force && _latestDiagnostics != null)
        {
            UpdateDiagnosticsUi(_latestDiagnostics);
            return;
        }

        _isDiagnosticsRunning = true;
        try
        {
            if (BtnRefreshDiagnostics != null) BtnRefreshDiagnostics.IsEnabled = false;

            var result = await HardwareDiagnosticsService.Instance.CollectHardwareInfoAsync();
            _latestDiagnostics = result;
            UpdateDiagnosticsUi(result);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Diagnostics] Error: {ex.Message}");
        }
        finally
        {
            _isDiagnosticsRunning = false;
            if (BtnRefreshDiagnostics != null) BtnRefreshDiagnostics.IsEnabled = true;
        }
    }

    private void UpdateDiagnosticsUi(HardwareDiagnosticsResult result)
    {
        var isVi = LocalizationService.Current.Language == "vi";

        // Score
        if (DiagnosticsScoreText != null)
            DiagnosticsScoreText.Text = result.CompatibilityScore.ToString();

        // Tier
        string tierText;
        SolidColorBrush tierFg;
        SolidColorBrush tierBg;
        SolidColorBrush tierBorder;

        switch (result.OverallTier)
        {
            case ReadinessTier.Optimal:
                tierText = isVi ? "XUẤT SẮC - MƯỢT MÀ 100%" : "OPTIMAL - 100% SMOOTH";
                tierFg = (SolidColorBrush)FindResource("SuccessBrush");
                tierBg = (SolidColorBrush)FindResource("SuccessBgBrush");
                tierBorder = (SolidColorBrush)FindResource("SuccessBrush");
                break;
            case ReadinessTier.Good:
                tierText = isVi ? "ĐỦ ĐIỀU KIỆN - RẤT TỐT" : "GOOD - FULLY CAPABLE";
                tierFg = (SolidColorBrush)FindResource("InfoBrush");
                tierBg = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0c, 0x27, 0x3d));
                tierBorder = (SolidColorBrush)FindResource("InfoBrush");
                break;
            case ReadinessTier.Warning:
                tierText = isVi ? "CẦN LƯU Ý - CÓ GIỚI HẠN PHỤ" : "NOTICE - MINOR BOTTLENECKS";
                tierFg = (SolidColorBrush)FindResource("WarningBrush");
                tierBg = (SolidColorBrush)FindResource("WarningBgBrush");
                tierBorder = (SolidColorBrush)FindResource("WarningBrush");
                break;
            default:
                tierText = isVi ? "KHÔNG KHUYẾN NGHỊ" : "INSUFFICIENT - UPGRADE NEEDED";
                tierFg = (SolidColorBrush)FindResource("DangerBrush");
                tierBg = (SolidColorBrush)FindResource("DangerBgBrush");
                tierBorder = (SolidColorBrush)FindResource("DangerBrush");
                break;
        }

        if (DiagnosticsTierText != null)
        {
            DiagnosticsTierText.Text = tierText;
            DiagnosticsTierText.Foreground = tierFg;
        }
        if (DiagnosticsTierBadge != null)
        {
            DiagnosticsTierBadge.Background = tierBg;
            DiagnosticsTierBadge.BorderBrush = tierBorder;
        }
        if (DiagnosticsSummaryText != null)
        {
            DiagnosticsSummaryText.Text = result.OverallTier switch
            {
                ReadinessTier.Optimal => LocalizationService.GetString("Diagnostics.SummaryOptimal", "System exceeds all performance requirements."),
                ReadinessTier.Good => LocalizationService.GetString("Diagnostics.SummaryGood", "System satisfies all core requirements."),
                ReadinessTier.Warning => LocalizationService.GetString("Diagnostics.SummaryWarning", "System can run Driver Booster, but some features may experience minor limitations."),
                _ => LocalizationService.GetString("Diagnostics.SummaryInsufficient", "System hardware or OS version is below recommended standards.")
            };
        }

        // Hardware Specs Cards
        if (DiagnosticsCpuName != null)
            DiagnosticsCpuName.Text = result.Cpu.Name;
        if (DiagnosticsCpuCores != null)
            DiagnosticsCpuCores.Text = isVi
                ? $"{result.Cpu.Cores} nhân / {result.Cpu.LogicalProcessors} luồng ({result.Cpu.Architecture})"
                : $"{result.Cpu.Cores} cores / {result.Cpu.LogicalProcessors} threads ({result.Cpu.Architecture})";
        if (DiagnosticsCpuAvx != null)
        {
            if (result.Cpu.HasAvx2)
                DiagnosticsCpuAvx.Text = isVi ? "AVX2 & AVX (Tăng tốc tối đa Whisper AI)" : "AVX2 & AVX (Max Whisper AI speed)";
            else if (result.Cpu.HasAvx)
                DiagnosticsCpuAvx.Text = isVi ? "AVX (Tương thích CPU thường)" : "AVX (Standard CPU compatibility)";
            else
                DiagnosticsCpuAvx.Text = isVi ? "Không hỗ trợ AVX (Chế độ cơ bản)" : "No AVX instruction set";
        }

        if (DiagnosticsRamDetails != null)
            DiagnosticsRamDetails.Text = isVi
                ? $"{result.Ram.TotalGigabytes:F1} GB Tổng | {result.Ram.AvailableGigabytes:F1} GB Trống"
                : $"{result.Ram.TotalGigabytes:F1} GB Total | {result.Ram.AvailableGigabytes:F1} GB Free";
        if (DiagnosticsRamLoad != null)
            DiagnosticsRamLoad.Text = isVi
                ? $"Tỷ lệ tải RAM: {result.Ram.MemoryLoadPercentage}%"
                : $"Memory Load: {result.Ram.MemoryLoadPercentage}%";
        if (DiagnosticsRamProgress != null)
            DiagnosticsRamProgress.Value = Math.Clamp(result.Ram.MemoryLoadPercentage, 0, 100);

        if (DiagnosticsGpuName != null)
            DiagnosticsGpuName.Text = result.Gpus.Count > 0 ? result.Gpus[0].Name : "Standard Video Adapter";
        if (DiagnosticsGpuVram != null)
        {
            if (result.Gpus.Count > 0 && result.Gpus[0].VramBytes > 0)
                DiagnosticsGpuVram.Text = isVi ? $"VRAM: {result.Gpus[0].VramGigabytes:F1} GB" : $"VRAM: {result.Gpus[0].VramGigabytes:F1} GB Dedicated";
            else
                DiagnosticsGpuVram.Text = isVi ? "Bộ nhớ chia sẻ (Shared Memory)" : "Shared System Memory";
        }
        if (DiagnosticsGpuDriver != null)
            DiagnosticsGpuDriver.Text = result.Gpus.Count > 0 ? $"Driver: {result.Gpus[0].DriverVersion}" : "Driver: N/A";

        if (DiagnosticsOsVersion != null)
            DiagnosticsOsVersion.Text = $"{result.Os.ProductName} {result.Os.DisplayVersion} (Build {result.Os.BuildNumber})";
        if (DiagnosticsAdminStatus != null)
            DiagnosticsAdminStatus.Text = result.Os.IsAdministrator
                ? (isVi ? "Administrator (Đầy đủ quyền quản trị)" : "Administrator (Elevated)")
                : (isVi ? "Standard User (Chưa cấp quyền quản trị)" : "Standard User (Non-elevated)");
        if (DiagnosticsStorageInfo != null)
            DiagnosticsStorageInfo.Text = isVi
                ? $"Ổ {result.Storage.DriveName} Trống {result.Storage.FreeGigabytes:F1} GB / {result.Storage.TotalGigabytes:F1} GB ({result.Storage.FreePercentage:F1}%)"
                : $"Drive {result.Storage.DriveName} Free {result.Storage.FreeGigabytes:F1} GB / {result.Storage.TotalGigabytes:F1} GB ({result.Storage.FreePercentage:F1}%)";
        if (DiagnosticsAudioWebViewInfo != null)
        {
            string mic = result.Audio.HasInputDevice ? (isVi ? "Mic: Có" : "Mic: Yes") : (isVi ? "Mic: Không" : "Mic: No");
            string spk = result.Audio.HasOutputDevice ? (isVi ? "Loa: Có" : "Speakers: Yes") : (isVi ? "Loa: Không" : "Speakers: No");
            string wv2 = !string.IsNullOrWhiteSpace(result.WebView2Version) ? "WebView2: OK" : "WebView2: Missing";
            DiagnosticsAudioWebViewInfo.Text = $"{mic} | {spk} | {wv2}";
        }

        // Feature Matrix
        UpdateFeatureMatrixUi(result, isVi);

        // Recommendations
        UpdateRecommendationsUi(result);
    }

    private void UpdateFeatureMatrixUi(HardwareDiagnosticsResult result, bool isVi)
    {
        var successBrush = (SolidColorBrush)FindResource("SuccessBrush");
        var successBgBrush = (SolidColorBrush)FindResource("SuccessBgBrush");
        var warningBrush = (SolidColorBrush)FindResource("WarningBrush");
        var warningBgBrush = (SolidColorBrush)FindResource("WarningBgBrush");

        // 1. WDA
        var wda = result.FeatureMatrix.FirstOrDefault(f => f.FeatureKey == "Diagnostics.FeatureWda");
        if (wda != null)
        {
            if (TxtWdaDetail != null) TxtWdaDetail.Text = wda.Detail;
            if (TxtWdaStatus != null)
            {
                TxtWdaStatus.Text = wda.Tier == ReadinessTier.Optimal ? (isVi ? "Tối ưu" : "Optimal") : (isVi ? "Lưu ý" : "Notice");
                TxtWdaStatus.Foreground = wda.Tier == ReadinessTier.Optimal ? successBrush : warningBrush;
            }
            if (BadgeWda != null)
            {
                BadgeWda.Background = wda.Tier == ReadinessTier.Optimal ? successBgBrush : warningBgBrush;
                BadgeWda.BorderBrush = wda.Tier == ReadinessTier.Optimal ? successBrush : warningBrush;
            }
        }

        // 2. Captions
        var captions = result.FeatureMatrix.FirstOrDefault(f => f.FeatureKey == "Diagnostics.FeatureCaptions");
        if (captions != null)
        {
            if (TxtCaptionsDetail != null) TxtCaptionsDetail.Text = captions.Detail;
            if (TxtCaptionsStatus != null)
            {
                TxtCaptionsStatus.Text = captions.Tier == ReadinessTier.Optimal ? (isVi ? "Tối ưu" : "Optimal") : (isVi ? "Lưu ý" : "Notice");
                TxtCaptionsStatus.Foreground = captions.Tier == ReadinessTier.Optimal ? successBrush : warningBrush;
            }
            if (BadgeCaptions != null)
            {
                BadgeCaptions.Background = captions.Tier == ReadinessTier.Optimal ? successBgBrush : warningBgBrush;
                BadgeCaptions.BorderBrush = captions.Tier == ReadinessTier.Optimal ? successBrush : warningBrush;
            }
        }

        // 3. Virtual Desktop
        var vd = result.FeatureMatrix.FirstOrDefault(f => f.FeatureKey == "Diagnostics.FeatureVd");
        if (vd != null)
        {
            if (TxtVdDetail != null) TxtVdDetail.Text = vd.Detail;
            if (TxtVdStatus != null)
            {
                TxtVdStatus.Text = vd.Tier == ReadinessTier.Optimal ? (isVi ? "Tối ưu" : "Optimal") : (isVi ? "Dự phòng" : "Fallback");
                TxtVdStatus.Foreground = vd.Tier == ReadinessTier.Optimal ? successBrush : warningBrush;
            }
            if (BadgeVd != null)
            {
                BadgeVd.Background = vd.Tier == ReadinessTier.Optimal ? successBgBrush : warningBgBrush;
                BadgeVd.BorderBrush = vd.Tier == ReadinessTier.Optimal ? successBrush : warningBrush;
            }
        }

        // 4. AI Traffic
        var traffic = result.FeatureMatrix.FirstOrDefault(f => f.FeatureKey == "Diagnostics.FeatureAiTraffic");
        if (traffic != null)
        {
            if (TxtAiTrafficDetail != null) TxtAiTrafficDetail.Text = traffic.Detail;
            if (TxtAiTrafficStatus != null)
            {
                TxtAiTrafficStatus.Text = traffic.Tier == ReadinessTier.Optimal ? (isVi ? "Đầy đủ quyền" : "Admin Active") : (isVi ? "Người dùng thường" : "User Mode");
                TxtAiTrafficStatus.Foreground = traffic.Tier == ReadinessTier.Optimal ? successBrush : warningBrush;
            }
            if (BadgeAiTraffic != null)
            {
                BadgeAiTraffic.Background = traffic.Tier == ReadinessTier.Optimal ? successBgBrush : warningBgBrush;
                BadgeAiTraffic.BorderBrush = traffic.Tier == ReadinessTier.Optimal ? successBrush : warningBrush;
            }
        }

        // 5. Browser
        var browser = result.FeatureMatrix.FirstOrDefault(f => f.FeatureKey == "Diagnostics.FeatureBrowser");
        if (browser != null)
        {
            if (TxtBrowserDetail != null) TxtBrowserDetail.Text = browser.Detail;
            if (TxtBrowserStatus != null)
            {
                TxtBrowserStatus.Text = browser.Tier == ReadinessTier.Optimal ? (isVi ? "Sẵn sàng" : "Ready") : (isVi ? "Chưa cài" : "Missing");
                TxtBrowserStatus.Foreground = browser.Tier == ReadinessTier.Optimal ? successBrush : warningBrush;
            }
            if (BadgeBrowser != null)
            {
                BadgeBrowser.Background = browser.Tier == ReadinessTier.Optimal ? successBgBrush : warningBgBrush;
                BadgeBrowser.BorderBrush = browser.Tier == ReadinessTier.Optimal ? successBrush : warningBrush;
            }
        }
    }

    private void UpdateRecommendationsUi(HardwareDiagnosticsResult result)
    {
        if (DiagnosticsRecommendationsPanel == null) return;
        DiagnosticsRecommendationsPanel.Children.Clear();

        var cardSurfaceBrush = (System.Windows.Media.Brush)FindResource("CardSurfaceBrush");
        var borderBrush2 = (System.Windows.Media.Brush)FindResource("BorderBrush2");
        var accentBrush = (System.Windows.Media.Brush)FindResource("AccentBrush");
        var textBrush = (System.Windows.Media.Brush)FindResource("TextBrush");

        foreach (var tip in result.Recommendations)
        {
            var itemBorder = new Border
            {
                Background = cardSurfaceBrush,
                BorderBrush = borderBrush2,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var dot = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = accentBrush,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(dot, 0);

            var textBlock = new TextBlock
            {
                Text = tip,
                FontSize = 11,
                Foreground = textBrush,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                LineHeight = 16
            };
            Grid.SetColumn(textBlock, 1);

            grid.Children.Add(dot);
            grid.Children.Add(textBlock);

            itemBorder.Child = grid;
            DiagnosticsRecommendationsPanel.Children.Add(itemBorder);
        }
    }

    internal void RefreshDiagnosticsLanguage()
    {
        if (_latestDiagnostics != null)
        {
            bool vdAvailable = _vdService?.IsAvailable ?? false;

            _latestDiagnostics = HardwareDiagnosticsService.Instance.EvaluateReadiness(
                _latestDiagnostics.Timestamp,
                _latestDiagnostics.Cpu,
                _latestDiagnostics.Ram,
                _latestDiagnostics.Gpus,
                _latestDiagnostics.Os,
                _latestDiagnostics.Storage,
                _latestDiagnostics.Audio,
                _latestDiagnostics.WebView2Version,
                vdAvailable);

            UpdateDiagnosticsUi(_latestDiagnostics);
        }
    }

    private async void BtnRefreshDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        await LoadHardwareDiagnosticsAsync(force: true);
        ShowDiagnosticsToast(LocalizationService.GetString("Diagnostics.RefreshedToast", "Hardware specifications and readiness assessment refreshed!"));
    }

    private void BtnCopyDiagnosticsReport_Click(object sender, RoutedEventArgs e)
    {
        if (_latestDiagnostics == null)
            _latestDiagnostics = HardwareDiagnosticsService.Instance.CollectHardwareInfo();

        var text = HardwareDiagnosticsService.Instance.GenerateReportText(_latestDiagnostics);
        bool copied = false;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
                copied = true;
                break;
            }
            catch
            {
                System.Threading.Thread.Sleep(50);
            }
        }

        if (copied)
        {
            ShowDiagnosticsToast(LocalizationService.GetString("Diagnostics.ReportCopiedToast", "Hardware diagnostic report copied to clipboard successfully!"));
        }
        else
        {
            UpdateStatus(LocalizationService.GetString("Diagnostics.ReportCopyFailed", "Could not access clipboard. Please try again."), false);
        }
    }

    private void ShowDiagnosticsToast(string message)
    {
        if (DiagnosticsToast != null && TxtDiagnosticsToast != null)
        {
            TxtDiagnosticsToast.Text = message;
            DiagnosticsToast.Visibility = Visibility.Visible;
            _diagnosticsToastTimer?.Stop();
            _diagnosticsToastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _diagnosticsToastTimer.Tick += (s, args) =>
            {
                _diagnosticsToastTimer?.Stop();
                DiagnosticsToast.Visibility = Visibility.Collapsed;
            };
            _diagnosticsToastTimer.Start();
        }

        ShowToast(message, ToastType.Success);
    }
}
