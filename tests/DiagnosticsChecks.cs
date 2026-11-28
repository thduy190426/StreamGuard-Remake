using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using StreamGuard;
using StreamGuard.Localization;
using StreamGuard.Services;

namespace StreamGuard.Tests;

internal static class DiagnosticsChecks
{
    internal static void Run(Action<bool, string> check)
    {
        TestHardwareCollection(check);
        TestReadinessEvaluationMatrix(check);
        TestReportGeneration(check);
    }

    private static void TestHardwareCollection(Action<bool, string> check)
    {
        var service = HardwareDiagnosticsService.Instance;
        var result = service.CollectHardwareInfoAsync().GetAwaiter().GetResult();

        check(result != null, "HardwareDiagnosticsService returns non-null result");
        check(!string.IsNullOrWhiteSpace(result!.Cpu.Name), "CPU Name is collected: " + result.Cpu.Name);
        check(result.Cpu.Cores > 0, "CPU physical cores count is positive: " + result.Cpu.Cores);
        check(result.Cpu.LogicalProcessors > 0, "CPU logical processors count is positive: " + result.Cpu.LogicalProcessors);
        check(!string.IsNullOrWhiteSpace(result.Cpu.Architecture), "CPU architecture is collected: " + result.Cpu.Architecture);

        check(result.Ram.TotalBytes > 0, "Total RAM bytes > 0: " + result.Ram.TotalGigabytes.ToString("F1") + " GB");
        check(result.Ram.AvailableBytes > 0, "Available RAM bytes > 0");
        check(result.Ram.MemoryLoadPercentage <= 100, "Memory load percentage is bounded: " + result.Ram.MemoryLoadPercentage + "%");

        check(result.Gpus.Count > 0, "At least one GPU/adapter is detected: " + result.Gpus[0].Name);
        check(!string.IsNullOrWhiteSpace(result.Os.ProductName), "OS product name is collected: " + result.Os.ProductName);
        check(result.Os.BuildNumber > 0, "OS build number is positive: " + result.Os.BuildNumber);

        check(result.Storage.TotalBytes > 0, "System drive storage size > 0: " + result.Storage.TotalGigabytes.ToString("F1") + " GB");
        check(result.Storage.FreePercentage <= 100.0 && result.Storage.FreePercentage >= 0.0, "Storage free percentage is bounded");

        check(result.CompatibilityScore >= 0 && result.CompatibilityScore <= 100, "Compatibility score is between 0 and 100: " + result.CompatibilityScore);
        check(result.FeatureMatrix.Count == 5, "Feature readiness matrix contains exactly 5 core features");
        check(result.Recommendations.Count > 0, "Recommendations list contains at least one actionable tip");
    }

