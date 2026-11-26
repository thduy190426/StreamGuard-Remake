using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using StreamGuard.Helpers;
using Newtonsoft.Json.Linq;
using StreamGuard;
using StreamGuard.Models;
using StreamGuard.Services;

internal static class AuditChecks
{
    internal static void Configuration(Action<bool, string> check, string temporaryRoot)
    {
        var directory = Path.Combine(temporaryRoot, "configuration-audit"); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "config.json");
        var app = new GuardedApp { Name = "Demo — tiếng Việt", ProcessName = "demo", SecureHotkey = "Ctrl+Alt+7", IsEnabled = false };
        foreach (var raia in new[] { false, true }) foreach (var portal in new[] { false, true })
        foreach (var camera in new[] { false, true }) foreach (var microphone in new[] { false, true })
        {
            var service = new ConfigService(path);
            check(service.Save(new[] { app }, raia || portal, raia, portal, camera, microphone, true, "vi"), "Configuration matrix save");
            var read = new ConfigService(path); var loaded = read.Load();
            check(read.BlockRaiaWebsite == raia && read.BlockPortalWebsite == portal, $"Website choices survive reload: RAIA={raia}, Portal={portal}");
            check(read.BlockCameraAccess == camera && read.BlockMicrophoneAccess == microphone && read.PassiveFocusMode, "Device and focus choices survive reload");
            check(loaded.Count == 1 && loaded[0].Name == app.Name && !loaded[0].IsEnabled && loaded[0].SecureHotkey == app.SecureHotkey, "Application identity/hotkey survive reload");
        }
        var stable = new ConfigService(path); stable.Load(); var before = File.ReadAllBytes(path);
        check(!stable.EnableCursorShield, "Cursor shield is disabled by default in loaded configuration");
        stable.Save(new[] { app }, enableCursorShield: true);
        var readCursor = new ConfigService(path); readCursor.Load();
        check(readCursor.EnableCursorShield, "Cursor shield preference persists across save and load");
        stable.Save(new[] { app }, enableCursorShield: false);
        readCursor = new ConfigService(path); readCursor.Load();
        check(!readCursor.EnableCursorShield, "Disabling cursor shield persists across save and load");
        check(!stable.BlockVirtualDesktopHotkeys, "Virtual desktop hotkey shield is disabled by default in loaded configuration");
        stable.Save(new[] { app }, blockVirtualDesktopHotkeys: true);
        var readVd = new ConfigService(path); readVd.Load();
        check(readVd.BlockVirtualDesktopHotkeys, "BlockVirtualDesktopHotkeys preference persists across save and load");
        stable.Save(new[] { app }, blockVirtualDesktopHotkeys: false);
        readVd = new ConfigService(path); readVd.Load();
        check(!readVd.BlockVirtualDesktopHotkeys, "Disabling BlockVirtualDesktopHotkeys persists across save and load");
        check(!stable.AutoStartShield, "AutoStartShield is disabled by default in loaded configuration");
        stable.Save(new[] { app }, autoStartShield: true);
        var readAutoStart = new ConfigService(path); readAutoStart.Load();
        check(readAutoStart.AutoStartShield, "AutoStartShield preference persists across save and load");
        stable.Save(new[] { app }, autoStartShield: false);
        readAutoStart = new ConfigService(path); readAutoStart.Load();
        check(!readAutoStart.AutoStartShield, "Disabling AutoStartShield persists across save and load");
        before = File.ReadAllBytes(path);
        using (var locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            check(!stable.Save(new[] { app }, false, false, false, false, false, false, "en"), "Locked destination rejects save");
            check(stable.Language == "vi" && stable.BlockCameraAccess && stable.BlockMicrophoneAccess, "Failed save does not commit in-memory preferences");
        }
        check(before.SequenceEqual(File.ReadAllBytes(path)), "Failed save leaves configuration bytes intact");
        check(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "Failed save cleans its temporary file");
        File.WriteAllText(path, "{\"BlockKnownWebsites\":true,\"Apps\":[]}");
        var legacy = new ConfigService(path); legacy.Load();
        check(legacy.BlockRaiaWebsite, "Legacy website preference remains supported");
        File.WriteAllText(path, "{\"Apps\":[null,{\"Name\":null,\"ProcessName\":\"demo\",\"ExecutablePath\":null,\"SecureHotkey\":null}]}");
        var normalized = new ConfigService(path).Load().Single();
        check(normalized.Name == "demo" && normalized.ExecutablePath == "" && normalized.SecureHotkey == "", "Null app strings are normalized without losing valid entries");
        var same = new ConfigService(path); same.Load();
        File.WriteAllText(path, "[]"); same.Load();
        check(same.Language == "en" && same.BlockCameraAccess, "Repeated load resets defaults for a legacy array");
        check(same.Save(new[] { app }, language: "vi"), "Save recoverable configuration");
        check(same.Save(new[] { app }, language: "en"), "Atomic replacement creates backup");
        File.WriteAllText(path, "broken configuration");
        var recovery = new ConfigService(path);
        check(recovery.Load().Single().Name == app.Name && recovery.LoadedFromBackup && recovery.Language == "vi", "Malformed primary falls back to valid backup");
        check(recovery.Save(new[] { app }, language: "en"), "Save after backup recovery");
        check(Directory.GetFiles(directory, "config.json.corrupt-*").Any(file => File.ReadAllText(file) == "broken configuration"), "Malformed original is retained for manual recovery");
        check(JObject.Parse(File.ReadAllText(path + ".bak"))["Language"]!.Value<string>() == "vi", "A corrupt primary never replaces the good backup");
        File.WriteAllText(path, "null"); File.Delete(path + ".bak");
        check(new ConfigService(path).Load().Count == 0, "Unsupported JSON root fails safely without a backup");
        app.IsHotkeyDisabled = true; app.SecureHotkey = "";
        check(new ConfigService(path).Save(new[] { app }, blockKnownWebsites: true), "Legacy aggregate website-save API remains supported");
        var disabled = new ConfigService(path); var disabledApp = disabled.Load().Single();
        check(disabled.BlockRaiaWebsite && disabledApp.IsHotkeyDisabled && disabledApp.SecureHotkey == "", "Explicitly disabled hotkeys remain disabled after reload");
        var oversizedPath = Path.Combine(directory, "oversized.json");
        File.WriteAllText(oversizedPath + ".bak", "{\"Language\":\"vi\",\"Apps\":[]}");
        using (var oversized = File.Create(oversizedPath)) oversized.SetLength(6 * 1024 * 1024);
        var bounded = new ConfigService(oversizedPath); bounded.Load();
        check(bounded.LoadedFromBackup && bounded.LastLoadError?.Contains("large", StringComparison.OrdinalIgnoreCase) == true,
            "An oversized primary configuration is rejected before parsing and the valid backup remains usable");
        var sizePath = Path.Combine(directory, "size-limit.json");
        var sizeService = new ConfigService(sizePath); check(sizeService.Save(new[] { app }), "Prepare valid configuration for size-limit checks");
        var sizeBefore = File.ReadAllBytes(sizePath);
        var hugeApp = new GuardedApp { Name = new string('x', 5 * 1024 * 1024), ProcessName = "demo" };
        check(!sizeService.Save(new[] { hugeApp }) && sizeBefore.SequenceEqual(File.ReadAllBytes(sizePath)),
            "A configuration too large to reload is never written over a working configuration");
    }

    internal static void Hotkeys(Action<bool, string> check)
    {
        var method = typeof(MainWindow).GetMethod("IsReservedSystemHotkey", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var key in new[] { "Ctrl+Shift+G", "Control+Shift+G", "Shift+Ctrl+G", "Ctrl+Alt+Shift+H", "Alt+Shift+Control+C", "Ctrl+Shift+Alt+X", "Alt+Ctrl+Shift+A", "Shift+S+G", "Ctrl+Shift+S+G" })
            check((bool)method.Invoke(null, new object[] { key })!, "Reserved recovery shortcut cannot be assigned through aliases/order: " + key);
        check(!(bool)method.Invoke(null, new object[] { "Ctrl+Alt+7" })!, "Ordinary app shortcut is available");
    }

    internal static void ChildProcesses(Action<bool, string> check)
    {
        var runner = typeof(ConfigService).Assembly.GetType("StreamGuard.Services.ChildProcessRunner")!;
        var run = runner.GetMethod("Run", BindingFlags.Static | BindingFlags.NonPublic)!;
        object Execute(string mode, int timeout)
        {
            var info = new ProcessStartInfo(Environment.ProcessPath!);
            if (Path.GetFileNameWithoutExtension(info.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                info.ArgumentList.Add(typeof(AuditChecks).Assembly.Location);
            info.ArgumentList.Add(mode);
            return run.Invoke(null, new object[] { info, timeout })!;
        }
        object Get(object result, string property) => result.GetType().GetProperty(property)!.GetValue(result)!;
        var flood = Execute("--audit-child-flood", 10000);
        check((int)Get(flood, "ExitCode") == 7, "Verbose child exits normally without pipe deadlock");
        check(((string)Get(flood, "StandardOutput")).Length == 131072 && ((string)Get(flood, "StandardError")).Length == 131072, "Both output pipes are completely drained");
        var clock = Stopwatch.StartNew(); var timeoutResult = Execute("--audit-child-wait", 200);
        check((int)Get(timeoutResult, "ExitCode") == -1 && (string)Get(timeoutResult, "StandardError") == "Timed out" && clock.Elapsed < TimeSpan.FromSeconds(5), "Timeout terminates only the owned test child promptly");
        var missing = run.Invoke(null, new object[] { new ProcessStartInfo("missing-audit-executable-" + Guid.NewGuid().ToString("N")), 200 })!;
        check((int)Get(missing, "ExitCode") == -1, "Missing helper executable is reported safely");
    }

    internal static void Window(Action<bool, string> check, string temporaryRoot)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var path = Path.Combine(temporaryRoot, "async-config.json");
        var config = new ConfigService(path); config.Save(Array.Empty<GuardedApp>(), blockCameraAccess: false, blockMicrophoneAccess: false);
        Task<List<WindowInfo>> nextLaunch = Task.FromResult(new List<WindowInfo>());
        var launchCount = 0;
        Func<GuardedApp, Task<List<WindowInfo>>> launcher = _ => { launchCount++; return nextLaunch; };
        var manualCurtain = new FakeCurtain();
        var captureCurtain = new FakeCurtain();
        var window = (MainWindow)Activator.CreateInstance(typeof(MainWindow), flags, null,
            new object[] { config, captureCurtain, launcher, manualCurtain }, null)!;
        var apps = (ObservableCollection<GuardedApp>)typeof(MainWindow).GetField("_apps", flags)!.GetValue(window)!;
        object? Call(string method, params object[] args) => typeof(MainWindow).GetMethod(method, flags)!.Invoke(window, args);
        void Finish(Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!task.IsCompleted && DateTime.UtcNow < deadline)
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            check(task.IsCompletedSuccessfully, "Asynchronous protection operation completes without an exception");
        }
        foreach (var secure in new[] { false, true })
        foreach (var cancellation in new[] { "stop", "remove", "disable", "exit" })
        {
            apps.Clear();
            var app = new GuardedApp { Name = "Audit", ProcessName = "nonexistent-audit-" + Guid.NewGuid().ToString("N"), IsEnabled = true };
            apps.Add(app);
            var completion = new TaskCompletionSource<List<WindowInfo>>(); nextLaunch = completion.Task;
            var countBefore = launchCount;
            Task Begin() => (Task)(secure ? Call("SecureOpenAppAsync", app) : Call("StartProtectingAsync"))!;
            var task = Begin();
            check(!task.IsCompleted && launchCount == countBefore + 1, "Protection waits for the injected launcher");
            check(Begin().IsCompletedSuccessfully && launchCount == countBefore + 1, "Repeated clicks/hotkeys do not launch a duplicate operation");
            var opposite = (Task)(secure ? Call("StartProtectingAsync") : Call("SecureOpenAppAsync", app))!;
            check(opposite.IsCompletedSuccessfully && launchCount == countBefore + 1, "Start and secure-open operations do not overlap");
            if (cancellation == "stop") Call("StopProtecting");
            if (cancellation == "remove") apps.Remove(app);
            if (cancellation == "disable") app.IsEnabled = false;
            if (cancellation == "exit") typeof(MainWindow).GetField("_cleanupDone", flags)!.SetValue(window, true);
            var status = ((TextBlock)window.FindName("StatusText")).Text;
            completion.SetResult(new List<WindowInfo> { new() { Handle = new IntPtr(1), ProcessName = app.ProcessName } });
            Finish(task);
            check(!app.IsProtected && app.ProtectedWindows.Count == 0 && !(bool)typeof(MainWindow).GetField("_isProtecting", flags)!.GetValue(window)!, "Canceled operation cannot reactivate protection: " + cancellation);
            if (cancellation != "disable" && cancellation != "remove")
                check(((TextBlock)window.FindName("StatusText")).Text == status, "Canceled continuation preserves Stop/Exit status");
            typeof(MainWindow).GetField("_cleanupDone", flags)!.SetValue(window, false);
        }
        apps.Clear();
        var first = new GuardedApp { Name = "First", ProcessName = "audit-first", SecureHotkey = "Ctrl+Alt+9" };
        Call("TogglePrivacyCurtain");
        check(!manualCurtain.IsVisible, "Manual curtain cannot activate without a recovery hotkey");
        typeof(MainWindow).GetField("_captureCurtainHotkeyReady", flags)!.SetValue(window, true);
        Call("TogglePrivacyCurtain");
        check(manualCurtain.IsVisible, "Manual curtain can activate when recovery is available");
        Call("StopProtecting");
        check(!manualCurtain.IsVisible && !captureCurtain.IsVisible, "Stop removes both manual and automatic curtains even while idle");
        Call("TogglePrivacyCurtain"); window.PanicRestoreAll();
        check(!manualCurtain.IsVisible && !captureCurtain.IsVisible, "Panic removes a manual curtain without relying on physical-output flags");
        var second = new GuardedApp { Name = "Second", ProcessName = "audit-second", SecureHotkey = "Control+Alt+7" };
        apps.Add(first); apps.Add(second);
        foreach (var invalid in new[] { "Ctrl+Alt+Shift+H", "Control+Shift+G", "Ctrl+Alt+Unknown", "Ctrl+Alt+7" })
        {
            Call("CommitSecureHotkey", first, invalid);
            check(first.SecureHotkey == "Ctrl+Alt+9", "Rejected hotkey preserves the existing working assignment: " + invalid);
        }
        Call("CommitSecureHotkey", first, ""); Call("EnsureDefaultAppHotkeys");
        check(first.IsHotkeyDisabled && first.SecureHotkey == "", "Default assignment does not reactivate an explicitly disabled shortcut");
        Call("CommitSecureHotkey", first, "Ctrl+Alt+8");
        check(!first.IsHotkeyDisabled && first.SecureHotkey == "Ctrl+Alt+8", "A valid new shortcut can re-enable the app hotkey");
        nextLaunch = Task.FromException<List<WindowInfo>>(new IOException("Simulated launch failure"));
        Finish((Task)Call("StartProtectingAsync")!);
        check(!(bool)typeof(MainWindow).GetField("_isStartingProtection", flags)!.GetValue(window)!, "Launch failure releases the Start operation guard");
        Finish((Task)Call("SecureOpenAppAsync", first)!);
        check(((HashSet<GuardedApp>)typeof(MainWindow).GetField("_pendingSecureOpens", flags)!.GetValue(window)!).Count == 0, "Launch failure releases the per-app operation guard");
        var control = new CheckBox();
        Func<ThreatBlockerStatus> failPreference = () => throw new IOException("Simulated preference failure");
        var failedPreference = (Task<ThreatBlockerStatus?>)Call("RunPreferenceUpdateAsync", failPreference, new Control[] { control })!;
        Finish(failedPreference);
        check(failedPreference.Result == null && control.IsEnabled, "Preference failure restores disabled controls");
        var pendingPreference = new TaskCompletionSource<bool>();
        Func<ThreatBlockerStatus> latePreference = () => { pendingPreference.Task.GetAwaiter().GetResult(); return new(false, 0, 0, "Late status", DateTime.Now); };
        var late = (Task<ThreatBlockerStatus?>)Call("RunPreferenceUpdateAsync", latePreference, new Control[] { control })!;
        typeof(MainWindow).GetField("_cleanupDone", flags)!.SetValue(window, true);
        pendingPreference.SetResult(true); Finish(late);
        check(late.Result == null, "Late preference result is ignored after cleanup begins");
        typeof(MainWindow).GetField("_cleanupDone", flags)!.SetValue(window, false);
        using var source = new HwndSource(new HwndSourceParameters("Audit-owned restoration test")
        { Width = 16, Height = 16, PositionX = -32000, PositionY = -32000, WindowStyle = unchecked((int)0x80000000) });
        var state = new ProtectedWindowState { Handle = source.Handle, ProcessId = Environment.ProcessId, WrappedByStreamGuard = true, OriginalParent = new IntPtr(0x7fffffff) };
        check(!ProtectedWindowWrapperService.Restore(state) && state.WrappedByStreamGuard, "Failed native detach preserves wrapper recovery state");
        var restoreApp = new GuardedApp { Name = "Audit restore", ProcessName = "audit-owned", IsProtected = true };
        var unrelated = new GuardedApp { ProcessName = "unrelated-audit-process", WindowHandle = source.Handle };
        check(((List<WindowInfo>)Call("GetCandidateWindows", unrelated)!).Count == 0, "Reused cached HWND cannot adopt an unrelated process");
        var match = typeof(MainWindow).GetMethod("MatchesAppProcess", BindingFlags.Static | BindingFlags.NonPublic)!;
        var identity = new ProcessSafetyInfo(200, "demo", @"C:\One\demo.exe", 1, false);
        check(!(bool)match.Invoke(null, new object[] { new GuardedApp { ProcessName = "demo", ExecutablePath = @"C:\Two\demo.exe" }, identity })!, "Same-name executables at different paths are not mixed");
        check((bool)match.Invoke(null, new object[] { new GuardedApp { ProcessName = "DEMO", ExecutablePath = @"c:\one\demo.exe" }, identity })!, "Identity matching remains case-insensitive");
        restoreApp.ProtectedWindows.Add(state); apps.Clear(); apps.Add(restoreApp);
        check(!(bool)Call("RestoreApp", restoreApp)! && restoreApp.ProtectedWindows.Contains(state), "Failed restore retains the app's recovery record");
        Call("RemoveApp_Click", new Button { Tag = restoreApp }, new RoutedEventArgs());
        check(apps.Contains(restoreApp), "Remove does not discard an app whose windows failed to restore");
        window.PanicRestoreAll();
        check(!((TextBlock)window.FindName("StatusText")).Text.Contains("All applications restored"), "Panic never claims success after a restore failure");
        var position = typeof(PrivacyCurtainService).GetMethod("PositionCurtain", BindingFlags.Static | BindingFlags.NonPublic)!;
        for (var index = 0; index < 20; index++)
            check((bool)position.Invoke(null, new object[] { source.Handle, new System.Drawing.Rectangle(-32000, -32000, 16, 16) })!, "Repeated curtain positioning keeps exact physical pixel bounds");
        typeof(MainWindow).GetField("_captureScanInProgress", flags)!.SetValue(window, false);
        Finish((Task)Call("ScanCaptureRiskAsync")!);
        check(((Button)window.FindName("BtnScanCaptureTools")).IsEnabled &&
            !(bool)typeof(MainWindow).GetField("_captureScanInProgress", flags)!.GetValue(window)!, "Read-only live capture-tool scan completes and releases the scan control");
        
        // Test AutoStartShield standby logic
        apps.Clear();
        var standbyApp = new GuardedApp { Name = "Standby App", ProcessName = "nonexistent-standby-proc", IsEnabled = true };
        apps.Add(standbyApp);
        config.Save(apps, autoStartShield: true);
        typeof(MainWindow).GetField("_config", flags)!.SetValue(window, config);
        nextLaunch = Task.FromResult(new List<WindowInfo>());
        Finish((Task)Call("StartProtectingAsync")!);
        check((bool)typeof(MainWindow).GetField("_isProtecting", flags)!.GetValue(window)!, "AutoStartShield activates shield standby even when app window is not yet launched");
        check(standbyApp.Status.Contains("waiting for application window", StringComparison.OrdinalIgnoreCase) || standbyApp.Status.Contains("Chờ bảo vệ", StringComparison.OrdinalIgnoreCase), "App status reflects waiting for window: " + standbyApp.Status);
        Call("StopProtecting");
        check(!(bool)typeof(MainWindow).GetField("_isProtecting", flags)!.GetValue(window)!, "StopProtecting deactivates shield");
        config.Save(Array.Empty<GuardedApp>(), autoStartShield: false);
        typeof(MainWindow).GetField("_config", flags)!.SetValue(window, config);

        apps.Clear();
        HotkeyChords(check);
        Icons(check, source);
        DesktopChecks.Run(check);
        ((IDisposable)typeof(MainWindow).GetField("_vdService", flags)!.GetValue(window)!).Dispose();
        ((IDisposable)typeof(MainWindow).GetField("_threatBlocker", flags)!.GetValue(window)!).Dispose();
    }

    private static void HotkeyChords(Action<bool, string> check)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var hotkeys = new HotkeyService(); var plain = 0; var control = 0; var add = 0;
        typeof(HotkeyService).GetField("_shiftSgAction", flags)!.SetValue(hotkeys, (Action)(() => plain++));
        typeof(HotkeyService).GetField("_ctrlShiftSgAction", flags)!.SetValue(hotkeys, (Action)(() => control++));
        typeof(HotkeyService).GetField("_ctrlShiftAltAAction", flags)!.SetValue(hotkeys, (Action)(() => add++));
        void Key(int key, bool down) => typeof(HotkeyService).GetMethod("UpdateChordState", flags)!.Invoke(hotkeys, new object[] { key, down });
        void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Key(0xA2, true); Key(0xA3, true); Key(0xA2, false); // Right Ctrl is still held.
        Key(0xA0, true); Key(0x53, true); Key(0x47, true); Key(0x47, true); Pump();
        check(control == 1 && plain == 0, "Left/right modifier tracking and repeat suppression preserve the Ctrl chord");
        Key(0xA3, false); Pump();
        check(plain == 0, "Releasing Ctrl does not trigger a second, plain Shift chord");
        Key(0x53, false); Key(0x47, false); Key(0x53, true); Key(0x47, true); Pump();
        check(plain == 1, "A fresh plain Shift chord still works");
        Key(0x53, false); Key(0x47, false); Key(0xA2, true); Key(0xA4, true); Key(0x41, true); Key(0x41, true); Pump();
        check(add == 1, "Fallback add-app chord fires once per press");
        Key(0x41, false); Key(0x41, true); hotkeys.Dispose(); Pump();
        check(add == 1, "Queued callbacks are canceled when hotkeys are disposed");
        hotkeys.Dispose(); Key(0x47, true); Pump();
        check(control == 1 && plain == 1, "Repeated disposal and late keyboard events are harmless");
    }

    private static void Icons(Action<bool, string> check, HwndSource source)
    {
        var name = "audit-icon-" + Guid.NewGuid().ToString("N");
        check(IconHelper.GetIconForApp("", name) == null, "Missing icon initially returns no icon");
        source.AddHook((IntPtr window, int message, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (message == 0x007f) { handled = true; return System.Drawing.SystemIcons.Application.Handle; }
            return IntPtr.Zero;
        });
        var icon = IconHelper.GetIconForApp("", name, source.Handle);
        check(icon != null && icon.IsFrozen, "Window icons are frozen and a previous miss does not poison the cache");
        var width = Task.Run(() => IconHelper.GetIconForApp("", name)!.Width).GetAwaiter().GetResult();
        check(width > 0, "Cached window icon is safe to read on another thread");
        check(IconHelper.GetIconForApp(Path.Combine(Path.GetTempPath(), name + ".exe"), name) == null,
            "An icon cached by process name cannot override a different executable path");
    }

    internal static void AvailableWindowCancellation(Action<bool, string> check, string temporaryRoot)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (var fail in new[] { false, true })
        {
            var config = new ConfigService(Path.Combine(temporaryRoot, $"late-scan-{fail}.json"));
            config.Save(Array.Empty<GuardedApp>(), blockCameraAccess: false, blockMicrophoneAccess: false);
            var window = (MainWindow)Activator.CreateInstance(typeof(MainWindow), flags, null,
                new object[] { config, new FakeCurtain() }, null)!;
            using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            var calls = 0;
            Func<List<WindowInfo>> scanner = () =>
            {
                Interlocked.Increment(ref calls); started.Set(); release.Wait(5000);
                if (fail) throw new IOException("Late test scan failure");
                return new();
            };
            typeof(MainWindow).GetField("_enumerateAvailableWindows", flags)!.SetValue(window, scanner);
            var windows = (ObservableCollection<WindowInfo>)typeof(MainWindow).GetField("_availableWindows", flags)!.GetValue(window)!;
            windows.Add(new WindowInfo { ProcessName = "audit-preserved", Title = "Keep this result" });
            var statusBefore = ((TextBlock)window.FindName("StatusText")).Text;
            var scan = typeof(MainWindow).GetMethod("RefreshAvailableWindowsAsync", flags)!;
            try
            {
                var pending = (Task)scan.Invoke(window, new object[] { true })!;
                check(started.Wait(5000), "The test-owned window scan reaches its asynchronous barrier");
                typeof(MainWindow).GetField("_cleanupDone", flags)!.SetValue(window, true);
                release.Set();
                var timer = Stopwatch.StartNew();
                while (!pending.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(5))
                { Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); Thread.Sleep(10); }
                check(pending.IsCompletedSuccessfully && windows.Count == 1 && ((TextBlock)window.FindName("StatusText")).Text == statusBefore,
                    "Late scan results/errors never change a cleaned-up window: failure=" + fail);
                var ignored = (Task)scan.Invoke(window, new object[] { true })!;
                check(ignored.IsCompleted && calls == 1, "No new window scan starts after cleanup");
            }
            finally
            {
                release.Set(); typeof(MainWindow).GetMethod("CleanupCaptions", flags)!.Invoke(window, null);
                ((IDisposable)typeof(MainWindow).GetField("_vdService", flags)!.GetValue(window)!).Dispose();
                ((IDisposable)typeof(MainWindow).GetField("_threatBlocker", flags)!.GetValue(window)!).Dispose();
            }
        }
    }

    private sealed class FakeCurtain : IPrivacyCurtain
    {
        public bool IsVisible { get; private set; }
        public bool CoversAllTargetScreens => IsVisible;
        public void Show() => IsVisible = true;
        public void Hide() => IsVisible = false;
    }
}
