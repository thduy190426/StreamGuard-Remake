using System.Globalization;
using System.IO;
using System.Text;

namespace StreamGuard.Services;

/// <summary>Only the owner process writes transcripts; session names never collide.</summary>
public sealed class CaptionTranscriptWriter : IDisposable
{
    private readonly object _lock = new();
    private readonly StreamWriter _txt, _srt;
    private int _index;
    private bool _disposed;
    public string TextPath { get; }
    public string SubtitlePath { get; }
    public CaptionTranscriptWriter(string directory)
    {
        Directory.CreateDirectory(directory);
        var name = "transcript_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];
        TextPath = Path.Combine(directory, name + ".txt"); SubtitlePath = Path.Combine(directory, name + ".srt");
        _txt = new StreamWriter(new FileStream(TextPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
        try { _srt = new StreamWriter(new FileStream(SubtitlePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true }; }
        catch { _txt.Dispose(); throw; }
    }
    public static string Timestamp(double seconds)
    {
        if (!double.IsFinite(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
        var milliseconds = (long)Math.Round(Math.Clamp(seconds, 0, 360000000) * 1000, MidpointRounding.AwayFromZero);
        return $"{milliseconds / 3600000:00}:{milliseconds / 60000 % 60:00}:{milliseconds / 1000 % 60:00},{milliseconds % 1000:000}";
    }
    public void Write(string text, double start, double end, string? translation = null)
    {
        lock (_lock)
        {
            if (_disposed) return;
            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length == 0) return;
            if (!double.IsFinite(start) || !double.IsFinite(end)) throw new ArgumentOutOfRangeException(nameof(start));
            start = Math.Max(0, start); end = Math.Max(start + .5, end);
            _txt.WriteLine(text);
            if (!string.IsNullOrWhiteSpace(translation))
            {
                var cleanTrans = translation.Replace("\r", " ").Replace("\n", " ").Trim();
                if (cleanTrans.Length > 0 && !cleanTrans.Equals(text, StringComparison.OrdinalIgnoreCase))
                    _txt.WriteLine("  -> " + cleanTrans);
            }
            _txt.WriteLine();
            _srt.WriteLine(++_index); _srt.WriteLine(Timestamp(start) + " --> " + Timestamp(end)); _srt.WriteLine(text);
            if (!string.IsNullOrWhiteSpace(translation))
            {
                var cleanTrans = translation.Replace("\r", " ").Replace("\n", " ").Trim();
                if (cleanTrans.Length > 0 && !cleanTrans.Equals(text, StringComparison.OrdinalIgnoreCase))
                    _srt.WriteLine(cleanTrans);
            }
            _srt.WriteLine();
        }
    }
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            try { _txt.Dispose(); } finally { _srt.Dispose(); }
        }
    }
}
