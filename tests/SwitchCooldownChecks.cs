using System;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using StreamGuard.Localization;
using StreamGuard.Services;

namespace StreamGuard.Tests;

internal static class SwitchCooldownChecks
{
    internal static void Run(Action<bool, string> check)
    {
        TestCooldownPerSwitchAndExpiration(check);
        TestFindFeatureSwitchFiltering(check);
        TestLocalizationKeys(check);
    }

    private static void TestCooldownPerSwitchAndExpiration(Action<bool, string> check)
    {
        long now = 10_000;
        var service = new SwitchCooldownService(cooldownMs: 1200, nowProvider: () => now);
        var switchA = new CheckBox { Name = "SwitchA" };
        var switchB = new CheckBox { Name = "SwitchB" };

        check(service.TryAcquireToggle(switchA, out var rem1) && rem1 == 0,
            "First toggle on switchA is allowed immediately");

        now += 400;
        check(!service.TryAcquireToggle(switchA, out var rem2) && rem2 >= 0.7 && rem2 <= 0.9,
            $"Rapid toggle on switchA at +400ms is blocked with remaining ~0.8s (actual={rem2:0.00})");

        now += 50;
        check(service.TryAcquireToggle(switchB, out var remB) && remB == 0,
            "Toggling a different switchB is not blocked by switchA's cooldown");

        now += 350; // total +800ms since switchA's first toggle
        check(!service.TryAcquireToggle(switchA, out var rem3) && rem3 >= 0.3 && rem3 <= 0.5,
            $"Rejected toggle at +800ms remains blocked without resetting cooldown (actual={rem3:0.00})");

        now += 400; // total +1200ms since switchA's first toggle
        check(service.TryAcquireToggle(switchA, out var rem4) && rem4 == 0,
            "Toggle on switchA succeeds once full 1200ms cooldown elapses");
    }

    private static void TestFindFeatureSwitchFiltering(Action<bool, string> check)
    {
        var enabledCheckBox = new CheckBox { IsEnabled = true };
        var childText = new TextBlock { Text = "Label" };
        enabledCheckBox.Content = childText;
        check(ReferenceEquals(SwitchCooldownService.FindFeatureSwitch(enabledCheckBox), enabledCheckBox),
            "FindFeatureSwitch detects enabled CheckBox");
        check(ReferenceEquals(SwitchCooldownService.FindFeatureSwitch(childText), enabledCheckBox),
            "FindFeatureSwitch walks logical/visual parent to find CheckBox");

        var disabledCheckBox = new CheckBox { IsEnabled = false };
        check(SwitchCooldownService.FindFeatureSwitch(disabledCheckBox) == null,
            "FindFeatureSwitch ignores disabled CheckBox");

        var selectorToggle = new ToggleButton { Name = "SelectorToggle", IsEnabled = true };
        check(SwitchCooldownService.FindFeatureSwitch(selectorToggle) == null,
            "FindFeatureSwitch ignores ComboBox internal SelectorToggle");

        var featureToggle = new ToggleButton { Name = "ToggleBrowserAntiBlur", IsEnabled = true };
        check(ReferenceEquals(SwitchCooldownService.FindFeatureSwitch(featureToggle), featureToggle),
            "FindFeatureSwitch detects named feature ToggleButton");
    }

    private static void TestLocalizationKeys(Action<bool, string> check)
    {
        var l = LocalizationService.Current;
        l.SetLanguage("en");
        var en = l["Switch.CooldownWarning"];
        check(!string.IsNullOrWhiteSpace(en) && en != "Switch.CooldownWarning" && en.Contains("{0:0.0}"),
            $"English Switch.CooldownWarning exists: {en}");

        l.SetLanguage("vi");
        var vi = l["Switch.CooldownWarning"];
        check(!string.IsNullOrWhiteSpace(vi) && vi != "Switch.CooldownWarning" && vi.Contains("{0:0.0}"),
            $"Vietnamese Switch.CooldownWarning exists: {vi}");

        check(en != vi, "English and Vietnamese Switch.CooldownWarning translations differ");
    }
}
