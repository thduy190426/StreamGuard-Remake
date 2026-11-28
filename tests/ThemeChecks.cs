using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using StreamGuard.Models;
using StreamGuard.Services;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;

namespace StreamGuard.Tests;

public static class ThemeChecks
{
    private static readonly string[] RequiredBrushKeys =
    {
        "BgBrush", "SidebarBgBrush", "WindowBorderBrush",
        "CardBrush", "CardHoverBrush", "CardActiveBrush",
        "CardSurfaceBrush", "CardSurfaceSubtleBrush", "CardSurfaceElevatedBrush",
        "BorderBrush2", "BorderHoverBrush", "BorderFocusBrush",
        "InputBgBrush", "InputFocusBgBrush",
        "PopupBgBrush", "PopupHoverBrush", "PopupSelectedBrush",
        "ChipBgBrush", "ChipBorderBrush",
        "SectionBadgeBgBrush", "SectionBadgeBorderBrush",
        "BadgePillBgBrush", "BadgePillBorderBrush",
        "IndigoCardBgBrush", "IndigoCardBorderBrush",
        "ToggleTrackOffBgBrush", "ToggleTrackOffBorderBrush", "ToggleThumbOffBrush",
        "CheckBoxBgBrush", "NavTabHoverBgBrush", "NavTabSelectedTextBrush",
        "GhostHoverBgBrush", "GhostPressedBgBrush",
        "ListItemHoverBgBrush", "ListItemSelectedBgBrush",
        "ScrollThumbBgBrush", "ScrollThumbHoverBrush", "MeterTrackBgBrush",
        "AccentBrush", "AccentDimBrush", "AccentNeonBrush", "AccentCyanBrush", "AccentTextBrush",
        "AccentGradient", "ShieldActiveGradient", "DangerGradient",
        "SuccessBrush", "SuccessTextBrush", "SuccessBgBrush", "SuccessBgSubtleBrush", "SuccessBorderBrush",
        "DangerBrush", "DangerTextBrush", "DangerBgBrush", "DangerBgSubtleBrush", "DangerBorderBrush", "DangerHoverBgBrush",
        "WarningBrush", "WarningTextBrush", "WarningBgBrush", "WarningBgSubtleBrush", "WarningBorderBrush",
        "InfoBrush", "InfoTextBrush", "BlueBgSubtleBrush", "BlueBorderBrush",
        "CyanBgSubtleBrush", "CyanBorderBrush",
        "AccentPurpleBrush", "PurpleBgSubtleBrush", "PurpleBorderBrush",
        "AccentTealBrush", "TealBgSubtleBrush", "TealBorderBrush",
        "IndigoBgSubtleBrush", "IndigoBorderBrush",
        "TextBrush", "TextDimBrush", "TextMutedBrush",
        "ModalBackdropBrush", "ModalSurfaceBrush", "EditorCanvasBgBrush",
        "ToastTextBrush", "ToastCloseStrokeBrush", "ToastCloseHoverBgBrush", "ToastTrackBgBrush",
        "ToastSuccessBgBrush", "ToastSuccessBorderBrush",
        "ToastErrorBgBrush", "ToastErrorBorderBrush",
        "ToastWarningBgBrush", "ToastWarningBorderBrush",
        "ToastInfoBgBrush", "ToastInfoBorderBrush"
    };

