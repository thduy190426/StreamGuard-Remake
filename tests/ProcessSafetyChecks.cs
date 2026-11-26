using StreamGuard;
using StreamGuard.Helpers;
using StreamGuard.Models;
using StreamGuard.Services;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Interop;

internal static class ProcessSafetyChecks
{
    public static void Run(Action<bool, string> check, string artifactDirectory)
    {
        foreach (var name in new[] { "System", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost", "dwm", "explorer", "sihost", "ShellExperienceHost", "StartMenuExperienceHost", "RuntimeBroker", "TextInputHost", "MsMpEng", "SecurityHealthService", "WmiPrvSE", "Taskmgr" })
        {
            check(ProcessSafetyService.IsProtectedIdentity(name.ToUpperInvariant() + ".EXE"), "Important system name is blocked: " + name);
            check(ProcessSafetyService.IsProtectedIdentity("custom-name", @"C:\Windows\System32\" + name + ".exe"), "A different configured name cannot bypass the executable identity: " + name);
        }
        foreach (var name in new[] { "Antigravity", "Code", "chrome", "obs64", "notepad", "System.App", "explorer-helper", "svchost-tools" })
            check(!ProcessSafetyService.IsProtectedIdentity(name, @"C:\Apps\" + name + ".exe"), "Ordinary application is retained: " + name);
        var systemApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SystemApps");
        check(ProcessSafetyService.IsProtectedIdentity("unknown-shell", Path.Combine(systemApps, "package", "host.exe")), "Windows SystemApps components are excluded");
        check(!ProcessSafetyService.IsProtectedIdentity("regular", Path.Combine(systemApps + "Backup", "regular.exe")), "Directory-prefix lookalikes are not excluded");
        var system32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32");
        check(ProcessSafetyService.IsProtectedIdentity("unlisted-host", Path.Combine(system32, "new-system-host.exe")), "Unlisted core Windows components are excluded by location");
        check(!ProcessSafetyService.IsProtectedIdentity("notepad", Path.Combine(system32, "notepad.exe")), "Ordinary desktop utilities remain usable in the Windows folder");
        check(ProcessSafetyService.IsProtectedIdentity(null), "An unknown configured identity is rejected");

        var query = new FakeQuery();
        var safety = new ProcessSafetyService(query);
        check(safety.CanManageProcess(100), "An identified interactive application is allowed");
        query.Info = query.Info! with { IsCritical = true };
        check(!safety.CanManageProcess(100), "A critical process is blocked even with an ordinary name");
        query.Info = query.Info with { IsCritical = false, SessionId = 0 };
        check(!safety.CanManageProcess(100), "Session-zero services are blocked");
        query.Info = query.Info with { SessionId = 1, Name = "explorer" };
        check(!safety.CanManageProcess(100), "The live process identity overrides an innocent UI label");
        query.Info = query.Info with { Name = "Antigravity", ExecutablePath = "" };
        check(!safety.CanManageProcess(100), "Missing executable verification prevents manipulation");
        query.Info = query.Info with { ExecutablePath = @"C:\Apps\Antigravity.exe", ProcessId = 101 };
        check(!safety.CanManageProcess(100), "A mismatched process snapshot is rejected");
        query.Info = query.Info with { ProcessId = 100 };
        query.Available = false;
        check(!safety.CanManageProcess(100), "Access denied or an exited process fails closed");
        query.Available = true; query.Throw = true;
        check(!safety.CanManageProcess(100) && !safety.CanManageWindow(new IntPtr(1)), "Query exceptions cannot permit actions");
        query.Throw = false;
        var reads = query.Reads;
        check(!safety.CanManageProcess(0) && !safety.CanManageProcess(4) && query.Reads == reads, "System/idle PIDs are rejected without opening them");
        check(safety.CanManageWindow(new IntPtr(1), 100), "A window owned by the expected safe process is allowed");
        check(!safety.CanManageWindow(new IntPtr(1), 101), "A recycled HWND with a different owner is blocked");
        query.WindowProcessId = 0;
        check(!safety.CanManageWindow(new IntPtr(1)) && !safety.CanManageWindow(IntPtr.Zero), "Unknown and invalid window owners are rejected");

        var path = Path.Combine(artifactDirectory, "system-process-config.json");
        var apps = new[]
        {
            new GuardedApp { Name = "Windows shell", ProcessName = "explorer", ExecutablePath = @"C:\Windows\explorer.exe" },
            new GuardedApp { Name = "Disguised entry", ProcessName = "ordinary", ExecutablePath = @"C:\Windows\System32\lsass.exe" },
            new GuardedApp { Name = "IDE", ProcessName = "Antigravity", ExecutablePath = @"C:\Apps\Antigravity.exe", SecureHotkey = "Ctrl+Alt+7" }
        };
        var config = new ConfigService(path);
        check(config.Save(apps, language: "vi", captureGuardEnabled: true), "Mixed configurations can be saved safely");
        var safeApps = new ConfigService(path).Load();
        check(safeApps.Count == 1 && safeApps[0].ProcessName == "Antigravity" && safeApps[0].SecureHotkey == "Ctrl+Alt+7", "Saving removes system targets and preserves ordinary app preferences");
        File.WriteAllText(path, "[{\"ProcessName\":\"csrss\"},{\"ProcessName\":\"Antigravity\"}]");
        check(new ConfigService(path).Load().Single().ProcessName == "Antigravity", "Legacy arrays cannot reintroduce system targets");
        File.WriteAllText(path, "{\"Language\":\"vi\",\"CaptureGuardEnabled\":true,\"Apps\":[{\"ProcessName\":\"svchost\"},{\"ProcessName\":\"Antigravity\"}]}");
        config = new ConfigService(path); safeApps = config.Load();
        check(safeApps.Single().ProcessName == "Antigravity" && config.Language == "vi" && config.CaptureGuardEnabled, "Legacy object filtering retains global settings");

        using var current = Process.GetCurrentProcess();
        check(ProcessSafetyService.Default.CanManageProcess(current.Id), "Read-only Windows checks permit the interactive test process");
        check(!ProcessSafetyService.Default.CanManageProcess(4) && !ProcessSafetyService.Default.CanManageProcess(int.MaxValue), "Native checks reject system and nonexistent PIDs");
        check(WindowEnumerator.FindAllIncludingHiddenByProcessName("csrss").Count == 0, "The hidden-window fallback cannot select a system process");

        CheckCachingAndZeroAllocation(check);
    }

    public static void CheckCachingAndZeroAllocation(Action<bool, string> check)
    {
        // 1. Verification of cache hits and query count suppression with TTL
        var query = new FakeQuery();
        var cachedService = new ProcessSafetyService(query, TimeSpan.FromMilliseconds(500));

        check(cachedService.CanManageProcess(100), "Initial manageable query succeeds");
        check(query.Reads == 1, "First call queries the underlying provider");

        check(cachedService.CanManageProcess(100), "Second query within TTL succeeds");
        check(query.Reads == 1, "Repeated call within TTL does not query provider (0 reads)");

        // Mutating query.Info while cached: cache should return previously validated identity within TTL
        query.Info = query.Info! with { IsCritical = true };
        check(cachedService.CanManageProcess(100), "Cached verification shields against transient provider churn within TTL");
        check(query.Reads == 1, "Cache hit avoided opening or querying process");

        // Explicit cache invalidation for specific PID
        check(cachedService.InvalidateCache(100), "InvalidateCache returns true for cached PID");
        check(!cachedService.CanManageProcess(100), "After invalidating cache, query is re-read and critical process is rejected");
        check(query.Reads == 2, "Invalidated entry re-queried provider");

        // ClearCache verification
        query.Info = query.Info! with { IsCritical = false };
        check(cachedService.CanManageProcess(100), "Restored safe process is accepted");
        check(query.Reads == 3, "New query after rejection hits provider");
        check(cachedService.CanManageProcess(100), "Cached again");
        check(query.Reads == 3, "Hit cache again");
        cachedService.ClearCache();
        check(cachedService.CanManageProcess(100), "CanManageProcess works after ClearCache");
        check(query.Reads == 4, "ClearCache forced provider re-read");

        // TTL expiry verification
        var shortTtlService = new ProcessSafetyService(query, TimeSpan.FromMilliseconds(50));
        var initialReads = query.Reads;
        check(shortTtlService.CanManageProcess(100), "Short TTL service initial query succeeds");
        check(query.Reads == initialReads + 1, "Short TTL queried provider once");
        check(shortTtlService.CanManageProcess(100), "Immediate second query uses cache");
        check(query.Reads == initialReads + 1, "No re-read within 50ms TTL");
        Thread.Sleep(80);
        check(shortTtlService.CanManageProcess(100), "Query after 50ms TTL expires succeeds");
        check(query.Reads == initialReads + 2, "TTL expiration triggered provider re-query as expected");

        // 2. Zero-allocation verification in tight loop (simulating 50ms timer with a real HWND)
        using var current = Process.GetCurrentProcess();
        int currentPid = current.Id;

        using var testWindow = new HwndSource(new HwndSourceParameters("Zero-alloc timer check window")
        {
            Width = 10, Height = 10, PositionX = -32000, PositionY = -32000, WindowStyle = unchecked((int)0x80000000)
        });
        IntPtr validHwnd = testWindow.Handle;

        // Warm up JIT and populate cache
        check(ProcessSafetyService.Default.CanManageProcess(currentPid), "Warmup CanManageProcess succeeds");
        check(ProcessSafetyService.Default.CanManageWindow(validHwnd, currentPid), "Warmup CanManageWindow on real HWND succeeds");

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long startAlloc = GC.GetAllocatedBytesForCurrentThread();
        const int iterations = 1000;
        for (int i = 0; i < iterations; i++)
        {
            ProcessSafetyService.Default.CanManageProcess(currentPid);
            ProcessSafetyService.Default.CanManageWindow(validHwnd, currentPid);
        }
        long endAlloc = GC.GetAllocatedBytesForCurrentThread();
        long allocatedBytes = endAlloc - startAlloc;

        check(allocatedBytes == 0, $"ProcessSafety loop with valid HWND allocated 0 bytes (actual: {allocatedBytes} bytes across {iterations * 2} calls)");

        // 3. NativeQuery zero-allocation buffer verification
        var nativeQuery = new ProcessSafetyService.NativeQuery();
        check(nativeQuery.TryRead(currentPid, out var nativeInfo) && nativeInfo != null && nativeInfo.ProcessId == currentPid,
            "NativeQuery correctly reads current process image using stackalloc buffer");
        check(File.Exists(nativeInfo!.ExecutablePath), "NativeQuery executable path points to a real file");

        // 4. NativeQuery fallback to ArrayPool when initial buffer is insufficient
        check(nativeQuery.TryRead(currentPid, out var fallbackInfo, initialBufferSize: 4) && fallbackInfo != null && fallbackInfo.ExecutablePath == nativeInfo.ExecutablePath,
            "NativeQuery correctly falls back to ArrayPool when initial buffer is insufficient and retrieves identical path");
    }

    public static void CheckUiGuards(MainWindow window, Action<bool, string> check)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var target = new GuardedApp { Name = "Unsafe old entry", ProcessName = "lsass", ExecutablePath = @"C:\Windows\System32\lsass.exe", IsEnabled = false };
        var add = typeof(MainWindow).GetMethod("AddWindowToGuardedApps", flags)!;
        check(add.Invoke(window, new object[] { new WindowInfo { ProcessName = "explorer", Handle = new IntPtr(1) } }) == null, "Direct add cannot bypass the system filter");
        var launch = (Task<List<WindowInfo>>)typeof(MainWindow).GetMethod("LaunchAndWaitForWindowsAsync", flags)!.Invoke(window, new object[] { target })!;
        check(launch.IsCompletedSuccessfully && launch.Result.Count == 0, "An old system entry cannot launch a process");
        var secure = (Task)typeof(MainWindow).GetMethod("SecureOpenAppAsync", flags)!.Invoke(window, new object[] { target })!;
        check(secure.IsCompletedSuccessfully && !target.IsEnabled, "Secure hotkeys cannot activate a system target");
        object[] protectArguments = { target, Array.Empty<WindowInfo>(), 0 };
        check(!(bool)typeof(MainWindow).GetMethod("ProtectSingleApp", flags)!.Invoke(window, protectArguments)! && (int)protectArguments[2] == 0, "Direct protection rejects a system target before native actions");
        using var source = new HwndSource(new HwndSourceParameters("Process-safety recovery check")
        {
            Width = 16, Height = 16, PositionX = -32000, PositionY = -32000, WindowStyle = unchecked((int)0x80000000)
        });
        var stale = new GuardedApp { Name = "Stale owner", ProcessName = "Antigravity", IsProtected = true };
        stale.ProtectedWindows.Add(new ProtectedWindowState { Handle = source.Handle, ProcessId = Environment.ProcessId + 1, HiddenByStreamGuard = true });
        var restored = (bool)typeof(MainWindow).GetMethod("RestoreApp", flags)!.Invoke(window, new object[] { stale })!;
        check(!restored && stale.ProtectedWindows.Count == 1 && stale.IsProtected, "A changed HWND owner is left untouched and its recovery state is retained");
        var brought = (bool)typeof(MainWindow).GetMethod("BringProtectedAppToFront", flags)!.Invoke(window, new object[] { stale })!;
        check(!brought && stale.ProtectedWindows.Count == 1, "Focus failure never discards a live window recovery record");
    }

    private sealed class FakeQuery : IProcessSafetyQuery
    {
        public ProcessSafetyInfo? Info = new(100, "Antigravity", @"C:\Apps\Antigravity.exe", 1, false);
        public bool Available = true;
        public bool Throw;
        public int Reads;
        public int WindowProcessId = 100;
        public bool TryRead(int processId, out ProcessSafetyInfo? info)
        {
            Reads++;
            if (Throw) throw new InvalidOperationException("Simulated access failure");
            info = Info;
            return Available;
        }
        public int GetWindowProcessId(IntPtr window)
        {
            if (Throw) throw new InvalidOperationException("Simulated owner failure");
            return WindowProcessId;
        }
    }
}
