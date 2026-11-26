using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using Binding = System.Windows.Data.Binding;

namespace StreamGuard.Localization;

/// <summary>Presentation-only translations. Raw service messages and state stay unchanged.</summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    public sealed record Entry(string Key, string English, string Vietnamese, string? Source = null, int[]? TranslateArguments = null);
    private sealed record Pattern(Regex Regex, Entry Entry, int[] Indices);
    private readonly Dictionary<string, Entry> _keys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Entry> _exact = new(StringComparer.Ordinal);
    private readonly List<Pattern> _patterns = new();
    private string _language = "en";
    public static LocalizationService Current { get; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Language => _language;
    public string this[string key] => Render(_keys[key]);

    public static string GetString(string key, string fallback = "") =>
        Current._keys.TryGetValue(key, out var entry) ? Current.Render(entry) : fallback;

    private LocalizationService()
    {
        using var stream = typeof(LocalizationService).Assembly.GetManifestResourceStream("StreamGuard.Localization.Strings.json")
            ?? throw new InvalidOperationException("Missing translation catalog.");
        var entries = JsonSerializer.Deserialize<Entry[]>(stream) ?? throw new InvalidOperationException("Invalid translation catalog.");
        foreach (var entry in entries)
        {
            _keys.Add(entry.Key, entry);
            foreach (var source in new[] { entry.English, entry.Source }.OfType<string>().Distinct())
            {
                var fields = Regex.Matches(source, @"\{(\d+)\}");
                if (fields.Count == 0) { _exact.TryAdd(source, entry); continue; }
                var indices = fields.Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray();
                var pattern = "\\A";
                var position = 0;
                foreach (Match field in fields)
                {
                    pattern += Regex.Escape(source[position..field.Index]) + "(.*?)";
                    position = field.Index + field.Length;
                }
                pattern += Regex.Escape(source[position..]) + "\\z";
                _patterns.Add(new Pattern(new Regex(pattern, RegexOptions.Singleline | RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(100)), entry, indices));
            }
        }
        // Specific templates must precede generic wrappers such as "{0}. Total contained: {1}".
        _patterns.Sort((a, b) =>
        {
            var wrappers = b.Entry.Key.StartsWith("Detail.", StringComparison.Ordinal).CompareTo(
                a.Entry.Key.StartsWith("Detail.", StringComparison.Ordinal));
            return wrappers != 0 ? wrappers : b.Regex.ToString().Length.CompareTo(a.Regex.ToString().Length);
        });
    }

    public static string NormalizeLanguage(string? language) =>
        language?.Split('-')[0].Equals("vi", StringComparison.OrdinalIgnoreCase) == true ? "vi" : "en";

    public void SetLanguage(string? language)
    {
        var normalized = NormalizeLanguage(language);
        if (_language == normalized) return;
        _language = normalized;
        // UI culture only: number parsing, native API behavior, and service threads are unaffected.
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(normalized == "vi" ? "vi-VN" : "en-US");
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    private string Render(Entry entry) => Language == "vi" ? entry.Vietnamese : entry.English;

    public string Translate(string? text) => Translate(text ?? "", 0);
    private string Translate(string text, int depth)
    {
        if (_exact.TryGetValue(text, out var exact)) return Render(exact);
        if (depth > 4 || text.Length > 16384) return text;
        foreach (var pattern in _patterns)
        {
            Match match;
            try { match = pattern.Regex.Match(text); }
            catch (RegexMatchTimeoutException) { continue; }
            if (!match.Success) continue;
            var values = new Dictionary<int, string>();
            for (var i = 0; i < pattern.Indices.Length; i++)
                values[pattern.Indices[i]] = match.Groups[i + 1].Value;
            // Only diagnostic wrappers contain nested messages. App names/paths remain verbatim.
            foreach (var index in pattern.Entry.TranslateArguments ?? Array.Empty<int>())
                if (values.TryGetValue(index, out var detail)) values[index] = Translate(detail, depth + 1);
            return Regex.Replace(Render(pattern.Entry), @"\{(\d+)\}", m => values[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)]);
        }
        if (text.Contains("; ", StringComparison.Ordinal))
            return string.Join("; ", text.Split("; ").Select(part => Translate(part, depth + 1)));
        return text; // Native error details and application-generated names stay readable.
    }

    public static void BindText(DependencyObject target, DependencyProperty property, string rawText)
    {
        var binding = CreateBinding(new Binding { Source = rawText });
        BindingOperations.SetBinding(target, property, binding);
    }

    public static void BindKey(DependencyObject target, DependencyProperty property, string key)
    {
        var binding = CreateBinding(new Binding { Source = key });
        binding.ConverterParameter = "Key";
        BindingOperations.SetBinding(target, property, binding);
    }

    internal static MultiBinding CreateBinding(BindingBase textBinding)
    {
        var binding = new MultiBinding { Mode = BindingMode.OneWay, Converter = TranslationConverter.Instance };
        binding.Bindings.Add(textBinding);
        binding.Bindings.Add(new Binding(nameof(Language)) { Source = Current, Mode = BindingMode.OneWay });
        return binding;
    }
}

public sealed class TranslationConverter : IMultiValueConverter
{
    public static TranslationConverter Instance { get; } = new();
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        parameter as string == "Key"
            ? LocalizationService.Current[values[0] as string ?? ""]
            : LocalizationService.Current.Translate(values.FirstOrDefault() as string);
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class TranslateExtension : MarkupExtension
{
    public string? Key { get; set; }
    public string? Path { get; set; }
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        BindingBase text = Path != null ? new Binding(Path) { Mode = BindingMode.OneWay }
            : new Binding { Source = Key ?? "" };
        var binding = LocalizationService.CreateBinding(text);
        if (Key != null) binding.ConverterParameter = "Key";
        return binding.ProvideValue(serviceProvider);
    }
}