    public static void Run(Action<bool, string> check, string tempDir)
    {
        // 1. Normalization checks
        check(ThemeService.NormalizeTheme(null) == ThemeService.DarkMode, "Null theme normalizes to Dark");
        check(ThemeService.NormalizeTheme("") == ThemeService.DarkMode, "Empty theme normalizes to Dark");
        check(ThemeService.NormalizeTheme("unknown") == ThemeService.DarkMode, "Unknown theme normalizes to Dark");
        check(ThemeService.NormalizeTheme("light") == ThemeService.LightMode, "Lowercase light normalizes to Light");
        check(ThemeService.NormalizeTheme("  LIGHT  ") == ThemeService.LightMode, "Trimmed uppercase LIGHT normalizes to Light");

        // 2. Pack URI ResourceDictionaries load & contain identical complete key sets
        var asmName = Uri.EscapeDataString(typeof(ThemeService).Assembly.GetName().Name ?? "Driver Booster");
        var darkDict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/{asmName};component/Themes/DarkTheme.xaml", UriKind.Absolute) };
        var lightDict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/{asmName};component/Themes/LightTheme.xaml", UriKind.Absolute) };

        foreach (var key in RequiredBrushKeys)
        {
            check(darkDict.Contains(key), $"DarkTheme.xaml missing key: {key}");
            check(lightDict.Contains(key), $"LightTheme.xaml missing key: {key}");
            check(darkDict[key] is WpfBrush, $"DarkTheme key is not a Brush: {key}");
            check(lightDict[key] is WpfBrush, $"LightTheme key is not a Brush: {key}");
        }

        // 3. Contrast luminance checks (WCAG clarity guarantees on Light and Dark surfaces)
        var darkBg = ((SolidColorBrush)darkDict["BgBrush"]).Color;
        var darkCard = ((SolidColorBrush)darkDict["CardBrush"]).Color;
        var darkText = ((SolidColorBrush)darkDict["TextBrush"]).Color;
        var darkDim = ((SolidColorBrush)darkDict["TextDimBrush"]).Color;
        check(ContrastRatio(darkText, darkBg) >= 10.0, "Dark mode primary text has >= 10:1 contrast on BgBrush");
        check(ContrastRatio(darkText, darkCard) >= 10.0, "Dark mode primary text has >= 10:1 contrast on CardBrush");
        check(ContrastRatio(darkDim, darkCard) >= 4.5, "Dark mode secondary text has >= 4.5:1 contrast on CardBrush");

        var lightBg = ((SolidColorBrush)lightDict["BgBrush"]).Color;
        var lightCard = ((SolidColorBrush)lightDict["CardBrush"]).Color;
        var lightText = ((SolidColorBrush)lightDict["TextBrush"]).Color;
        var lightDim = ((SolidColorBrush)lightDict["TextDimBrush"]).Color;
        var lightSuccessText = ((SolidColorBrush)lightDict["SuccessTextBrush"]).Color;
        var lightDangerText = ((SolidColorBrush)lightDict["DangerTextBrush"]).Color;
        var lightWarningText = ((SolidColorBrush)lightDict["WarningTextBrush"]).Color;
        check(RelativeLuminance(lightBg) > 0.75, "Light mode BgBrush is genuinely bright");
        check(ContrastRatio(lightText, lightBg) >= 10.0, "Light mode primary text has >= 10:1 contrast on BgBrush");
        check(ContrastRatio(lightText, lightCard) >= 12.0, "Light mode primary text has >= 12:1 contrast on CardBrush");
        check(ContrastRatio(lightDim, lightCard) >= 4.5, "Light mode secondary text has >= 4.5:1 contrast on CardBrush");
        check(ContrastRatio(lightSuccessText, lightCard) >= 4.5, "Light mode SuccessTextBrush has >= 4.5:1 contrast on CardBrush");
        check(ContrastRatio(lightDangerText, lightCard) >= 4.5, "Light mode DangerTextBrush has >= 4.5:1 contrast on CardBrush");
        check(ContrastRatio(lightWarningText, lightCard) >= 4.5, "Light mode WarningTextBrush has >= 4.5:1 contrast on CardBrush");

        // 4. ConfigService persistence for ThemeMode & EnableSystemTrayIcon
        var cfgPath = Path.Combine(tempDir, "theme-config.json");
        var cfg = new ConfigService(cfgPath);
        check(!cfg.EnableSystemTrayIcon, "EnableSystemTrayIcon defaults to false");
        cfg.Save(Array.Empty<GuardedApp>(), false, false, false, true, true, true, "vi", themeMode: ThemeService.LightMode, enableSystemTrayIcon: true);
        var cfgReloaded = new ConfigService(cfgPath);
        cfgReloaded.Load();
        check(cfgReloaded.ThemeMode == ThemeService.LightMode, "ConfigService persists ThemeMode=Light across Load");
        check(cfgReloaded.EnableSystemTrayIcon, "ConfigService persists EnableSystemTrayIcon=true across Load");
        var json = JObject.Parse(File.ReadAllText(cfgPath));
        check(json["ThemeMode"]?.Value<string>() == ThemeService.LightMode, "config.json stores ThemeMode=Light");
        check(json["EnableSystemTrayIcon"]?.Value<bool>() == true, "config.json stores EnableSystemTrayIcon=true");
    }

