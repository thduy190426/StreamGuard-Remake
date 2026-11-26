using Microsoft.Win32.SafeHandles;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;

namespace StreamGuard.Services;

public sealed record ProcessSafetyInfo(int ProcessId, string Name, string ExecutablePath, int SessionId, bool IsCritical);

public interface IProcessSafetyQuery
{
    bool TryRead(int processId, out ProcessSafetyInfo? info);
    int GetWindowProcessId(IntPtr window);
}

/// <summary>Read-only checks shared by the picker and all managed-window action paths.</summary>
public sealed class ProcessSafetyService
{
    public static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromSeconds(4);
    public static ProcessSafetyService Default { get; } = new();
    private readonly IProcessSafetyQuery _query;
    private readonly TimeSpan _cacheTtl;

    private readonly struct CacheEntry
    {
        public readonly ProcessSafetyInfo Info;
        public readonly long Timestamp;

        public CacheEntry(ProcessSafetyInfo info, long timestamp)
        {
            Info = info;
            Timestamp = timestamp;
        }
    }

    private readonly ConcurrentDictionary<int, CacheEntry> _cache = new();

    public ProcessSafetyService(IProcessSafetyQuery? query = null, TimeSpan? cacheTtl = null)
    {
        _query = query ?? new NativeQuery();
        _cacheTtl = cacheTtl ?? (_query is NativeQuery ? DefaultCacheTtl : TimeSpan.Zero);
    }

    public TimeSpan CacheTtl => _cacheTtl;

    public void ClearCache() => _cache.Clear();

    public bool InvalidateCache(int processId) => _cache.TryRemove(processId, out _);

    public static void ClearDefaultCache() => Default.ClearCache();

