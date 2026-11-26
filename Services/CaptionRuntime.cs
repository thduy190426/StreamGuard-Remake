using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

namespace StreamGuard.Services;

public static class CaptionRuntime
{
    private static readonly SemaphoreSlim Gate = new(1);
    private static readonly string[] BackendFiles = { "worker.py", "app/__init__.py", "app/config.py", "app/audio_capture.py", "app/transcriber.py" };
    private const string Resource = "StreamGuard.Captions.Runtime.zip";
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamGuard", "CaptionsRuntime");
    private static Stream Open(string resource) => typeof(CaptionRuntime).Assembly.GetManifestResourceStream(resource) ?? throw new IOException("The caption runtime is missing from this build.");
    private static string BackendResource(string relative) => "StreamGuard.Captions.Backend." + relative.Replace('/', '.');
    internal static string SafeEntryPath(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Invalid caption runtime archive path.");
        return path;
    }
    private static void NoLinks(string path)
    {
        for (var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("The caption runtime path contains a link.");
    }
    public static async Task<ProcessStartInfo> CreateStartInfoAsync(CancellationToken cancellation)
    {
        await Gate.WaitAsync(cancellation).ConfigureAwait(false);
        try { return await Task.Run(() => Prepare(cancellation), cancellation).ConfigureAwait(false); }
        finally { Gate.Release(); }
    }
    private static ProcessStartInfo Prepare(CancellationToken cancellation)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using (var runtime = Open(Resource))
        {
            var buffer = new byte[128 * 1024]; int count;
            while ((count = runtime.Read(buffer)) != 0) { cancellation.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, count); }
        }
        foreach (var relative in BackendFiles)
        { using var file = Open(BackendResource(relative)); using var memory = new MemoryStream(); file.CopyTo(memory); hash.AppendData(memory.ToArray()); }
        var version = Convert.ToHexString(hash.GetHashAndReset())[..24];
        NoLinks(Root); Directory.CreateDirectory(Root);
        var destination = Path.Combine(Root, version);
        NoLinks(destination);
        if (!File.Exists(Path.Combine(destination, ".complete")))
        {
            // Unique staging directories make cancellation/retries safe without deleting a working runtime.
            var stage = Path.Combine(Root, ".staging-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            try
            {
            using (var stream = Open(Resource))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                long total = 0;
                foreach (var entry in zip.Entries)
                {
                    cancellation.ThrowIfCancellationRequested();
                    total += entry.Length;
                    if (total > 2L * 1024 * 1024 * 1024 || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000) throw new IOException("Invalid caption runtime archive.");
                    var target = SafeEntryPath(stage, entry.FullName);
                    if (entry.Name.Length == 0) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using var input = entry.Open(); using var output = File.Create(target); input.CopyTo(output);
                }
            }
            foreach (var relative in BackendFiles)
            {
                var target = SafeEntryPath(stage, "backend/" + relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var input = Open(BackendResource(relative)); using var output = File.Create(target); input.CopyTo(output);
            }
            cancellation.ThrowIfCancellationRequested();
            File.WriteAllText(Path.Combine(stage, ".complete"), version);
            if (Directory.Exists(destination)) throw new IOException("An incomplete caption runtime exists. Rename its directory and retry.");
            try { Directory.Move(stage, destination); }
            catch (IOException) when (File.Exists(Path.Combine(destination, ".complete"))) { /* Another app instance finished the same runtime. */ }
            }
            finally
            {
                // Only remove the unique directory created by this operation.
                if (Directory.Exists(stage) && Path.GetFullPath(stage).StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar + ".staging-", StringComparison.OrdinalIgnoreCase))
                {
                    try { NoLinks(stage); Directory.Delete(stage, recursive: true); } catch { }
                }
            }
        }
        var info = new ProcessStartInfo(Path.Combine(destination, "python.exe"));
        info.ArgumentList.Add("-I"); info.ArgumentList.Add("-B"); info.ArgumentList.Add("-u");
        info.ArgumentList.Add(Path.Combine(destination, "backend", "worker.py"));
        info.ArgumentList.Add("--model"); info.ArgumentList.Add(Path.Combine(destination, "model"));
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        info.Environment["HF_HUB_OFFLINE"] = "1";
        info.Environment["HF_HUB_DISABLE_TELEMETRY"] = "1";
        info.Environment["OPENBLAS_NUM_THREADS"] = "1";
        info.Environment["MKL_NUM_THREADS"] = "1";
        info.Environment["OMP_NUM_THREADS"] = "2";
        info.Environment["CT2_PACKED_GEMM"] = "0";
        return info;
    }
}