    public static void RunUiChecks(MainWindow window, string configPath, Action<bool, string> check)
    {
        var btnToggleTheme = window.FindName("BtnToggleTheme") as Button;
        var themeCombo = window.FindName("ThemeModeComboBox") as ComboBox;
        var toastText = window.TryFindResource("ToastTextBrush") as SolidColorBrush;
        var transitionOverlay = window.FindName("ThemeTransitionOverlay") as Grid;
        var transitionImage = window.FindName("ThemeTransitionImage") as System.Windows.Controls.Image;
        var transitionSheen = window.FindName("ThemeTransitionSheen") as Border;
        var iconThemeRotate = window.FindName("IconThemeRotate") as RotateTransform;
        var iconThemeScale = window.FindName("IconThemeScale") as ScaleTransform;
        var btnToggleTray = window.FindName("BtnToggleTray") as Button;
        var trayActiveDot = window.FindName("TrayActiveDot") as UIElement;
        var trayCheckBox = window.FindName("SystemTrayIconCheckBox") as CheckBox;
        var trayStatusText = window.FindName("SystemTrayIconStatusText") as TextBlock;

        check(btnToggleTheme != null, "TitleBar BtnToggleTheme exists");
        check(themeCombo != null, "Settings ThemeModeComboBox exists");
        check(toastText != null, "ToastTextBrush resolves in Application resources");
        check(transitionOverlay != null && transitionImage != null && transitionSheen != null, "Theme transition cross-fade overlay elements exist");
        check(iconThemeRotate != null && iconThemeScale != null, "Theme toggle icon transforms exist");
        check(btnToggleTray != null && trayActiveDot != null, "TitleBar BtnToggleTray and active indicator dot exist");
        check(trayCheckBox != null && trayStatusText != null, "Settings SystemTrayIconCheckBox and status label exist");

        // Default is Dark Mode & Tray Hidden
        check(ThemeService.Instance.CurrentTheme == ThemeService.DarkMode, "Initial runtime theme is Dark");
        check((string?)themeCombo!.SelectedValue == ThemeService.DarkMode, "Settings ThemeModeComboBox starts on Dark");
        check(trayCheckBox!.IsChecked == false && trayActiveDot!.Visibility == Visibility.Collapsed, "System tray icon defaults to hidden");
        var initialBg = ((SolidColorBrush)window.FindResource("BgBrush")).Color;
        check(RelativeLuminance(initialBg) < 0.1, "Initial MainWindow BgBrush is dark");

        // Switch to Light Mode via Settings ComboBox
        themeCombo.SelectedValue = ThemeService.LightMode;
        Pump();
        check(ThemeService.Instance.CurrentTheme == ThemeService.LightMode, "ComboBox switches ThemeService to Light");
        var lightBg = ((SolidColorBrush)window.FindResource("BgBrush")).Color;
        var lightText = ((SolidColorBrush)window.FindResource("TextBrush")).Color;
        check(RelativeLuminance(lightBg) > 0.75, "Live window BgBrush updates to Light palette");
        check(RelativeLuminance(lightText) < 0.1, "Live window TextBrush updates to dark ink on Light palette");
        check(JObject.Parse(File.ReadAllText(configPath))["ThemeMode"]?.Value<string>() == ThemeService.LightMode, "Light theme persists to config.json");

        // Toggle back to Dark Mode via TitleBar button click (triggers icon morph & transition guard safely)
        btnToggleTheme!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
        check(ThemeService.Instance.CurrentTheme == ThemeService.DarkMode, "TitleBar toggle button switches back to Dark");
        check((string?)themeCombo.SelectedValue == ThemeService.DarkMode, "Settings ComboBox stays synchronized after TitleBar toggle");
        var darkAgainBg = ((SolidColorBrush)window.FindResource("BgBrush")).Color;
        check(RelativeLuminance(darkAgainBg) < 0.1, "Live window BgBrush updates back to Dark palette");
        check(JObject.Parse(File.ReadAllText(configPath))["ThemeMode"]?.Value<string>() == ThemeService.DarkMode, "Dark theme persists back to config.json");

        // Verify cross-fade runner handles headless dispatcher execution cleanly without leaving stuck overlay
        typeof(MainWindow).GetMethod("RunThemeCrossFadeAnimation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(window, new object[] { 0 });
        Pump();
        check(transitionOverlay!.Visibility == Visibility.Collapsed, "ThemeTransitionOverlay remains cleanly collapsed in headless mode");

        // Verify System Tray on-demand toggle (TitleBar -> Settings sync -> NotifyIcon + Menu -> Hide)
        btnToggleTray!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
        var trayField = typeof(MainWindow).GetField("_trayIcon", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var trayIcon = trayField.GetValue(window) as System.Windows.Forms.NotifyIcon;
        check(trayCheckBox.IsChecked == true, "TitleBar tray button synchronizes Settings checkbox to checked");
        check(trayActiveDot!.Visibility == Visibility.Visible, "TitleBar TrayActiveDot turns visible when tray is active");
        check(trayIcon != null && trayIcon.Visible && trayIcon.Icon != null, "NotifyIcon is created, visible, and has an icon");
        check(trayIcon!.ContextMenuStrip != null && trayIcon.ContextMenuStrip.Items.Count == 8, "NotifyIcon context menu has all expected action items including Hide tray icon");
        check(JObject.Parse(File.ReadAllText(configPath))["EnableSystemTrayIcon"]?.Value<bool>() == true, "EnableSystemTrayIcon=true persists to config.json");

        // Hide tray icon cleanly via tray menu helper and verify persistence + disposal
        typeof(MainWindow).GetMethod("HideSystemTrayFromMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(window, null);
        Pump();
        check(trayCheckBox.IsChecked == false, "HideSystemTrayFromMenu unchecks Settings SystemTrayIconCheckBox");
        check(trayActiveDot.Visibility == Visibility.Collapsed, "HideSystemTrayFromMenu hides TitleBar TrayActiveDot");
        check(!trayIcon.Visible, "NotifyIcon becomes hidden after disabling");
        check(JObject.Parse(File.ReadAllText(configPath))["EnableSystemTrayIcon"]?.Value<bool>() == false, "EnableSystemTrayIcon=false persists to config.json");

        trayIcon.Dispose();
        trayField.SetValue(window, null);
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static double RelativeLuminance(WpfColor c)
    {
        static double Lin(byte channel)
        {
            double s = channel / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    private static double ContrastRatio(WpfColor fg, WpfColor bg)
    {
        double l1 = RelativeLuminance(fg);
        double l2 = RelativeLuminance(bg);
        if (l1 < l2) (l1, l2) = (l2, l1);
        return (l1 + 0.05) / (l2 + 0.05);
    }
}