    private static void TestReadinessEvaluationMatrix(Action<bool, string> check)
    {
        var service = HardwareDiagnosticsService.Instance;

        // 1. Optimal Machine Scenario
        var optimalCpu = new CpuDiagnostics("Intel Core i7-13700H", 14, 16, "X64", true, true);
        var optimalRam = new RamDiagnostics(16UL * 1024 * 1024 * 1024, 10UL * 1024 * 1024 * 1024, 38);
        var optimalGpus = new[] { new GpuDiagnostics("NVIDIA RTX 4060", 8UL * 1024 * 1024 * 1024, "32.0.15.5123") };
        var optimalOs = new OsDiagnostics("Windows 11 Pro", "24H2", 26200, true, true);
        var optimalStorage = new StorageDiagnostics("C:\\", 512UL * 1024 * 1024 * 1024, 200UL * 1024 * 1024 * 1024);
        var optimalAudio = new AudioDiagnostics(true, true, 2, 2);

        var optimalResult = service.EvaluateReadiness(
            DateTime.Now,
            optimalCpu,
            optimalRam,
            optimalGpus,
            optimalOs,
            optimalStorage,
            optimalAudio,
            "131.0.2903.40",
            true
        );

        check(optimalResult.CompatibilityScore == 100, "Optimal machine scores 100/100 points: " + optimalResult.CompatibilityScore);
        check(optimalResult.OverallTier == ReadinessTier.Optimal, "Optimal machine classified as Optimal tier");
        check(optimalResult.FeatureMatrix.All(f => f.Tier == ReadinessTier.Optimal), "All features are Optimal on optimal machine");

        // 2. Non-admin scenario: downgraded from Optimal to Good due to FeatureAiTraffic Warning
        var nonAdminOs = new OsDiagnostics("Windows 11 Pro", "24H2", 26200, true, false);
        var nonAdminResult = service.EvaluateReadiness(
            DateTime.Now,
            optimalCpu,
            optimalRam,
            optimalGpus,
            nonAdminOs,
            optimalStorage,
            optimalAudio,
            "131.0.2903.40",
            true
        );
        check(nonAdminResult.CompatibilityScore == 97, "Non-admin machine scores 97/100 points: " + nonAdminResult.CompatibilityScore);
        check(nonAdminResult.OverallTier == ReadinessTier.Good, "Non-admin machine classified as Good tier due to proxy warning");
        check(nonAdminResult.FeatureMatrix.Any(f => f.FeatureKey == "Diagnostics.FeatureAiTraffic" && f.Tier == ReadinessTier.Warning),
            "FeatureAiTraffic flagged as Warning for non-admin");
        check(nonAdminResult.Recommendations.Any(r => r.Contains("Administrator") || r.Contains("administrator")),
            "Recommendation suggests running as administrator");

        // 3. High memory and low storage edge case recommendations
        var heavyRam = new RamDiagnostics(8UL * 1024 * 1024 * 1024, 800UL * 1024 * 1024, 90);
        var lowDisk = new StorageDiagnostics("C:\\", 128UL * 1024 * 1024 * 1024, 3UL * 1024 * 1024 * 1024);
        var heavyResult = service.EvaluateReadiness(
            DateTime.Now,
            optimalCpu,
            heavyRam,
            optimalGpus,
            optimalOs,
            lowDisk,
            optimalAudio,
            "131.0.2903.40",
            true
        );
        check(heavyResult.Recommendations.Any(r => r.Contains("90%")), "High memory load generates specific percentage tip");
        check(heavyResult.Recommendations.Any(r => r.Contains("3.0 GB")), "Low disk space generates free space warning tip");

        // 4. Legacy / Constrained Machine Scenario
        var lowCpu = new CpuDiagnostics("Intel Core 2 Duo", 2, 2, "X86", false, false);
        var lowRam = new RamDiagnostics(3UL * 1024 * 1024 * 1024, 500UL * 1024 * 1024, 85);
        var lowGpus = new[] { new GpuDiagnostics("Generic Display", 0, "1.0") };
        var lowOs = new OsDiagnostics("Windows 10 Home", "1507", 10240, false, false);
        var lowStorage = new StorageDiagnostics("C:\\", 120UL * 1024 * 1024 * 1024, 1UL * 1024 * 1024 * 1024);
        var lowAudio = new AudioDiagnostics(false, true, 0, 1);

        var lowResult = service.EvaluateReadiness(
            DateTime.Now,
            lowCpu,
            lowRam,
            lowGpus,
            lowOs,
            lowStorage,
            lowAudio,
            null, // No WebView2
            false // No Virtual Desktop COM
        );

        check(lowResult.CompatibilityScore <= 55, "Constrained machine receives bounded low score: " + lowResult.CompatibilityScore);
        check(lowResult.OverallTier is ReadinessTier.Warning or ReadinessTier.Insufficient, "Constrained machine tier is Warning or Insufficient");

        var wda = lowResult.FeatureMatrix.FirstOrDefault(f => f.FeatureKey == "Diagnostics.FeatureWda");
        check(wda != null && wda.Tier != ReadinessTier.Optimal, "WDA is not Optimal on legacy 32-bit build 10240");

        var captions = lowResult.FeatureMatrix.FirstOrDefault(f => f.FeatureKey == "Diagnostics.FeatureCaptions");
        check(captions != null && captions.Tier == ReadinessTier.Warning, "Captions feature warns when mic is missing and CPU lacks AVX2");

        var traffic = lowResult.FeatureMatrix.FirstOrDefault(f => f.FeatureKey == "Diagnostics.FeatureAiTraffic");
        check(traffic != null && traffic.Tier == ReadinessTier.Warning, "AI Traffic warns when non-administrator");

        var browser = lowResult.FeatureMatrix.FirstOrDefault(f => f.FeatureKey == "Diagnostics.FeatureBrowser");
        check(browser != null && browser.Tier == ReadinessTier.Warning, "Browser warns when WebView2 is missing");
    }

    private static void TestReportGeneration(Action<bool, string> check)
    {
        var service = HardwareDiagnosticsService.Instance;
        var result = service.CollectHardwareInfo();

        var viReport = service.GenerateReportText(result, "vi");
        check(!string.IsNullOrWhiteSpace(viReport), "Vietnamese report is generated");
        check(viReport.Contains("DRIVER BOOSTER - BÁO CÁO PHẦN CỨNG"), "Vietnamese report header present");
        check(viReport.Contains("[THÔNG SỐ PHẦN CỨNG VÀ HỆ THỐNG]"), "Vietnamese specifications section present");
        check(viReport.Contains("[MA TRẬN ĐÁNH GIÁ TÍNH NĂNG]"), "Vietnamese feature matrix section present");
        check(viReport.Contains("[KHUYẾN NGHỊ TỐI ƯU HỆ THỐNG]"), "Vietnamese recommendations section present");
        check(viReport.Contains(result.Cpu.Name), "Report contains CPU model name");

        var enReport = service.GenerateReportText(result, "en");
        check(!string.IsNullOrWhiteSpace(enReport), "English report is generated");
        check(enReport.Contains("DRIVER BOOSTER - HARDWARE & SYSTEM COMPATIBILITY REPORT"), "English report header present");
        check(enReport.Contains("[SYSTEM SPECIFICATIONS]"), "English specifications section present");
        check(enReport.Contains("[FEATURE READINESS MATRIX]"), "English feature matrix section present");
        check(enReport.Contains("[SMART OPTIMIZATION RECOMMENDATIONS]"), "English recommendations section present");

        // Verify all FeatureMatrix items translate into Vietnamese when Language == "vi"
        var prevLang = LocalizationService.Current.Language;
        try
        {
            LocalizationService.Current.SetLanguage("vi");
            var viEvaluated = service.EvaluateReadiness(
                result.Timestamp, result.Cpu, result.Ram, result.Gpus, result.Os, result.Storage, result.Audio,
                result.WebView2Version, true);
            check(viEvaluated.FeatureMatrix.Count == 5, "Evaluated FeatureMatrix contains 5 items");
            check(viEvaluated.FeatureMatrix.All(f => !f.FeatureName.Contains("Window Stealth") && !f.Detail.Contains("provides full stealth")),
                "Vietnamese FeatureMatrix translates all FeatureName and Detail strings into Vietnamese");
        }
        finally
        {
            LocalizationService.Current.SetLanguage(prevLang);
        }
    }