    public static bool InvalidateDefaultCache(int processId) => Default.InvalidateCache(processId);

    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Idle", "System", "Registry", "Secure System", "SecureSystem", "Memory Compression", "MemoryCompression",
        "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso", "svchost",
        "fontdrvhost", "dwm", "sihost", "explorer", "userinit", "LogonUI", "LockApp",
        "ShellExperienceHost", "StartMenuExperienceHost", "ShellHost", "SearchHost", "SearchApp",
        "SearchUI", "SearchIndexer", "SearchProtocolHost", "SearchFilterHost", "TextInputHost",
        "RuntimeBroker", "ApplicationFrameHost", "ctfmon", "conhost", "OpenConsole",
        "taskhostw", "taskhost", "taskeng", "spoolsv", "audiodg", "WmiPrvSE", "dllhost",
        "WUDFHost", "dasHost", "DisplaySwitch", "SystemSettings", "SystemSettingsBroker",
        "SecurityHealthService", "SecurityHealthSystray", "MsMpEng", "NisSrv", "MsSense",
        "SenseIR", "SenseNdr", "SenseCncProxy", "SenseSampleUploader", "MpDefenderCoreService",
        "TrustedInstaller", "TiWorker", "MoUsoCoreWorker", "UsoClient", "CompatTelRunner", "Taskmgr"
    };
    private static readonly HashSet<string> DesktopUtilities = new(StringComparer.OrdinalIgnoreCase)
    {
        "notepad", "mspaint", "calc", "SnippingTool"
    };

    public static bool IsProtectedIdentity(string? processName, string? executablePath = null)
    {
        try
        {
            var name = NormalizeName(processName);
            if (name.Length == 0 || ProtectedNames.Contains(name)) return true;
            if (string.IsNullOrWhiteSpace(executablePath)) return false;
            if (ProtectedNames.Contains(NormalizeName(executablePath))) return true;
            var path = Path.GetFullPath(executablePath);
            if (IsUnder(path, CaptionRuntime.Root)) return true;
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (IsUnder(path, Path.Combine(windows, "SystemApps")) ||
                IsUnder(path, Path.Combine(windows, "servicing"))) return true;
            return (IsUnder(path, Path.Combine(windows, "System32")) ||
                    IsUnder(path, Path.Combine(windows, "SysWOW64"))) &&
                   !DesktopUtilities.Contains(NormalizeName(path));
        }
        catch { return true; }
    }

    private static string NormalizeName(string? name)
    {
        var file = Path.GetFileName((name ?? "").Trim());
        return file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file;
    }

    private static bool IsUnder(string path, string directory) =>
        path.StartsWith(directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public bool TryGetManageableProcess(int processId, out ProcessSafetyInfo? info)
    {
        info = null;
        if (processId <= 4) return false;

        if (_cacheTtl > TimeSpan.Zero && _cache.TryGetValue(processId, out var entry))
        {
            if (Environment.TickCount64 - entry.Timestamp < (long)_cacheTtl.TotalMilliseconds)
            {
                info = entry.Info;
                return true;
            }
            ((ICollection<KeyValuePair<int, CacheEntry>>)_cache).Remove(new KeyValuePair<int, CacheEntry>(processId, entry));
        }

        try
        {
            if (!_query.TryRead(processId, out var candidate) || candidate == null ||
                candidate.ProcessId != processId || candidate.IsCritical || candidate.SessionId <= 0 ||
                string.IsNullOrWhiteSpace(candidate.ExecutablePath) ||
                IsProtectedIdentity(candidate.Name, candidate.ExecutablePath))
            {
                if (_cacheTtl > TimeSpan.Zero)
                    _cache.TryRemove(processId, out _);
                return false;
            }

            if (_cacheTtl > TimeSpan.Zero)
            {
                _cache[processId] = new CacheEntry(candidate, Environment.TickCount64);
            }

            info = candidate;
            return true;
        }
        catch
        {
            if (_cacheTtl > TimeSpan.Zero)
                _cache.TryRemove(processId, out _);
            return false;
        }
    }

    public bool TryRead(int processId, out ProcessSafetyInfo? info) => TryGetManageableProcess(processId, out info);

    public bool CanManageProcess(int processId) => TryGetManageableProcess(processId, out _);

    public bool CanManageWindow(IntPtr window, int expectedProcessId = 0)
    {
        if (window == IntPtr.Zero) return false;
        try
        {
            var processId = _query.GetWindowProcessId(window);
            return processId > 0 && (expectedProcessId == 0 || expectedProcessId == processId) && CanManageProcess(processId);
        }
        catch { return false; }
    }

    internal sealed class NativeQuery : IProcessSafetyQuery
    {
        private const int ErrorInsufficientBuffer = 122;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, int processId);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool IsProcessCritical(SafeProcessHandle process, out bool critical);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
        private static extern unsafe bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, char* name, ref int size);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ProcessIdToSessionId(int processId, out int sessionId);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        public unsafe bool TryRead(int processId, out ProcessSafetyInfo? info) => TryRead(processId, out info, 1024);

        internal unsafe bool TryRead(int processId, out ProcessSafetyInfo? info, int initialBufferSize)
        {
            info = null;
            using var handle = OpenProcess(0x1000 /* QUERY_LIMITED_INFORMATION only */, false, processId);
            if (handle.IsInvalid || !IsProcessCritical(handle, out var critical) ||
                !ProcessIdToSessionId(processId, out var sessionId)) return false;

            int stackSize = Math.Min(Math.Max(1, initialBufferSize), 1024);
            Span<char> buffer = stackalloc char[stackSize];
            int size = buffer.Length;
            string path;

            fixed (char* ptr = buffer)
            {
                if (QueryFullProcessImageNameW(handle, 0, ptr, ref size))
                {
                    path = new string(ptr, 0, size);
                }
                else if (Marshal.GetLastWin32Error() == ErrorInsufficientBuffer)
                {
                    var rented = ArrayPool<char>.Shared.Rent(32768);
                    try
                    {
                        int rentedSize = rented.Length;
                        fixed (char* rentedPtr = rented)
                        {
                            if (!QueryFullProcessImageNameW(handle, 0, rentedPtr, ref rentedSize))
                                return false;
                            path = new string(rentedPtr, 0, rentedSize);
                        }
                    }
                    finally
                    {
                        ArrayPool<char>.Shared.Return(rented);
                    }
                }
                else
                {
                    return false;
                }
            }

            info = new(processId, Path.GetFileNameWithoutExtension(path), path, sessionId, critical);
            return true;
        }

        public int GetWindowProcessId(IntPtr window)
        {
            GetWindowThreadProcessId(window, out var processId);
            return unchecked((int)processId);
        }
    }
}
