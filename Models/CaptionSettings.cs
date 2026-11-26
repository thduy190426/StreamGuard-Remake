using Newtonsoft.Json;
using System.IO;

namespace StreamGuard.Models;

public sealed class CaptionSettings
{
    public int FontSize { get; set; } = 18;
    public bool SingleLine { get; set; }
    public bool ExcludeFromCapture { get; set; } = true;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public bool EnableTranslation { get; set; } = true;
    public string TranslationMode { get; set; } = "Bilingual";
    public string TranslationHotkey { get; set; } = "Ctrl+Alt+T";
    public string CustomKeywords { get; set; } = "Antigravity, Gemini, Driver Booster, StreamGuard, Python, C#, Windows, AI";
    public void Normalize()
    {
        FontSize = Math.Clamp(FontSize, 12, 36);
        if (Left.HasValue && (!double.IsFinite(Left.Value) || Math.Abs(Left.Value) > 100000)) Left = null;
        if (Top.HasValue && (!double.IsFinite(Top.Value) || Math.Abs(Top.Value) > 100000)) Top = null;
        if (TranslationMode != "VietnameseOnly") TranslationMode = "Bilingual";
        if (string.IsNullOrWhiteSpace(TranslationHotkey)) TranslationHotkey = "Ctrl+Alt+T";
        if (CustomKeywords == null)
            CustomKeywords = "Antigravity, Gemini, Driver Booster, StreamGuard, Python, C#, Windows, AI";
        else if (CustomKeywords.Length > 4000)
            CustomKeywords = CustomKeywords[..4000];
    }
    public static CaptionSettings Load(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 32768) return new();
            var settings = JsonConvert.DeserializeObject<CaptionSettings>(File.ReadAllText(path)) ?? new();
            settings.Normalize(); return settings;
        }
        catch { return new(); }
    }
    public bool Save(string path)
    {
        string? temporary = null;
        try
        {
            Normalize(); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(this, Formatting.Indented));
            File.Move(temporary, path, true); return true;
        }
        catch { return false; }
        finally { if (temporary != null) try { File.Delete(temporary); } catch { } }
    }
}
