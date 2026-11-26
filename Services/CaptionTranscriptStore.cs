using System.Globalization;
using System.IO;
using System.Text;

namespace StreamGuard.Services;

internal sealed record CaptionTranscriptSession(string Id, string Label, DateTime ModifiedUtc)
{
    public override string ToString() => Label;
}
internal sealed record CaptionTranscriptContent(string Text, bool Truncated);

/// <summary>Read-only, bounded access to this application's saved caption sessions.</summary>
internal sealed class CaptionTranscriptStore
{
    internal const int MaximumBytes = 256 * 1024;
    private readonly string _root;
    internal CaptionTranscriptStore(string root) => _root = Path.GetFullPath(root);

    internal IReadOnlyList<CaptionTranscriptSession> List(CancellationToken cancellation = default)
    {
        if (!Directory.Exists(_root)) return Array.Empty<CaptionTranscriptSession>();
        var sessions = new Dictionary<string, CaptionTranscriptSession>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in new DirectoryInfo(_root).EnumerateFiles("transcript_*"))
        {
            cancellation.ThrowIfCancellationRequested();
            if ((!file.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) && !file.Extension.Equals(".srt", StringComparison.OrdinalIgnoreCase)) ||
                (file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            var id = Path.GetFileNameWithoutExtension(file.Name);
            var label = id;
            if (id.Length >= 35 && DateTime.TryParseExact(id.Substring(11, 15), "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var started)) label = started.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " · " + id[27..];
            if (!sessions.TryGetValue(id, out var old) || old.ModifiedUtc < file.LastWriteTimeUtc)
                sessions[id] = new(id, label, file.LastWriteTimeUtc);
        }
        return sessions.Values.OrderByDescending(item => item.ModifiedUtc).ThenByDescending(item => item.Id).Take(200).ToArray();
    }

    internal CaptionTranscriptContent Read(string id, bool subtitle, CancellationToken cancellation = default)
    {
        if (!id.StartsWith("transcript_", StringComparison.Ordinal) || id != Path.GetFileName(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new IOException("Invalid transcript session.");
        var path = Path.Combine(_root, id + (subtitle ? ".srt" : ".txt"));
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Transcript links are not supported.");
        // FileShare.Write is essential while CaptionTranscriptWriter owns the active file.
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = input.Length;
        var start = Math.Max(0, length - MaximumBytes);
        input.Position = start;
        var buffer = new byte[(int)Math.Min(length, MaximumBytes)];
        var count = 0;
        while (count < buffer.Length)
        {
            cancellation.ThrowIfCancellationRequested();
            var read = input.Read(buffer, count, buffer.Length - count);
            if (read == 0) break;
            count += read;
        }
        var offset = 0;
        if (start > 0) while (offset < count && (buffer[offset] & 0xC0) == 0x80) offset++;
        var characters = new char[count];
        // Do not render a replacement character for an incomplete UTF-8 write at EOF.
        Encoding.UTF8.GetDecoder().Convert(buffer, offset, count - offset, characters, 0, characters.Length,
            flush: false, out _, out var used, out _);
        return new(new string(characters, 0, used).TrimStart('\uFEFF'), start > 0);
    }

    internal string ReadAllText(string id, bool subtitle)
    {
        if (!id.StartsWith("transcript_", StringComparison.Ordinal) || id != Path.GetFileName(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "";
        var path = Path.Combine(_root, id + (subtitle ? ".srt" : ".txt"));
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return "";
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(input, Encoding.UTF8);
            return reader.ReadToEnd().TrimStart('\uFEFF');
        }
        catch { return ""; }
    }

    internal bool Delete(string id)
    {
        if (!id.StartsWith("transcript_", StringComparison.Ordinal) || id != Path.GetFileName(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;
        var txt = Path.Combine(_root, id + ".txt");
        var srt = Path.Combine(_root, id + ".srt");
        var deleted = false;
        try { if (File.Exists(txt)) { File.Delete(txt); deleted = true; } } catch { }
        try { if (File.Exists(srt)) { File.Delete(srt); deleted = true; } } catch { }
        return deleted;
    }

    internal bool ClearContent(string id)
    {
        if (!id.StartsWith("transcript_", StringComparison.Ordinal) || id != Path.GetFileName(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;
        var txt = Path.Combine(_root, id + ".txt");
        var srt = Path.Combine(_root, id + ".srt");
        var cleared = false;
        try { if (File.Exists(txt)) { File.WriteAllText(txt, "", new UTF8Encoding(false)); cleared = true; } } catch { }
        try { if (File.Exists(srt)) { File.WriteAllText(srt, "", new UTF8Encoding(false)); cleared = true; } } catch { }
        return cleared;
    }

    internal int DeletePreviousSessions(string? activeId = null)
    {
        var sessions = List();
        if (sessions.Count <= 1) return 0;
        var count = 0;
        var idToKeep = !string.IsNullOrEmpty(activeId) ? activeId : sessions[0].Id;
        foreach (var session in sessions)
        {
            if (session.Id.Equals(idToKeep, StringComparison.OrdinalIgnoreCase)) continue;
            if (Delete(session.Id)) count++;
        }
        return count;
    }
}
