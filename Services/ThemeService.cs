using System;
using System.Windows;
using Application = System.Windows.Application;

namespace StreamGuard.Services;

public sealed class ThemeService
{
    public const string DarkMode = "Dark";
    public const string LightMode = "Light";

    private static readonly string AssemblyName =
        Uri.EscapeDataString(typeof(ThemeService).Assembly.GetName().Name ?? "Driver Booster");

    private static readonly Uri DarkThemeUri =
        new($"pack://application:,,,/{AssemblyName};component/Themes/DarkTheme.xaml", UriKind.Absolute);

    private static readonly Uri LightThemeUri =
        new($"pack://application:,,,/{AssemblyName};component/Themes/LightTheme.xaml", UriKind.Absolute);

    public static ThemeService Instance { get; } = new();

    public string CurrentTheme { get; private set; } = DarkMode;
    public bool IsDark => string.Equals(CurrentTheme, DarkMode, StringComparison.OrdinalIgnoreCase);

    public event Action<string>? ThemeChanged;

    public static string NormalizeTheme(string? theme)
    {
        if (string.Equals(theme?.Trim(), LightMode, StringComparison.OrdinalIgnoreCase))
            return LightMode;
        return DarkMode;
    }

    public string ApplyTheme(string? theme)
    {
        var normalized = NormalizeTheme(theme);
        CurrentTheme = normalized;

        var app = Application.Current;
        if (app?.Resources?.MergedDictionaries != null)
        {
            var merged = app.Resources.MergedDictionaries;
            for (int i = merged.Count - 1; i >= 0; i--)
            {
                var src = merged[i].Source?.OriginalString;
                if (src != null && (src.Contains("Themes/DarkTheme.xaml", StringComparison.OrdinalIgnoreCase) ||
                                    src.Contains("Themes/LightTheme.xaml", StringComparison.OrdinalIgnoreCase)))
                {
                    merged.RemoveAt(i);
                }
            }

            var targetUri = normalized == LightMode ? LightThemeUri : DarkThemeUri;
            merged.Insert(0, new ResourceDictionary { Source = targetUri });
        }

        ThemeChanged?.Invoke(normalized);
        return normalized;
    }

    public string ToggleTheme()
    {
        return ApplyTheme(IsDark ? LightMode : DarkMode);
    }
}
