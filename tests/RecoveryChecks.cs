using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Newtonsoft.Json;
using StreamGuard;
using StreamGuard.Helpers;
using StreamGuard.Localization;
using StreamGuard.Models;
using StreamGuard.Services;

internal static class RecoveryChecks
{
    internal static void Run(Action<bool, string> check, string temporaryRoot)
    {
        var api = new FakeApi(); var store = new MemoryStore(); var service = new RecoveryService(store, api);
        check(service.TryPrepare((IntPtr)101, "Trình soạn thảo", out var record), "Recovery snapshot can be prepared");
        check(store.Saves == 1 && store.Json.Contains("ProcessStartedUtcTicks") && api.Restores == 0, "Original identity is durable before any mutation");
        check(!service.TryPrepare((IntPtr)101, "Duplicate", out _), "A window with a valid pending marker cannot be protected twice");
        // Simulate a crash after changing the window but BEFORE MarkProtected can run.
        var restarted = new RecoveryService(store, api);
        var pending = restarted.Records.Single();
        check(pending.IsPreviousSession && pending.State == RecoveryState.Pending, "A write-ahead snapshot survives a crash before commit");
        check(pending.ApplicationName == "Trình soạn thảo" && pending.Snapshot.Style == api.Snapshot.Style && pending.Snapshot.DesktopId == api.Snapshot.DesktopId, "Unicode, layout and desktop survive serialization");
        var outcome = restarted.Restore(pending.Id);
        check(outcome.WindowReleased && outcome.Saved && pending.State == RecoveryState.Recovered && !pending.CanRetry, "Recovery restores and commits a previous-session record");
        check(api.Restores == 1 && api.Tags.Count == 0 && new RecoveryService(store, api).PendingCount == 0, "Committed recovery removes its marker and is not offered again after restart");
        restarted.Restore(pending.Id);
        check(api.Restores == 1, "Completed recovery is idempotent");

        foreach (var mismatch in new[] { "pid", "creation", "session", "path", "marker" })
        {
            api = new(); store = new(); service = new(store, api);
            check(service.TryPrepare((IntPtr)101, "Editor", out record), "Prepare identity mismatch fixture");
            api.Snapshot = mismatch switch
            {
                "pid" => api.Snapshot with { Identity = api.Snapshot.Identity with { ProcessId = 2002 } },
                "creation" => api.Snapshot with { Identity = api.Snapshot.Identity with { ProcessStartedUtcTicks = 200 } },
                "session" => api.Snapshot with { Identity = api.Snapshot.Identity with { SessionId = 2 } },
                "path" => api.Snapshot with { Identity = api.Snapshot.Identity with { ExecutablePath = @"C:\Other\editor.exe" } },
                _ => api.Snapshot
            };
            if (mismatch == "marker") api.Tags.Clear();
            outcome = service.Restore(record!.Id);
            check(!outcome.WindowReleased && api.Restores == 0 && record.State == RecoveryState.Unverified, "Mismatch never performs restoration: " + mismatch);
            check(record.CanRetry && service.PendingCount == 1, "Unverified recovery data is retained");
        }
        api.Tags.Clear();
        check(service.TryPrepare((IntPtr)101, "New window using a reused handle", out _), "An unverified old HWND record does not block a newly identified window");

        api = new(); store = new(); service = new(store, api);
        service.TryPrepare((IntPtr)101, "Editor", out record); service.MarkProtected(record!.Id, RecoveryMethod.WindowHost);
        var previousActive = new RecoveryService(store, api).Records.Single();
        check(previousActive.State == RecoveryState.Pending && previousActive.IsPreviousSession, "Previously protected windows are pending rather than falsely managed after restart");
        api.FailRestore = true; outcome = service.Restore(record.Id);
        check(!outcome.WindowReleased && record.State == RecoveryState.Failed && record.CanRetry, "A failed restore keeps an actionable record");
        api.FailRestore = false; outcome = service.Restore(record.Id);
        check(outcome.WindowReleased && record.State == RecoveryState.Recovered && api.Restores == 2, "Retry can finish after a transient failure");

        api = new(); store = new() { FailSave = true }; service = new(store, api);
        check(!service.TryPrepare((IntPtr)101, "Editor", out _) && service.Records.Count == 0 && api.Tags.Count == 0 && api.Restores == 0,
            "Storage failure prevents protection and removes only the new marker");
        api = new(); store = new(); service = new(store, api);
        service.TryPrepare((IntPtr)101, "Editor", out record); store.FailSave = true;
        service.MarkProtected(record!.Id, RecoveryMethod.HiddenWindow);
        check(new RecoveryService(store, api).Records.Single().State == RecoveryState.Pending, "Failed status commit leaves a durable original snapshot");
        outcome = service.Restore(record.Id);
        check(outcome.WindowReleased && !outcome.Saved && record.CanRetry && api.Tags.Count == 1, "Restoration still works with unwritable storage and retains the marker for a safe retry");
        store.FailSave = false; outcome = service.Restore(record.Id);
        check(outcome.WindowReleased && outcome.Saved && api.Tags.Count == 0 && api.Restores == 1, "Retry commits recovery once file access returns without moving an already released window again");

        api = new(); store = new(); service = new(store, api); service.TryPrepare((IntPtr)101, "Editor", out record);
        api.Exists = false; outcome = service.Restore(record!.Id);
        check(outcome.WindowReleased && record.State == RecoveryState.Closed && api.Restores == 0, "A closed window is reported without fabricating a new window");
        api = new(); store = new(); service = new(store, api); service.TryPrepare((IntPtr)101, "Editor", out record);
        api.ThrowRestore = true; outcome = service.Restore(record!.Id);
        check(!outcome.WindowReleased && record.State == RecoveryState.Failed && record.CanRetry, "Unexpected restoration errors keep the recovery record");
        var brokenStore = new MemoryStore { FailLoad = true }; service = new(brokenStore, new FakeApi());
        check(!service.TryPrepare((IntPtr)101, "Editor", out _) && service.Warning != null, "Unreadable recovery data is never overwritten with an empty journal");
        brokenStore.FailLoad = false; service.Refresh();
        check(service.TryPrepare((IntPtr)101, "Editor", out _), "Refresh can recover after a journal read failure");
        var tagFailure = new FakeApi { FailTag = true }; var tagService = new RecoveryService(new MemoryStore(), tagFailure);
        check(!tagService.TryPrepare((IntPtr)101, "Editor", out _) && tagService.Records.Count == 0, "Protection cannot start if its window marker cannot be attached");
        api = new(); service = new(new MemoryStore(), api); service.TryPrepare((IntPtr)101, "Editor", out record);
        api.ThrowValidation = true; service.Refresh();
        check(record!.State == RecoveryState.Unverified, "Refresh reports verification errors without removing the record");
        api.ThrowValidation = false; service.Refresh();
        check(record.State == RecoveryState.Pending, "Refresh can re-enable verification after a transient read failure");

        // Test Delete and ClearFinished
        api = new(); store = new(); service = new(store, api);
        service.TryPrepare((IntPtr)101, "Editor 1", out var rec1);
        service.TryPrepare((IntPtr)102, "Editor 2", out var rec2);
        check(service.Records.Count == 2, "Two recovery records prepared");
        check(service.Delete(rec1!.Id) && service.Records.Count == 1 && service.Find(rec1.Id) == null, "Manual delete removes specific record");
        api.Exists = false; service.Restore(rec2!.Id);
        check(rec2.State == RecoveryState.Closed && !rec2.CanRetry, "Record 2 is closed/finished");
        check(service.ClearFinished() == 1 && service.Records.Count == 0, "ClearFinished removes closed records");

        // Verify CanDelete logic on current protected session vs finished/previous session
        api = new(); store = new(); service = new(store, api);
        service.TryPrepare((IntPtr)101, "Protected App", out var protectedRec);
        service.MarkProtected(protectedRec!.Id, RecoveryMethod.VirtualDesktop);
        check(!protectedRec.CanDelete, "Current protected session record cannot be deleted (CanDelete == false)");
        check(!service.Delete(protectedRec.Id), "service.Delete rejects deleting a currently protected session record");
        check(service.Find(protectedRec.Id) != null, "Protected record remains in recovery list");
        check(protectedRec.DeleteTooltipText.Contains("Cannot delete"), "Protected record has Cannot delete tooltip");

        // Previous session record marked protected can be deleted
        var prevStore = new MemoryStore();
        var prevApi = new FakeApi();
        var prepSvc = new RecoveryService(prevStore, prevApi);
        prepSvc.TryPrepare((IntPtr)101, "Editor", out var pRec);
        var restartedSvc = new RecoveryService(prevStore, prevApi);
        var prevRecord = restartedSvc.Records.Single();
        check(prevRecord.IsPreviousSession && prevRecord.CanDelete, "Previous session record can be deleted");
        check(restartedSvc.Delete(prevRecord.Id), "restartedSvc.Delete succeeds on previous session record");
        check(restartedSvc.Records.Count == 0, "Previous session record removed from recovery list");

        // After restoration, record can be deleted
        api.Exists = true;
        service.Restore(protectedRec.Id);
        check(protectedRec.State == RecoveryState.Recovered, "Record restored");
        check(protectedRec.CanDelete, "Restored session record can be deleted (CanDelete == true)");
        check(service.Delete(protectedRec.Id), "service.Delete succeeds on restored session record");
        check(service.Find(protectedRec.Id) == null, "Restored record removed from recovery list");

        FileJournal(check, temporaryRoot);
        Native(check, temporaryRoot);
        ActualCrash(check, temporaryRoot);
    }

