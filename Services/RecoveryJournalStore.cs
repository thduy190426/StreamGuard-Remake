using Newtonsoft.Json;
using StreamGuard.Models;
using System.IO;

namespace StreamGuard.Services;

public interface IRecoveryJournalStore
{
    IReadOnlyList<RecoveryRecord> Load();
    void Save(IEnumerable<RecoveryRecord> records);
    string? Warning { get; }
}

/// <summary>Separate from preferences. A durable snapshot is written before any window changes.</summary>
public sealed class RecoveryJournalStore : IRecoveryJournalStore
{
    private readonly string _path;
    public string? Warning { get; private set; }
    private sealed record Journal(int Version, List<RecoveryRecord> Records);
    public RecoveryJournalStore(string path) => _path = Path.GetFullPath(path);
    private static List<RecoveryRecord> Parse(string json)
    {
        var journal = JsonConvert.DeserializeObject<Journal>(json) ?? throw new JsonException("Empty recovery journal.");
        if (journal.Version != 1 || journal.Records == null || journal.Records.Count > 2000 ||
            journal.Records.Any(record => record == null || record.Id == Guid.Empty || record.Snapshot == null ||
                record.Snapshot.Identity == null || record.Token <= 0 || !Enum.IsDefined(record.State) || !Enum.IsDefined(record.Method)) ||
            journal.Records.Select(record => record.Id).Distinct().Count() != journal.Records.Count)
            throw new JsonException("Invalid recovery journal.");
        return journal.Records;
    }
    private static string Read(string path)
    {
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new JsonException("Recovery journal is too large.");
        return File.ReadAllText(path);
    }
    public IReadOnlyList<RecoveryRecord> Load()
    {
        Warning = null;
        if (!File.Exists(_path) && !File.Exists(_path + ".bak")) return Array.Empty<RecoveryRecord>();
        Exception? failure = null;
        foreach (var path in new[] { _path, _path + ".bak" })
        {
            if (!File.Exists(path)) continue;
            try
            {
                var records = Parse(Read(path));
                if (failure != null) Warning = "Recovery data was loaded from its backup.";
                return records;
            }
            catch (Exception error) { failure = error; }
        }
        throw new IOException("Recovery data could not be read.", failure);
    }
    public void Save(IEnumerable<RecoveryRecord> records)
    {
        var json = JsonConvert.SerializeObject(new Journal(1, records.ToList()), Formatting.Indented);
        Parse(json);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 4 * 1024 * 1024) throw new IOException("Recovery journal is too large.");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), leaveOpen: true);
                writer.Write(json); writer.Flush(); stream.Flush(true);
            }
            if (File.Exists(_path))
            {
                var valid = true;
                try { Parse(Read(_path)); }
                catch (JsonException) { valid = false; File.Copy(_path, _path + ".corrupt-" + Guid.NewGuid().ToString("N")); }
                File.Replace(temporary, _path, valid ? _path + ".bak" : null, true);
            }
            else File.Move(temporary, _path);
            Warning = null;
        }
        finally { try { File.Delete(temporary); } catch { } }
    }
}