    internal static void RunUiChecks(MainWindow window, Action<bool, string> check)
    {
        var tab = (RadioButton)window.FindName("TabDiagnostics");
        check(tab != null, "TabDiagnostics RadioButton exists on MainWindow");
        check(tab?.ToolTip != null, "TabDiagnostics has a ToolTip");
        check(!string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(tab!)),
            "TabDiagnostics has an AutomationProperties Name");

        var tabText = (TextBlock)window.FindName("TabDiagnosticsText");
        check(tabText != null, "TabDiagnosticsText TextBlock exists");

        var view = (Grid)window.FindName("ViewDiagnostics");
        check(view != null, "ViewDiagnostics Grid exists");

        var btnRefresh = (Button)window.FindName("BtnRefreshDiagnostics");
        check(btnRefresh != null, "BtnRefreshDiagnostics Button exists");

        var btnCopy = (Button)window.FindName("BtnCopyDiagnosticsReport");
        check(btnCopy != null, "BtnCopyDiagnosticsReport Button exists");

        var scoreText = (TextBlock)window.FindName("DiagnosticsScoreText");
        check(scoreText != null, "DiagnosticsScoreText exists");

        var tierBadge = (Border)window.FindName("DiagnosticsTierBadge");
        check(tierBadge != null, "DiagnosticsTierBadge exists");

        var tierText = (TextBlock)window.FindName("DiagnosticsTierText");
        check(tierText != null, "DiagnosticsTierText exists");

        var cpuName = (TextBlock)window.FindName("DiagnosticsCpuName");
        check(cpuName != null, "DiagnosticsCpuName exists");

        var ramDetails = (TextBlock)window.FindName("DiagnosticsRamDetails");
        check(ramDetails != null, "DiagnosticsRamDetails exists");

        var gpuName = (TextBlock)window.FindName("DiagnosticsGpuName");
        check(gpuName != null, "DiagnosticsGpuName exists");

        var osVersion = (TextBlock)window.FindName("DiagnosticsOsVersion");
        check(osVersion != null, "DiagnosticsOsVersion exists");

        var badgeWda = (Border)window.FindName("BadgeWda");
        check(badgeWda != null, "BadgeWda exists");

        var badgeCaptions = (Border)window.FindName("BadgeCaptions");
        check(badgeCaptions != null, "BadgeCaptions exists");

        var badgeVd = (Border)window.FindName("BadgeVd");
        check(badgeVd != null, "BadgeVd exists");

        var badgeAiTraffic = (Border)window.FindName("BadgeAiTraffic");
        check(badgeAiTraffic != null, "BadgeAiTraffic exists");

        var badgeBrowser = (Border)window.FindName("BadgeBrowser");
        check(badgeBrowser != null, "BadgeBrowser exists");

        var recPanel = (StackPanel)window.FindName("DiagnosticsRecommendationsPanel");
        check(recPanel != null, "DiagnosticsRecommendationsPanel exists");

        var toast = (Border)window.FindName("DiagnosticsToast");
        check(toast != null && toast.Visibility == Visibility.Collapsed, "DiagnosticsToast exists and is initially Collapsed");

        // Test tab switching
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var switchToTabMethod = typeof(MainWindow).GetMethod("SwitchToTab", flags);
        check(switchToTabMethod != null, "SwitchToTab method exists");

        switchToTabMethod!.Invoke(window, new object?[] { view });
        check(view!.Visibility == Visibility.Visible, "Switching to TabDiagnostics makes ViewDiagnostics Visible");
        var viewProtected = (Grid)window.FindName("ViewProtected");
        check(viewProtected != null && viewProtected.Visibility == Visibility.Collapsed, "Switching to TabDiagnostics hides ViewProtected");

        // Test RefreshDiagnosticsLanguage
        var refreshDiagMethod = typeof(MainWindow).GetMethod("RefreshDiagnosticsLanguage", flags);
        check(refreshDiagMethod != null, "RefreshDiagnosticsLanguage method exists");
        refreshDiagMethod!.Invoke(window, null);

        // Test Tab Checked event logic
        tab!.IsChecked = true;
        check(view!.Visibility == Visibility.Visible, "Selecting TabDiagnostics keeps ViewDiagnostics Visible");
    }
}