    private static void FileJournal(Action<bool, string> check, string temporaryRoot)
    {
        var directory = Path.Combine(temporaryRoot, "recovery-files"); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "windows.json"); var store = new RecoveryJournalStore(path);
        check(store.Load().Count == 0, "A missing recovery journal starts empty");
        var service = new RecoveryService(store, new FakeApi());
        check(service.TryPrepare((IntPtr)101, "Ứng dụng", out var record), "Atomic file journal prepare");
        service.MarkProtected(record!.Id, RecoveryMethod.VirtualDesktop);
        check(store.Load().Single().Method == RecoveryMethod.VirtualDesktop && File.Exists(path + ".bak"), "File journal and backup round-trip");
        var before = File.ReadAllBytes(path);
        using (var locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { store.Save(Array.Empty<RecoveryRecord>()); check(false, "Locked journal unexpectedly saved"); }
            catch (IOException) { check(true, "Locked journal reports failure"); }
        }
        check(before.SequenceEqual(File.ReadAllBytes(path)) && !Directory.EnumerateFiles(directory, "*.tmp").Any(), "Failed atomic save preserves bytes and cleans its temporary file");
        File.WriteAllText(path, "not valid JSON");
        check(store.Load().Count == 1 && store.Warning != null, "Recovery journal falls back to a valid backup");
        var backup = File.ReadAllText(path + ".bak"); store.Save(new[] { record });
        check(File.ReadAllText(path + ".bak") == backup && Directory.EnumerateFiles(directory, "windows.json.corrupt-*").Any(), "Corrupt journal is preserved without replacing its good backup");
        foreach (var bad in new[] { "null", "[]", "{\"Version\":99,\"Records\":[]}", "{\"Version\":1,\"Records\":[null]}" })
        {
            File.WriteAllText(path, bad); File.Delete(path + ".bak");
            try { store.Load(); check(false, "Invalid recovery journal was accepted"); }
            catch (IOException) { check(true, "Invalid journal shape/version fails closed"); }
        }
    }

    private static void Native(Action<bool, string> check, string temporaryRoot)
    {
        using var desktops = new VirtualDesktopService();
        using var source = new HwndSource(new HwndSourceParameters("Recovery-owned native check")
        { Width = 160, Height = 80, PositionX = -32000, PositionY = -32000, WindowStyle = unchecked((int)0x80000000) });
        var blockedSelf = new WindowsRecoveryApi(desktops);
        check(!blockedSelf.TryCapture(source.Handle, out _, out _), "Recovery cannot capture its own application window by default");
        var native = new WindowsRecoveryApi(desktops, -1); // Allow only this test-owned process for the integration check.
        var path = Path.Combine(temporaryRoot, "native-recovery.json");
        var store = new RecoveryJournalStore(path); var service = new RecoveryService(store, native);
        check(service.TryPrepare(source.Handle, "Native test", out var record), "Native identity, placement and window marker can be journaled");
        check(native.Validate(record!).State == RecoveryValidation.Valid, "Native marker validation succeeds for the original window");
        var wrongTime = new RecoveryRecord { Id = record!.Id, Token = record.Token, Snapshot = record.Snapshot with { Identity = record.Snapshot.Identity with { ProcessStartedUtcTicks = 1 } } };
        check(native.Validate(wrongTime).State == RecoveryValidation.Unverified && !native.TryRestore(wrongTime, out _), "Native recovery rejects a reused PID/process instance");
        native.RemoveTag(record);
        check(native.Validate(record).State == RecoveryValidation.Unverified && !native.TryRestore(record, out _), "Native recovery rejects a missing HWND marker even when PID still matches");
        check(native.TryTag(record, out _), "Native marker can be restored for the owned test fixture");
        using var host = new HwndSource(new HwndSourceParameters("Recovery-owned host check")
        { Width = 160, Height = 80, PositionX = -32000, PositionY = -32000, WindowStyle = unchecked((int)0x80000000) });
        SetTestStyle(source.Handle, -16, (nint)((record.Snapshot.Style | 0x40000000 | 0x10000000) & ~0x80000000L));
        SetTestParent(source.Handle, host.Handle);
        var restarted = new RecoveryService(store, native);
        var outcome = restarted.Restore(record.Id);
        check(outcome.WindowReleased && outcome.Saved && restarted.Find(record.Id)!.State == RecoveryState.Recovered && GetTestParent(source.Handle) == IntPtr.Zero,
            $"Previous-session journal detaches and restores only the test-owned native HWND: outcome={outcome}, state={restarted.Find(record.Id)!.State}, detail={restarted.Find(record.Id)!.Detail}, parent={GetTestParent(source.Handle)}, wantedStyle={record.Snapshot.Style:X}, actualStyle={GetTestStyle(source.Handle, -16):X}");
        using var dialog = new HwndSource(new HwndSourceParameters("Recovery-owned dialog check")
        { Width = 160, Height = 80, PositionX = -32000, PositionY = -32000, WindowStyle = unchecked((int)0x80000000), ParentWindow = host.Handle });
        check(service.TryPrepare(dialog.Handle, "Owned dialog", out var owned) && owned!.Snapshot.Owner?.Window == host.Handle.ToInt64(),
            "A popup owner is captured separately from its desktop parent");
        SetTestStyle(dialog.Handle, -8, IntPtr.Zero);
        check(native.Validate(owned!).State == RecoveryValidation.Valid && service.Restore(owned!.Id).WindowReleased && GetTestParent(dialog.Handle) == host.Handle,
            "Recovery restores the original marked popup owner without treating it as a child parent");

        // GPU/Chromium/Electron windows (e.g. Antigravity) must never be SetParent-wrapped
        check(MainWindow.IsGpuCompositedClassName("Chrome_WidgetWin_1") &&
              MainWindow.IsGpuCompositedClassName("Chrome_WidgetWin_0") &&
              MainWindow.IsGpuCompositedClassName("MozillaWindowClass") &&
              !MainWindow.IsGpuCompositedClassName("Notepad"),
            "IsGpuCompositedClassName identifies Chromium/Electron/GPU window classes so SetParent wrapping is bypassed");
    }

    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "SetParent")] private static extern IntPtr SetTestParent(IntPtr window, IntPtr parent);
    [DllImport("user32.dll", EntryPoint = "GetParent")] private static extern IntPtr GetTestParent(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetTestStyle(IntPtr window, int index, nint value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetTestStyle(IntPtr window, int index);
    internal static int RunCrashFixture(string[] args)
    {
        var window = new IntPtr(long.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture));
        using var desktops = new VirtualDesktopService();
        var service = new RecoveryService(new RecoveryJournalStore(args[2]), new WindowsRecoveryApi(desktops));
        if (!service.TryPrepare(window, "Test-owned crash window", out var record)) return 12;
        ShowWindowAsync(window, 0);
        Console.WriteLine("RECOVERY_READY:" + record!.Id); Console.Out.Flush();
        Thread.Sleep(30000); // The parent deliberately terminates only this owned fixture process.
        return 0;
    }
    private static void ActualCrash(Action<bool, string> check, string temporaryRoot)
    {
        using var source = new HwndSource(new HwndSourceParameters("Recovery-owned crash check")
        { Width = 160, Height = 80, PositionX = -32000, PositionY = -32000, WindowStyle = unchecked((int)0x90000000) });
        var path = Path.Combine(temporaryRoot, "crashed-writer.recovery.json");
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(info.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) info.ArgumentList.Add(typeof(RecoveryChecks).Assembly.Location);
        info.ArgumentList.Add("--recovery-crash-fixture"); info.ArgumentList.Add(source.Handle.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture)); info.ArgumentList.Add(path);
        using var child = Process.Start(info)!;
        var stderr = child.StandardError.ReadToEndAsync();
        try
        {
            var line = child.StandardOutput.ReadLineAsync();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!line.IsCompleted && DateTime.UtcNow < deadline)
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Thread.Sleep(10);
            }
            check(line.IsCompleted && line.Result?.StartsWith("RECOVERY_READY:") == true, "Separate writer durably journals the parent-owned test HWND before termination");
            child.Kill(entireProcessTree: false); child.WaitForExit(3000);
            check(child.HasExited && child.ExitCode != 0, "The owned journal writer is terminated without graceful application cleanup");
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            check(!IsWindowVisible(source.Handle), "The fixture hid only its parent-owned off-screen test window");
            using var desktops = new VirtualDesktopService();
            var restart = new RecoveryService(new RecoveryJournalStore(path), new WindowsRecoveryApi(desktops, -1));
            var pending = restart.Records.Single();
            check(pending.IsPreviousSession && pending.State == RecoveryState.Pending, "An actual terminated writer leaves an actionable previous-session snapshot");
            var result = restart.Restore(pending.Id);
            check(result.WindowReleased && result.Saved && IsWindowVisible(source.Handle), "The test window is restored after the original writer process has died");
        }
        finally
        {
            if (!child.HasExited) { child.Kill(entireProcessTree: false); child.WaitForExit(3000); }
            stderr.Wait(1000);
        }
    }

    internal static void Ui(Action<bool, string> check, string temporaryRoot)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var api = new FakeApi(); var store = new MemoryStore(); var first = new RecoveryService(store, api);
        first.TryPrepare((IntPtr)101, "Trình soạn thảo", out var record); first.MarkProtected(record!.Id, RecoveryMethod.HiddenWindow);
        api.FailRestore = true; first.Restore(record.Id);
        var recovery = new RecoveryService(store, api);
        var config = new ConfigService(Path.Combine(temporaryRoot, "recovery-ui-config.json"));
        config.Save(Array.Empty<GuardedApp>(), blockCameraAccess: false, blockMicrophoneAccess: false);
        var window = (MainWindow)Activator.CreateInstance(typeof(MainWindow), flags, null,
            new object?[] { config, new Curtain(), null, new Curtain(), recovery }, null)!;
        check(((RadioButton)window.FindName("TabRecovery")).IsChecked == true, "Unfinished previous-session recovery opens its center");
        check(ReferenceEquals(((ListBox)window.FindName("RecoveryListBox")).ItemsSource, recovery.Records), "Recovery center lists the actual journal records");
        foreach (var language in new[] { "en", "vi" })
        {
            ((ComboBox)window.FindName("LanguageComboBox")).SelectedValue = language;
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            check(((TextBlock)window.FindName("TabRecoveryText")).Text == (language == "vi" ? "Trung tâm khôi phục" : "Recovery Center"), "Recovery navigation follows the selected language");
            foreach (var view in new[] { "ViewProtected", "ViewAvailable", "ViewThreats", "ViewSettings", "ViewRecovery" })
            { var element = (FrameworkElement)window.FindName(view); element.BeginAnimation(UIElement.OpacityProperty, null); element.Opacity = 1; }
            foreach (var width in new[] { 860, 640 })
                typeof(Program).GetMethod("Render", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { window, language + "-Recovery", width, 600 });
            typeof(Program).GetMethod("Render", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { window, language + "-Recovery-compact", 640, 460 });
        }
        var loaded = recovery.Find(record.Id)!;
        var retry = typeof(MainWindow).GetMethod("BtnRecoveryRetry_Click", flags)!;
        retry.Invoke(window, new object[] { new Button { Tag = loaded }, new RoutedEventArgs() });
        check(loaded.State == RecoveryState.Failed && loaded.CanRetry, "UI retry keeps a failed item actionable");
        api.FailRestore = false;
        var apps = (ObservableCollection<GuardedApp>)typeof(MainWindow).GetField("_apps", flags)!.GetValue(window)!;
        var app = new GuardedApp { Name = "Editor", ProcessName = "editor", IsProtected = true };
        app.ProtectedWindows.Add(new() { Handle = (IntPtr)101, RecoveryId = loaded.Id }); apps.Add(app);
        retry.Invoke(window, new object[] { new Button { Tag = loaded }, new RoutedEventArgs() });
        check(loaded.State == RecoveryState.Recovered && !loaded.CanRetry && app.ProtectedWindows.Count == 0 && !app.IsProtected,
            "Successful per-window retry releases its live state and disables its button");
        check(!((Button)window.FindName("BtnRecoverAll")).IsEnabled, "Recover-all is disabled after the final pending record is released");
        recovery.TryPrepare((IntPtr)101, "Second window", out var second); recovery.MarkProtected(second!.Id, RecoveryMethod.VirtualDesktop);
        typeof(MainWindow).GetMethod("BtnRecoverAll_Click", flags)!.Invoke(window, new object[] { window, new RoutedEventArgs() });
        check(second.State == RecoveryState.Recovered && recovery.PendingCount == 0, "Recover-all processes the pending entries through the same verified restore path");
        recovery.TryPrepare((IntPtr)101, "Closed window", out var closed); api.Exists = false;
        typeof(MainWindow).GetMethod("BtnRecoveryRefresh_Click", flags)!.Invoke(window, new object[] { window, new RoutedEventArgs() });
        check(closed!.State == RecoveryState.Closed && !closed.CanRetry, "UI refresh recognizes a window that closed after being journaled");
        check(((Button)window.FindName("BtnClearFinishedRecovery")).IsEnabled, "Clear-finished button is enabled when finished records exist");
        var deleteMethod = typeof(MainWindow).GetMethod("BtnDeleteRecoveryRecord_Click", flags)!;
        deleteMethod.Invoke(window, new object[] { new Button { Tag = closed }, new RoutedEventArgs() });
        check(recovery.Find(closed.Id) == null, "UI delete button removes individual record");

        // Verify UI delete button logic for protected records
        api.Exists = true;
        recovery.TryPrepare((IntPtr)103, "Live Protected App", out var liveRec);
        recovery.MarkProtected(liveRec!.Id, RecoveryMethod.VirtualDesktop);
        check(!liveRec.CanDelete, "Live protected app has CanDelete == false");
        deleteMethod.Invoke(window, new object[] { new Button { Tag = liveRec }, new RoutedEventArgs() });
        check(recovery.Find(liveRec.Id) != null, "UI delete button cannot delete a currently protected window session");
        // Restore liveRec so it can be cleaned up
        recovery.Restore(liveRec.Id);
        check(liveRec.CanDelete, "After restoration, CanDelete is true");
        deleteMethod.Invoke(window, new object[] { new Button { Tag = liveRec }, new RoutedEventArgs() });
        check(recovery.Find(liveRec.Id) == null, "UI delete button successfully deletes restored record");
        var clearFinishedMethod = typeof(MainWindow).GetMethod("BtnClearFinishedRecovery_Click", flags)!;
        clearFinishedMethod.Invoke(window, new object[] { window, new RoutedEventArgs() });
        check(!((Button)window.FindName("BtnClearFinishedRecovery")).IsEnabled, "Clear-finished button is disabled when no finished records remain");
        ((IDisposable)typeof(MainWindow).GetField("_vdService", flags)!.GetValue(window)!).Dispose();
        ((IDisposable)typeof(MainWindow).GetField("_threatBlocker", flags)!.GetValue(window)!).Dispose();
        LocalizationService.Current.SetLanguage("en");
    }
    private sealed class Curtain : IPrivacyCurtain
    {
        public bool IsVisible => false;
        public bool CoversAllTargetScreens => false;
        public void Show() { }
        public void Hide() { }
    }
    private sealed class MemoryStore : IRecoveryJournalStore
    {
        public string Json = "[]";
        public bool FailSave, FailLoad;
        public int Saves;
        public string? Warning => null;
        public IReadOnlyList<RecoveryRecord> Load() => FailLoad ? throw new IOException("Simulated read failure") : JsonConvert.DeserializeObject<List<RecoveryRecord>>(Json)!;
        public void Save(IEnumerable<RecoveryRecord> records)
        {
            if (FailSave) throw new IOException("Simulated write failure");
            Saves++; Json = JsonConvert.SerializeObject(records);
        }
    }
    private sealed class FakeApi : IRecoveryWindowApi
    {
        public RecoverySnapshot Snapshot = new()
        {
            Identity = new(101, 1001, 100, 1, "editor", @"C:\Apps\editor.exe"), WindowTitle = "Unsaved document — Tiếng Việt",
            Left = 100, Top = 100, Right = 800, Bottom = 600, Style = 0x10cf0000, ExStyle = 0x40000, ShowCommand = 1,
            DesktopId = Guid.Parse("08d5ae1b-cd2d-473e-8f72-ceac8c040c77")
        };
        public Dictionary<Guid, long> Tags = new();
        public bool Exists = true, FailRestore, ThrowRestore, FailTag, ThrowValidation;
        public int Restores;
        public bool TryCapture(IntPtr window, out RecoverySnapshot snapshot, out string error) { snapshot = Snapshot; error = ""; return Exists; }
        public bool TryTag(RecoveryRecord record, out string error) { error = ""; if (FailTag) return false; Tags[record.Id] = record.Token; return true; }
        public void RemoveTag(RecoveryRecord record) => Tags.Remove(record.Id);
        public RecoveryCheck Validate(RecoveryRecord record)
        {
            if (ThrowValidation) throw new IOException("Simulated verification error");
            return !Exists ? new(RecoveryValidation.Closed) :
                record.Snapshot.Identity != Snapshot.Identity || !Tags.TryGetValue(record.Id, out var value) || value != record.Token
                    ? new(RecoveryValidation.Unverified, "The original process, window marker or parent no longer matches. No action was taken.") : new(RecoveryValidation.Valid);
        }
        public bool TryRestore(RecoveryRecord record, out string error)
        {
            Restores++;
            if (ThrowRestore) throw new IOException("Simulated native failure");
            error = "Windows could not restore this window.";
            return !FailRestore && Validate(record).State == RecoveryValidation.Valid;
        }
    }
}
