using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StreamGuard;
using StreamGuard.Localization;
using StreamGuard.Models;
using StreamGuard.Services;

internal static class CaptionChecks
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int identifier);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    internal static ProcessStartInfo Child(string mode)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!);
        if (Path.GetFileNameWithoutExtension(info.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) info.ArgumentList.Add(typeof(CaptionChecks).Assembly.Location);
        info.ArgumentList.Add("--captions-worker-fixture"); info.ArgumentList.Add(mode); return info;
    }
    internal static void Wait(Func<bool> condition, string description, int milliseconds = 10000)
    {
        var until = Stopwatch.StartNew();
        while (!condition() && until.ElapsedMilliseconds < milliseconds) Thread.Sleep(15);
        if (!condition()) throw new InvalidOperationException("Caption test timed out: " + description);
    }
    internal static int Fixture(string[] args)
    {
        if (args[0] == "--captions-owner-fixture")
        {
            using var owner = new CaptionService(args[1], _ => Task.FromResult(Child("unresponsive")));
            owner.Start(); Wait(() => owner.State == CaptionState.Running, "owned worker starts");
            Console.WriteLine("OWNER_READY:" + owner.WorkerProcessId); Console.Out.Flush(); Thread.Sleep(30000); return 0;
        }
        var mode = args[1];
        if (mode == "malformed") { Console.WriteLine("{broken"); Console.Out.Flush(); Thread.Sleep(30000); return 0; }
        if (mode == "oversized") { Console.WriteLine(new string('x', 70000)); Console.Out.Flush(); Thread.Sleep(30000); return 0; }
        if (mode == "unterminated") { Console.Write(new string('x', 70000)); Console.Out.Flush(); Thread.Sleep(30000); return 0; }
        if (mode == "stderr") { Console.Error.Write(new string('e', 256000)); Console.Error.Flush(); }
        void Emit(object value) { Console.WriteLine(JsonSerializer.Serialize(value)); Console.Out.Flush(); }
        Emit(new { type = "ready" });
        if (mode == "closed-pipe") { CloseHandle(GetStdHandle(-11)); Thread.Sleep(30000); return 0; }
        if (mode == "crash") return 23;
        Emit(new { type = "segment", text = "Hello — tiếng Việt", start = 0.0, end = 1.0 });
        Emit(new { type = "level", value = 4.0 });
        if (mode == "unresponsive") { Thread.Sleep(30000); return 0; }
        if (mode == "bad-segment") Emit(new { type = "segment", text = "bad", start = -10, end = -20 });
        if (mode == "error") { Emit(new { type = "error", message = "Simulated device error" }); return 1; }
        if (mode == "error-hang")
        {
            Emit(new { type = "error", message = "Device disconnected; worker failed to exit" });
            Emit(new { type = "ready" });
            Thread.Sleep(30000); return 0;
        }
        while (Console.ReadLine() is { } line)
        {
            using var doc = JsonDocument.Parse(line); var command = doc.RootElement.GetProperty("command").GetString();
            if (command == "stop") { Emit(new { type = "stopped" }); return 0; }
            if (command == "pause")
            {
                if (mode == "delayed-pause") Thread.Sleep(200);
                var value = doc.RootElement.GetProperty("paused").GetBoolean(); Emit(new { type = "paused", value });
                Emit(new { type = "segment", text = value ? "MUST BE IGNORED" : "Resumed speech", start = 2.0, end = 3.0 });
            }
        }
        return 0;
    }
    internal static void Run(Action<bool, string> check, string temporaryRoot)
    {
        var directory = Path.Combine(temporaryRoot, "captions"); Directory.CreateDirectory(directory);
        static string ReadLive(string path)
        { using var input = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)); return input.ReadToEnd(); }
        check(CaptionTranscriptWriter.Timestamp(59.9999) == "00:01:00,000" && CaptionTranscriptWriter.Timestamp(-1) == "00:00:00,000", "Subtitle milliseconds carry correctly and negative times clamp");
        using (var one = new CaptionTranscriptWriter(directory))
        using (var two = new CaptionTranscriptWriter(directory))
        {
            check(one.TextPath != two.TextPath && one.SubtitlePath != two.SubtitlePath, "Simultaneous caption sessions cannot share transcript files");
            one.Write("Tiếng Việt\r\nsecond line", 0, 0);
            check(ReadLive(one.TextPath).Contains("Tiếng Việt  second line") && ReadLive(one.SubtitlePath).Contains("00:00:00,000 --> 00:00:00,500"), "UTF-8 transcripts flush and sanitize subtitle payload newlines");
        }
        var settingsPath = Path.Combine(directory, "settings.json");
        File.WriteAllText(settingsPath, "broken"); check(CaptionSettings.Load(settingsPath).FontSize == 18, "Invalid subtitle settings fall back without changing main config");
        var settings = new CaptionSettings { FontSize = 100, Left = double.PositiveInfinity, Top = -200000, SingleLine = true, ExcludeFromCapture = false };
        check(settings.Save(settingsPath), "Subtitle preferences save atomically");
        var read = CaptionSettings.Load(settingsPath);
        check(read.FontSize == 36 && read.Left == null && read.Top == null && read.SingleLine && !read.ExcludeFromCapture, "Subtitle settings clamp invalid font and coordinates");
        check(!new CaptionSettings().Save(directory), "Caption preferences report an unwritable target");
        try { CaptionRuntime.SafeEntryPath(directory, "../escape.exe"); check(false, "Runtime zip accepted a traversal"); } catch (IOException) { check(true, "Runtime zip traversal is rejected"); }
        check(ProcessSafetyService.IsProtectedIdentity("python", Path.Combine(CaptionRuntime.Root, "version", "python.exe")), "Internal caption worker is excluded from managed-process operations");
        ToeicFormatterChecks(check);

        var texts = new ConcurrentQueue<string>(); var levels = new ConcurrentQueue<double>();
        using (var service = new CaptionService(directory, _ => Task.FromResult(Child("stderr"))))
        {
            service.Segment += segment => texts.Enqueue(segment.Text); service.Level += value => levels.Enqueue(value);
            check(service.State == CaptionState.Stopped && service.WorkerProcessId == null, "Caption creation never starts audio automatically");
            check(service.Start() && !service.Start(), "Concurrent starts cannot create multiple workers");
            Wait(() => texts.Contains("Hello — tiếng Việt"), "UTF-8 segment arrives");
            Wait(() => !levels.IsEmpty, "meter arrives");
            check(service.State == CaptionState.Running && levels.All(value => value is >= 0 and <= 1), "Worker status and audio meter values are bounded while stderr drains");
            check(service.SetPaused(true), "Caption pause sends an isolated command"); Wait(() => service.State == CaptionState.Paused, "pause acknowledgement");
            Thread.Sleep(80); check(!texts.Contains("MUST BE IGNORED"), "Segments received while paused never reach UI or files");
            check(service.SetPaused(false), "Caption resume sends a command"); Wait(() => texts.Contains("Resumed speech"), "resumed segment");
            service.StopAsync().GetAwaiter().GetResult();
            check(service.State == CaptionState.Stopped && service.WorkerProcessId == null, "Normal stop releases process and file handles");
            check(File.ReadAllText(service.LastTranscriptPath!).Contains("Resumed speech"), "Transcript is readable after stop");
            check(service.Start(), "Captions can start again after stop"); Wait(() => service.State == CaptionState.Running, "restart"); service.StopAsync().GetAwaiter().GetResult();
        }
        foreach (var mode in new[] { "malformed", "oversized", "unterminated", "closed-pipe", "bad-segment", "error", "crash" })
        {
            using var service = new CaptionService(directory, _ => Task.FromResult(Child(mode)));
            service.Start(); Wait(() => service.State == CaptionState.Faulted, "worker error " + mode);
            service.StopAsync().GetAwaiter().GetResult();
            check(service.WorkerProcessId == null, "Caption failure cleans its process: " + mode);
        }
        using (var service = new CaptionService(directory, _ => Task.FromResult(Child("unresponsive"))))
        {
            service.Start(); Wait(() => service.State == CaptionState.Running, "unresponsive worker"); var timer = Stopwatch.StartNew();
            for (var i = 0; i < 10000; i++) service.SetPaused(i % 2 == 0);
            check(timer.Elapsed < TimeSpan.FromSeconds(2), "A full worker input pipe never blocks the caller or accumulates unbounded commands");
            service.StopAsync().GetAwaiter().GetResult();
            check(timer.Elapsed < TimeSpan.FromSeconds(8) && service.State == CaptionState.Stopped, "Stop forcibly releases only its owned unresponsive worker after the grace period");
        }
        using (var service = new CaptionService(directory, async cancellation => { await Task.Delay(30000, cancellation); return Child("normal"); }))
        {
            service.Start(); service.StopAsync().GetAwaiter().GetResult();
            check(service.State == CaptionState.Stopped && service.WorkerProcessId == null, "Stop during runtime preparation never launches an orphan worker");
        }
        using (var service = new CaptionService(directory, _ => throw new IOException("Runtime preparation failed")))
        {
            service.Start(); Wait(() => service.State == CaptionState.Faulted, "runtime failure");
            check(service.Detail.Contains("Runtime preparation failed"), "Runtime preparation failure is visible and isolated");
        }
        CrashOwner(check, directory);
        using (var service = new CaptionService(directory, _ => Task.FromResult(Child("error-hang"))))
        {
            service.Start();
            Wait(() => service.State == CaptionState.Faulted && service.WorkerProcessId == null,
                "a reported terminal error must release its worker without a manual stop", 5000);
            check(service.Detail.Contains("Device disconnected"), "Terminal worker diagnostics survive cleanup and later messages");
            check(!service.SetPaused(false), "A failed worker never accepts further pause commands");
        }
        RealModel(check, directory);
    }
    private static void CrashOwner(Action<bool, string> check, string directory)
    {
        var info = Child("unused"); info.ArgumentList[info.ArgumentList.Count - 2] = "--captions-owner-fixture"; info.ArgumentList[info.ArgumentList.Count - 1] = directory;
        info.UseShellExecute = false; info.CreateNoWindow = true; info.RedirectStandardOutput = true; info.RedirectStandardError = true;
        using var owner = Process.Start(info)!;
        var errors = owner.StandardError.ReadToEndAsync(); var line = owner.StandardOutput.ReadLineAsync();
        Process? worker = null;
        try
        {
            check(line.Wait(10000) && line.Result?.StartsWith("OWNER_READY:") == true, "Owned crash fixture reports its private caption worker");
            worker = Process.GetProcessById(int.Parse(line.Result!["OWNER_READY:".Length..]));
            owner.Kill(entireProcessTree: false); owner.WaitForExit(3000);
            check(worker.WaitForExit(5000), "The worker job closes when its owner crashes; no audio helper remains running");
        }
        finally { if (!owner.HasExited) owner.Kill(entireProcessTree: true); worker?.Dispose(); }
    }
    private static void RealModel(Action<bool, string> check, string directory)
    {
        var segments = new ConcurrentQueue<string>();
        using var service = new CaptionService(directory, async cancellation =>
        {
            var info = await CaptionRuntime.CreateStartInfoAsync(cancellation);
            info.ArgumentList.Add("--file-test"); info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "caption-english.wav")); return info;
        });
        service.Segment += segment => segments.Enqueue(segment.Text);
        service.Start();
        Wait(() => segments.Any() || service.State == CaptionState.Faulted, "packaged offline speech recognition", 60000);
        check(segments.Any(text => text.Contains("test", StringComparison.OrdinalIgnoreCase)) && service.State != CaptionState.Faulted,
            "Packaged CPU model recognizes the synthetic English test WAV offline: " + service.Detail);
        service.StopAsync().GetAwaiter().GetResult();
        check(File.ReadAllText(service.LastTranscriptPath!).Contains("Hello", StringComparison.OrdinalIgnoreCase), "Real offline speech reaches the integrated transcript writer");
    }
    internal static void Ui(MainWindow window, Action<bool, string> check, string temporaryRoot)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var service = (CaptionService)typeof(MainWindow).GetField("_captions", flags)!.GetValue(window)!;
        check(service.State == CaptionState.Stopped, "Rendering and navigating captions never starts playback capture");
        var config = (ConfigService)typeof(MainWindow).GetField("_config", flags)!.GetValue(window)!;
        var mainConfigPath = (string)typeof(ConfigService).GetField("ConfigPath", flags)!.GetValue(config)!;
        var before = File.ReadAllText(mainConfigPath);
        var single = (CheckBox)window.FindName("CaptionSingleLineCheckBox"); single.IsChecked = true;
        var exclude = (CheckBox)window.FindName("CaptionCaptureCheckBox"); exclude.IsChecked = false;
        check(CaptionSettings.Load(config.CaptionSettingsPath).SingleLine && !CaptionSettings.Load(config.CaptionSettingsPath).ExcludeFromCapture, "Caption options persist separately from privacy settings");
        check(File.ReadAllText(mainConfigPath) == before, "Subtitle preferences preserve app list, protection, language and hotkey configuration");
        using (var settingsLock = File.Open(config.CaptionSettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            single.IsChecked = false;
            typeof(MainWindow).GetMethod("UpdateCaptionUi", flags)!.Invoke(window, null);
            check(((TextBlock)window.FindName("CaptionErrorText")).Text == LocalizationService.Current["Captions.SaveError"],
                "A failed preference save remains visible across status refreshes");
        }
        single.IsChecked = true;
        check(((TextBlock)window.FindName("CaptionErrorText")).Visibility == Visibility.Collapsed,
            "A successful subsequent preference save clears the warning");
        var settings = new CaptionSettings();
        var overlay = new CaptionOverlayWindow(settings);
        HoverFeedbackChecks.Run(overlay, check);
        var overlayPause = (Button)typeof(CaptionOverlayWindow).GetField("_pauseAction", flags)!.GetValue(overlay)!;
        var overlayStop = (Button)typeof(CaptionOverlayWindow).GetField("_stopAction", flags)!.GetValue(overlay)!;
        var overlayCopy = (Button)typeof(CaptionOverlayWindow).GetField("_copyAction", flags)!.GetValue(overlay)!;
        overlay.SetStatus(CaptionState.Stopped);
        check(!overlayPause.IsEnabled && !overlayStop.IsEnabled && !overlayCopy.IsEnabled, "An idle empty overlay disables actions that cannot do anything");
        overlay.SetStatus(CaptionState.Starting); check(!overlayPause.IsEnabled && overlayStop.IsEnabled, "A starting overlay can be stopped but cannot be paused before ready");
        new WindowInteropHelper(overlay).EnsureHandle();
        overlay.UpdateSubtitle("First caption"); overlay.UpdateSubtitle("Second caption — Tiếng Việt"); overlay.SetStatus(CaptionState.Running); overlay.UpdateLevel(.7);
        check(overlayPause.IsEnabled && overlayStop.IsEnabled && overlayCopy.IsEnabled, "Overlay actions become available when their content and session are ready");
        settings.EnableTranslation = true;
        settings.TranslationMode = "Bilingual";
        overlay.ApplySettings();
        var prevBox = (TextBlock)typeof(CaptionOverlayWindow).GetField("_previous", flags)!.GetValue(overlay)!;
        var currBox = (TextBlock)typeof(CaptionOverlayWindow).GetField("_current", flags)!.GetValue(overlay)!;
        overlay.UpdateSubtitle("Rapid English phrase", null);
        check(prevBox.Text == "Rapid English phrase" && currBox.Text == "",
            "Bilingual mode displays English on top line immediately with 0ms delay");
        overlay.UpdateTranslation("Rapid English phrase", "Cụm từ tiếng Anh nhanh");
        check(prevBox.Text == "Rapid English phrase" && currBox.Text == "Cụm từ tiếng Anh nhanh",
            "Bilingual mode fills bottom line with Vietnamese translation");

        settings.TranslationMode = "VietnameseOnly";
        overlay.ApplySettings();
        overlay.UpdateSubtitle("Second phrase", null);
        check(currBox.Text == "Second phrase",
            "VietnameseOnly mode shows English immediately without blank delay");
        overlay.UpdateTranslation("Second phrase", "Cụm từ thứ hai");
        check(currBox.Text == "Cụm từ thứ hai",
            "VietnameseOnly mode replaces English with Vietnamese translation");

        overlay.UpdateSubtitle("Third phrase", null);
        check(prevBox.Text == "Cụm từ thứ hai" && currBox.Text == "Third phrase",
            "VietnameseOnly mode moves previous Vietnamese translation to top line and shows new English on bottom line");
        overlay.UpdateTranslation("Third phrase", "Cụm từ thứ ba");
        check(prevBox.Text == "Cụm từ thứ hai" && currBox.Text == "Cụm từ thứ ba",
            "VietnameseOnly mode replaces bottom line with new Vietnamese translation keeping top line Vietnamese");
        settings.EnableTranslation = false;
        overlay.ApplySettings();
        overlay.SetClickThrough(true); check(overlay.IsClickThrough, "Overlay supports mouse passthrough without a global shortcut");
        overlay.SetClickThrough(false); settings.SingleLine = true; settings.FontSize = 36; overlay.ApplySettings();
        check(!overlay.IsClickThrough && settings.FontSize == 36, "Main controls can unlock and resize subtitle text");
        settings.SingleLine = false; settings.FontSize = 18; overlay.ApplySettings();
        foreach (var language in new[] { "vi", "en" })
        {
            LocalizationService.Current.SetLanguage(language);
            check(((TextBlock)window.FindName("TabCaptionsText")).Text == (language == "vi" ? "Phụ đề trực tiếp" : "Live Captions"), "Caption navigation follows live language switching");
            var content = (FrameworkElement)overlay.Content; content.Measure(new Size(760, 160)); content.Arrange(new Rect(0, 0, 760, 160)); content.UpdateLayout();
            var bitmap = new RenderTargetBitmap(760, 160, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(temporaryRoot, "caption-overlay-" + language + ".png")); png.Save(file);
        }
        using var liveWriter = new CaptionTranscriptWriter(service.OutputDirectory);
        typeof(CaptionService).GetProperty("LastTranscriptPath")!.SetValue(service, liveWriter.TextPath);
        var activeId = Path.GetFileNameWithoutExtension(liveWriter.TextPath);

        var transcriptPanel = (FrameworkElement)window.FindName("CaptionTranscriptPanel");
        var followCheck = (CheckBox)window.FindName("TranscriptFollowCheckBox");
        var transcriptText = (TextBox)window.FindName("CaptionTranscriptText");
        var formatCombo = (ComboBox)window.FindName("TranscriptFormatCombo");
        var sessionCombo = (ComboBox)window.FindName("TranscriptSessionCombo");

        typeof(MainWindow).GetField("_settingTranscriptSelection", flags)!.SetValue(window, true);
        transcriptPanel.Visibility = Visibility.Visible;
        followCheck.IsChecked = true;
        formatCombo.SelectedIndex = 0; // TXT format
        sessionCombo.ItemsSource = new[] { new CaptionTranscriptSession(activeId!, "Live Session", DateTime.UtcNow) };
        sessionCombo.SelectedItem = sessionCombo.ItemsSource.Cast<CaptionTranscriptSession>().First();
        typeof(MainWindow).GetField("_settingTranscriptSelection", flags)!.SetValue(window, false);

        window.AppendLiveSegment("In-memory live sentence", "Câu truyền trực tiếp qua RAM", 1.0, 3.0);
        check(transcriptText.Text.Contains("In-memory live sentence") && transcriptText.Text.Contains("-> Câu truyền trực tiếp qua RAM"),
            "AppendLiveSegment directly streams TXT segment in-memory to UI without waiting for disk polling");

        typeof(MainWindow).GetField("_settingTranscriptSelection", flags)!.SetValue(window, true);
        formatCombo.SelectedIndex = 1; // SRT format
        typeof(MainWindow).GetField("_settingTranscriptSelection", flags)!.SetValue(window, false);
        transcriptText.Text = "";
        window.AppendLiveSegment("Second SRT sentence", "Câu SRT thứ hai", 3.0, 5.0);
        check(transcriptText.Text.Contains("00:00:03,000 --> 00:00:05,000") && transcriptText.Text.Contains("Second SRT sentence"),
            "AppendLiveSegment directly streams SRT formatted segment in-memory");

        // ══════════════════════════════════════════════════════════════
        // Verify Smart Auto-Scroll Pause & Floating Pill Button
        // ══════════════════════════════════════════════════════════════
        var btnScrollLatest = (Button)window.FindName("BtnTranscriptScrollLatest");
        var txtScrollLatest = (TextBlock)window.FindName("TxtTranscriptScrollLatest");
        check(btnScrollLatest != null && txtScrollLatest != null, "Transcript viewer contains floating pill scroll-to-latest button");

        // Simulate user scrolling up
        typeof(MainWindow).GetField("_userScrolledUp", flags)!.SetValue(window, true);
        typeof(MainWindow).GetMethod("UpdateScrollLatestPill", flags)!.Invoke(window, null);
        check(btnScrollLatest!.Visibility == Visibility.Visible, "Floating pill appears when user scrolls up");
        window.AppendLiveSegment("Third live sentence while scrolled up", null, 6.0, 8.0);
        check(window.UserScrolledUp, "User scrolled up flag preserved during background live segments");
        check(window.UnreadSegmentsCount == 1, "Unread segments counter increments while auto-scroll is paused");
        check(btnScrollLatest.Visibility == Visibility.Visible, "Floating pill button is displayed when new segments arrive while scrolled up");
        check(txtScrollLatest!.Text.Contains("+1"), "Floating pill button shows accurate unread count (+1)");
        check(transcriptText.Text.Contains("Third live sentence while scrolled up"), "New segment is appended in memory without losing scroll position");

        // Click pill button to resume auto-scroll
        transcriptText.Select(2, 5);
        btnScrollLatest.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(!window.UserScrolledUp && window.UnreadSegmentsCount == 0 && transcriptText.SelectionLength == 0,
            "Clicking floating pill resumes auto-scroll, clears selection, and resets unread counter");
        check(btnScrollLatest.Visibility == Visibility.Collapsed, "Floating pill button collapses after jumping to latest");

        // Verify selection pauses auto-scroll even when at bottom, and sets _userScrolledUp to true
        transcriptText.Select(5, 5);
        window.AppendLiveSegment("Fourth live phrase with selection", null, 9.0, 11.0);
        check(window.UserScrolledUp && window.UnreadSegmentsCount == 1,
            "Active text selection automatically pauses auto-scroll and retains paused state");
        btnScrollLatest.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        // ══════════════════════════════════════════════════════════════
        // Verify Search Bar and Keyword Navigation
        // ══════════════════════════════════════════════════════════════
        var searchBox = (TextBox)window.FindName("TranscriptSearchBox");
        var searchCount = (TextBlock)window.FindName("TranscriptSearchCount");
        var btnClearSearch = (Button)window.FindName("BtnTranscriptClearSearch");
        var btnFindNext = (Button)window.FindName("BtnTranscriptFindNext");
        var btnFindPrev = (Button)window.FindName("BtnTranscriptFindPrev");
        check(searchBox != null && searchCount != null && btnClearSearch != null && btnFindNext != null && btnFindPrev != null,
            "Transcript search bar contains input box, counter, clear button, and navigation arrows");

        searchBox!.Text = "sentence";
        check(window.SearchMatches.Count == 2, "Transcript search accurately finds all 2 occurrences of 'sentence'");
        check(searchCount!.Text.Contains("1 / 2"), "Search counter reflects first match active: 1 / 2");

        btnFindNext!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(window.CurrentSearchIndex == 1 && searchCount.Text.Contains("2 / 2"), "Find next advances to match 2 / 2");

        btnFindPrev!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(window.CurrentSearchIndex == 0 && searchCount.Text.Contains("1 / 2"), "Find prev wraps back to match 1 / 2");

        btnClearSearch!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(searchBox.Text == "" && searchCount.Text == "", "Clear button empties search query and clears match counter");
        check(transcriptText.SelectionLength == 0, "Clearing search also clears transcript keyword selection");

        // ══════════════════════════════════════════════════════════════
        // Verify Viewer Expand / Collapse Height Toggle
        // ══════════════════════════════════════════════════════════════
        var btnExpand = (Button)window.FindName("BtnTranscriptExpand");
        check(btnExpand != null, "Viewer toolbar has Expand / Collapse button");
        check(transcriptText.Height == 240, "Transcript default height is 240px");

        btnExpand!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(transcriptText.Height == 440 && window.TranscriptExpanded, "Clicking Expand enlarges transcript viewer to 440px");

        btnExpand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(transcriptText.Height == 240 && !window.TranscriptExpanded, "Clicking Collapse restores transcript viewer to standard 240px");

        // ══════════════════════════════════════════════════════════════
        // Verify Quick Navigation: Top / Bottom
        // ══════════════════════════════════════════════════════════════
        var btnScrollTop = (Button)window.FindName("BtnTranscriptScrollTop");
        var btnScrollBottom = (Button)window.FindName("BtnTranscriptScrollBottom");
        check(btnScrollTop != null && btnScrollBottom != null, "Viewer toolbar has Top and Bottom jump buttons");

        btnScrollTop!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(window.UserScrolledUp, "Clicking Top sets user scrolled state to pause auto-scroll");

        btnScrollBottom!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(!window.UserScrolledUp, "Clicking Bottom restores auto-scroll state to bottom");

        // ══════════════════════════════════════════════════════════════
        // Verify ContextMenu and Smart Actions
        // ══════════════════════════════════════════════════════════════
        check(transcriptText.ContextMenu != null, "CaptionTranscriptText has ContextMenu attached");
        var menuCopySelected = (MenuItem)window.FindName("MenuTranscriptCopySelected");
        var menuCopyRecent = (MenuItem)window.FindName("MenuTranscriptCopyRecent");
        var menuCopyAll = (MenuItem)window.FindName("MenuTranscriptCopyAll");
        var menuSelectAll = (MenuItem)window.FindName("MenuTranscriptSelectAll");
        var menuScrollBottom = (MenuItem)window.FindName("MenuTranscriptScrollBottom");
        check(menuCopySelected != null && menuCopyRecent != null && menuCopyAll != null && menuSelectAll != null && menuScrollBottom != null,
            "Transcript ContextMenu provides Copy Selected, Copy 10 Recent, Copy All, Select All, and Scroll to Bottom");

        // Verify Smart Copy: selected portion vs entire transcript
        var btnTranscriptCopy = (Button)window.FindName("BtnTranscriptCopy");
        var statusTextBlock = (TextBlock)window.FindName("StatusText");
        btnTranscriptCopy.IsEnabled = true;
        transcriptText.Text = "Toàn bộ nội dung bản chép lời dài";
        transcriptText.Select(8, 8); // "nội dung"
        check(transcriptText.SelectedText == "nội dung", "Text selection active on transcript textbox");
        typeof(MainWindow).GetMethod("BtnTranscriptCopy_Click", flags)!.Invoke(window, new object[] { btnTranscriptCopy, new RoutedEventArgs() });
        check(statusTextBlock.Text.Contains("chọn", StringComparison.OrdinalIgnoreCase) ||
              statusTextBlock.Text.Contains("Selected", StringComparison.OrdinalIgnoreCase) ||
              statusTextBlock.Text.Contains("Clipboard is busy"),
            $"Smart Copy copies selected text when selection is active, status was: {statusTextBlock.Text}");

        transcriptText.Select(0, 0); // No selection
        typeof(MainWindow).GetMethod("BtnTranscriptCopy_Click", flags)!.Invoke(window, new object[] { btnTranscriptCopy, new RoutedEventArgs() });
        check(statusTextBlock.Text.Contains("toàn bộ", StringComparison.OrdinalIgnoreCase) ||
              statusTextBlock.Text.Contains("All", StringComparison.OrdinalIgnoreCase) ||
              statusTextBlock.Text.Contains("Clipboard is busy"),
            $"Smart Copy copies entire transcript when no selection is active, status was: {statusTextBlock.Text}");

        check(formatCombo.Items.Count == 3 &&
              ((ComboBoxItem)formatCombo.Items[0]).Content?.ToString() == "TXT" &&
              ((ComboBoxItem)formatCombo.Items[1]).Content?.ToString() == "SRT" &&
              ((ComboBoxItem)formatCombo.Items[2]).Content?.ToString() == "TOEIC (A-D)",
            "TranscriptFormatCombo exposes TXT, SRT, and TOEIC (A-D) options");

        typeof(MainWindow).GetField("_settingTranscriptSelection", flags)!.SetValue(window, true);
        formatCombo.SelectedIndex = 2; // TOEIC (A-D) format
        typeof(MainWindow).GetField("_settingTranscriptSelection", flags)!.SetValue(window, false);
        ((System.Text.StringBuilder)typeof(MainWindow).GetField("_liveRawTxtBuffer", flags)!.GetValue(window)!).Clear();
        transcriptText.Text = "";
        window.AppendLiveSegment("Where is the meeting room?", "Phòng họp ở đâu?", 10.0, 12.0);
        window.AppendLiveSegment("(A) On the second floor.", "Ở tầng hai.", 12.5, 14.0);
        window.AppendLiveSegment("(B) At 3 PM.", "Lúc 3 giờ chiều.", 14.5, 16.0);
        check(transcriptText.Text.Contains(TranscriptToeicFormatter.BlockDivider) &&
              transcriptText.Text.Contains("Where is the meeting room?") &&
              transcriptText.Text.Contains("  -> Phòng họp ở đâu?") &&
              transcriptText.Text.Contains("  (A) On the second floor.") &&
              transcriptText.Text.Contains("      -> Ở tầng hai.") &&
              transcriptText.Text.Contains("  (B) At 3 PM.") &&
              transcriptText.Text.Contains("      -> Lúc 3 giờ chiều."),
            "AppendLiveSegment in TOEIC (A-D) mode formats multi-segment Q&A and translations live");

        transcriptText.Text = "";
        transcriptPanel.Visibility = Visibility.Collapsed;
        sessionCombo.ItemsSource = null;
        typeof(CaptionService).GetProperty("LastTranscriptPath")!.SetValue(service, null);

        overlay.Close(); service.Dispose();
        Coexistence(check, temporaryRoot);
        FaultCleanupUi(check, temporaryRoot);
    }
    private sealed class Curtain : IPrivacyCurtain
    {
        public bool IsVisible => false;
        public bool CoversAllTargetScreens => false;
        public void Show() { }
        public void Hide() { }
    }
    private static void Coexistence(Action<bool, string> check, string temporaryRoot)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var path = Path.Combine(temporaryRoot, "captions-coexistence.json");
        var config = new ConfigService(path); config.Save(Array.Empty<GuardedApp>(), blockCameraAccess: false, blockMicrophoneAccess: false);
        var before = File.ReadAllText(path);
        using var service = new CaptionService(Path.Combine(temporaryRoot, "captions-coexistence"), _ => Task.FromResult(Child("delayed-pause")));
        var window = (MainWindow)Activator.CreateInstance(typeof(MainWindow), flags, null, new object?[] { config, new Curtain(), null, new Curtain(), null, service }, null)!;
        void Call(string method, params object[] arguments) => typeof(MainWindow).GetMethod(method, flags)!.Invoke(window, arguments);
        void PumpUntil(Func<bool> predicate)
        {
            var timer = Stopwatch.StartNew();
            while (!predicate() && timer.Elapsed < TimeSpan.FromSeconds(10))
            { System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); Thread.Sleep(10); }
            check(predicate(), "Caption UI operation completes");
        }
        var clickThrough = (CheckBox)window.FindName("CaptionClickThroughCheckBox");
        clickThrough.IsChecked = true;
        Call("EnsureCaptionOverlay");
        var overlayField = typeof(MainWindow).GetField("_captionOverlay", flags)!;
        var firstOverlay = (CaptionOverlayWindow)overlayField.GetValue(window)!;
        check(firstOverlay.IsClickThrough, "An overlay created after selecting click-through inherits that option");
        firstOverlay.Close();
        Call("EnsureCaptionOverlay");
        var replacementOverlay = (CaptionOverlayWindow)overlayField.GetValue(window)!;
        check(replacementOverlay.IsClickThrough && !ReferenceEquals(firstOverlay, replacementOverlay),
            "Recreating the overlay preserves the current click-through option");
        clickThrough.IsChecked = false;
        check(!replacementOverlay.IsClickThrough, "Main controls always unlock a recreated click-through overlay");
        var posted = 0;
        System.Windows.Threading.DispatcherHookEventHandler operationPosted = (_, e) =>
        { if (e.Operation.Priority == System.Windows.Threading.DispatcherPriority.Background) posted++; };
        window.Dispatcher.Hooks.OperationPosted += operationPosted;
        try { for (var i = 0; i < 1000; i++) Call("CaptionLevelReceived", i / 1000.0); }
        finally { window.Dispatcher.Hooks.OperationPosted -= operationPosted; }
        check(posted == 1, "A burst of 1000 meter events queues a bounded UI update instead of starving input");
        PumpUntil(() => ((ProgressBar)window.FindName("CaptionAudioLevel")).Value == .999);
        string historyId, historyPath;
        using (var history = new CaptionTranscriptWriter(service.OutputDirectory))
        {
            history.Write("Earlier saved session — tiếng Việt", 0, 1);
            history.Write("Who is leading the project? (A) Ms. Lee. (B) Next Monday. (C) In Room 204. (D) About twenty.", 2, 5, "Ai phụ trách dự án? (A) Cô Lee. (B) Thứ Hai tới. (C) Ở phòng 204. (D) Khoảng hai mươi.");
            historyPath = history.TextPath; historyId = Path.GetFileNameWithoutExtension(historyPath);
        }
        ((RadioButton)window.FindName("TabCaptions")).IsChecked = true;
        ((Button)window.FindName("BtnCaptionTranscripts")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var reader = (TextBox)window.FindName("CaptionTranscriptText");
        var sessionPicker = (ComboBox)window.FindName("TranscriptSessionCombo");
        var formatPicker = (ComboBox)window.FindName("TranscriptFormatCombo");
        var follow = (CheckBox)window.FindName("TranscriptFollowCheckBox");
        PumpUntil(() => reader.Text.Contains("Earlier saved session"));
        check(reader.IsReadOnly && service.State == CaptionState.Stopped, "Inline transcript history opens without launching recognition or an external application");
        formatPicker.SelectedIndex = 1; PumpUntil(() => reader.Text.Contains(" --> "));
        formatPicker.SelectedIndex = 2; PumpUntil(() => reader.Text.Contains(TranscriptToeicFormatter.BlockDivider) && reader.Text.Contains("  (A) Ms. Lee.") && reader.Text.Contains("      -> Cô Lee."));
        check(File.ReadAllText(historyPath).Contains("(A) Ms. Lee. (B) Next Monday."), "Switching to TOEIC (A-D) format never modifies the underlying .txt file on disk");
        formatPicker.SelectedIndex = 0; PumpUntil(() => !reader.Text.Contains(" --> ") && !reader.Text.Contains(TranscriptToeicFormatter.BlockDivider));
        foreach (var language in new[] { "vi", "en" })
        {
            LocalizationService.Current.SetLanguage(language);
            check(((Button)window.FindName("BtnCaptionTranscripts")).Content as string == (language == "vi" ? "Xem bản chép lời" : "View transcripts"),
                "Transcript controls follow live language switching without changing recognized text");
        }
        Call("SetProtectionUiState", true);
        check(service.Start(), "Injected headless caption worker starts while protection UI is active");
        PumpUntil(() => ((TextBlock)window.FindName("CaptionPreviewText")).Text.Contains("tiếng Việt"));
        PumpUntil(() => reader.Text.Contains("Hello — tiếng Việt"));
        var activeId = (sessionPicker.SelectedItem as CaptionTranscriptSession)!.Id;
        check(activeId != historyId && follow.IsChecked == true, "The viewer follows a newly started recording session automatically");
        sessionPicker.SelectedItem = sessionPicker.Items.Cast<CaptionTranscriptSession>().Single(item => item.Id == historyId);
        PumpUntil(() => reader.Text.Contains("Earlier saved session"));
        check(follow.IsChecked == false && service.State == CaptionState.Running, "Reading history suspends automatic session switching without pausing captions");
        File.Delete(historyPath);
        var failedRead = (Task)typeof(MainWindow).GetMethod("LoadTranscriptAsync", flags)!.Invoke(window, new object[] { false, false })!;
        PumpUntil(() => failedRead.IsCompleted && reader.Text.Length == 0);
        check(!((Button)window.FindName("BtnTranscriptCopy")).IsEnabled && service.State == CaptionState.Running,
            "A deleted transcript shows an isolated reader error while recording continues");
        File.WriteAllText(historyPath, "Earlier saved session restored");
        ((Button)window.FindName("BtnTranscriptRefresh")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => reader.Text.Contains("restored"));
        check((sessionPicker.SelectedItem as CaptionTranscriptSession)!.Id == historyId,
            "Refresh recovers a file and keeps the user's selected history session");
        follow.IsChecked = true; PumpUntil(() => (sessionPicker.SelectedItem as CaptionTranscriptSession)?.Id == activeId && reader.Text.Contains("Hello"));
        var closingRead = (Task)typeof(MainWindow).GetMethod("LoadTranscriptAsync", flags)!.Invoke(window, new object[] { true, false })!;
        Call("CloseTranscriptReader"); PumpUntil(() => closingRead.IsCompleted);
        check(service.State == CaptionState.Running && ((FrameworkElement)window.FindName("CaptionTranscriptPanel")).Visibility == Visibility.Collapsed,
            "Closing during an asynchronous read cancels the viewer without stopping recording");
        ((Button)window.FindName("BtnCaptionTranscripts")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => reader.Text.Contains("Hello") && ((FrameworkElement)window.FindName("CaptionTranscriptPanel")).Visibility == Visibility.Visible);
        check(((Button)window.FindName("BtnCaptionPause")).IsEnabled && (bool)typeof(MainWindow).GetField("_isProtecting", flags)!.GetValue(window)!, "Caption messages update the UI without stopping protection");
        Call("BtnCaptionPause_Click", window, new RoutedEventArgs());
        Call("BtnCaptionPause_Click", window, new RoutedEventArgs());
        PumpUntil(() => service.State == CaptionState.Running && new CaptionTranscriptStore(service.OutputDirectory).Read(activeId, false).Text.Contains("Resumed speech"));
        check(service.State == CaptionState.Running, "Two rapid pause toggles resume correctly even before the first worker acknowledgement");
        follow.IsChecked = false;
        PumpUntil(() => ((Button)window.FindName("BtnTranscriptRefresh")).IsEnabled);
        reader.Select(0, 5); var selectedText = reader.SelectedText; var frozenText = reader.Text;
        Call("BtnCaptionPause_Click", window, new RoutedEventArgs()); PumpUntil(() => service.State == CaptionState.Paused);
        Call("BtnCaptionPause_Click", window, new RoutedEventArgs()); PumpUntil(() => service.State == CaptionState.Running);
        var until = Stopwatch.StartNew();
        while (until.Elapsed < TimeSpan.FromMilliseconds(1200))
        { System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); Thread.Sleep(10); }
        check(reader.Text == frozenText && reader.SelectedText == selectedText,
            "Turning follow off freezes the text and selection even while recording and meter timers continue");
        ((Button)window.FindName("BtnTranscriptRefresh")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => reader.Text.Length > frozenText.Length);
        check(reader.SelectedText == selectedText, "Manual refresh preserves a selection when speech is appended to the displayed text");
        Call("BtnCaptionPause_Click", window, new RoutedEventArgs()); PumpUntil(() => service.State == CaptionState.Paused);
        check((bool)typeof(MainWindow).GetField("_isProtecting", flags)!.GetValue(window)!, "Pausing subtitles leaves protection active");
        Call("SetProtectionUiState", false);
        check(service.State == CaptionState.Paused, "Protection UI changes do not resume or stop subtitles");
        Call("SetProtectionUiState", true);
        var stopped = (Task)typeof(MainWindow).GetMethod("StopCaptionsAsync", flags)!.Invoke(window, null)!;
        PumpUntil(() => stopped.IsCompleted); stopped.GetAwaiter().GetResult();
        check(service.State == CaptionState.Stopped && (bool)typeof(MainWindow).GetField("_isProtecting", flags)!.GetValue(window)!, "Stopping captions preserves protection state");
        check(File.ReadAllText(path) == before, "Caption lifecycle never changes device, capture, hotkey or app preferences");
        foreach (var language in new[] { "vi", "en" })
        {
            LocalizationService.Current.SetLanguage(language);
            foreach (var width in new[] { 860, 640 })
            {
                window.Width = width; window.Height = width == 860 ? 600 : 460;
                window.Measure(new Size(window.Width, window.Height)); window.Arrange(new Rect(0, 0, window.Width, window.Height)); window.UpdateLayout();
                var content = (FrameworkElement)window.Content;
                Call("ApplySidebarLayout", (double)width);
                ((Grid)content).Background = window.Background;
                var view = (FrameworkElement)window.FindName("ViewCaptions"); view.BeginAnimation(UIElement.OpacityProperty, null); view.Opacity = 1;
                content.Measure(new Size(window.Width, window.Height)); content.Arrange(new Rect(0, 0, window.Width, window.Height)); content.UpdateLayout();
                var panel = (FrameworkElement)window.FindName("CaptionTranscriptPanel"); panel.BringIntoView();
                check(panel.ActualWidth > 0 && reader.ActualWidth > 0, "Transcript viewer lays out at the supported window size");
                bool HasLabel(DependencyObject parent)
                {
                    if (parent is TextBlock text && text.Text == (sessionPicker.SelectedItem as CaptionTranscriptSession)?.Label) return true;
                    for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
                        if (HasLabel(VisualTreeHelper.GetChild(parent, index))) return true;
                    return false;
                }
                check(HasLabel(sessionPicker), "The session picker renders a readable date label instead of a record dump");
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var bitmap = new RenderTargetBitmap(width, (int)window.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var image = File.Create(Path.Combine(temporaryRoot, $"transcript-viewer-{language}-{width}.png")); png.Save(image);
            }
        }
        Call("CloseTranscriptReader");
        check(((FrameworkElement)window.FindName("CaptionTranscriptPanel")).Visibility == Visibility.Collapsed && service.State == CaptionState.Stopped,
            "Closing the inline viewer leaves the recognition lifecycle unchanged");
        Call("CleanupCaptions");
        typeof(MainWindow).GetField("_cleanupDone", flags)!.SetValue(window, true);
        var preview = ((TextBlock)window.FindName("CaptionPreviewText")).Text;
        Call("CaptionLevelReceived", .5); Call("CaptionStateChanged");
        Call("CaptionSegmentReceived", new CaptionSegment("After cleanup", 0, 1));
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        check(((TextBlock)window.FindName("CaptionPreviewText")).Text == preview,
            "Late caption events cannot mutate UI after application cleanup");
        ((IDisposable)typeof(MainWindow).GetField("_vdService", flags)!.GetValue(window)!).Dispose();
        ((IDisposable)typeof(MainWindow).GetField("_threatBlocker", flags)!.GetValue(window)!).Dispose();
    }

    private static void FaultCleanupUi(Action<bool, string> check, string temporaryRoot)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var config = new ConfigService(Path.Combine(temporaryRoot, "captions-cleanup-ui.json"));
        config.Save(Array.Empty<GuardedApp>(), blockCameraAccess: false, blockMicrophoneAccess: false);
        using var service = new CaptionService(Path.Combine(temporaryRoot, "captions-cleanup-ui"), _ => Task.FromResult(Child("error-hang")));
        var window = (MainWindow)Activator.CreateInstance(typeof(MainWindow), flags, null, new object?[] { config, new Curtain(), null, new Curtain(), null, service }, null)!;
        using var faultReached = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        void HoldCleanup() { if (service.State == CaptionState.Faulted && !faultReached.IsSet) { faultReached.Set(); release.Wait(5000); } }
        service.Changed += HoldCleanup;
        try
        {
            service.Start(); check(faultReached.Wait(5000), "Faulted worker reaches the cleanup barrier");
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            check(!((Button)window.FindName("BtnCaptionStart")).IsEnabled && !service.Start(),
                "Restart stays unavailable until the failed session finishes releasing its worker");
            release.Set();
            var timer = Stopwatch.StartNew();
            while (!((Button)window.FindName("BtnCaptionStart")).IsEnabled && timer.Elapsed < TimeSpan.FromSeconds(5))
            { System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); Thread.Sleep(10); }
            check(((Button)window.FindName("BtnCaptionStart")).IsEnabled && service.WorkerProcessId == null,
                "Completing failure cleanup re-enables restart without a second click or manual stop");
        }
        finally
        {
            release.Set(); service.Changed -= HoldCleanup;
            service.StopAsync().GetAwaiter().GetResult();
            typeof(MainWindow).GetMethod("CleanupCaptions", flags)!.Invoke(window, null);
            ((IDisposable)typeof(MainWindow).GetField("_vdService", flags)!.GetValue(window)!).Dispose();
            ((IDisposable)typeof(MainWindow).GetField("_threatBlocker", flags)!.GetValue(window)!).Dispose();
        }
    }

    private static void ToeicFormatterChecks(Action<bool, string> check)
    {
        check(TranscriptToeicFormatter.Format(null) == "" && TranscriptToeicFormatter.Format("   \r\n  ") == "",
            "TOEIC formatter returns empty string for blank input");

        // 1. Single-line (A) (B) (C) (D) with combined translation
        var singleLine = "Where is the meeting room? (A) On the second floor. (B) At 3 PM. (C) Yes, I met him. (D) In the binder.\r\n" +
                         "  -> Phòng họp ở đâu? (A) Ở tầng hai. (B) Lúc 3 giờ chiều. (C) Vâng, tôi đã gặp anh ấy. (D) Trong bìa hồ sơ.\r\n";
        var formattedSingle = TranscriptToeicFormatter.Format(singleLine);
        check(formattedSingle.Contains(TranscriptToeicFormatter.BlockDivider) &&
              formattedSingle.Contains("Where is the meeting room?\r\n  -> Phòng họp ở đâu?\r\n\r\n  (A) On the second floor.\r\n      -> Ở tầng hai.\r\n  (B) At 3 PM.\r\n      -> Lúc 3 giờ chiều.\r\n  (C) Yes, I met him.\r\n      -> Vâng, tôi đã gặp anh ấy.\r\n  (D) In the binder.\r\n      -> Trong bìa hồ sơ."),
            "TOEIC formatter splits single-line (A)-(D) and pairs bilingual translations under each item");

        // 2. Dot, parenthesis, colon, comma, and Option A..D variants
        var dotVariant = "Question 1.\r\n\r\nWhen will the report be ready?\r\n\r\nA. By Friday. B. In the lobby. C. No, she didn't. D. About fifty pages.";
        var formattedDot = TranscriptToeicFormatter.Format(dotVariant);
        check(formattedDot.Contains("Question 1. When will the report be ready?") &&
              formattedDot.Contains("  (A) By Friday.") &&
              formattedDot.Contains("  (B) In the lobby.") &&
              formattedDot.Contains("  (C) No, she didn't.") &&
              formattedDot.Contains("  (D) About fifty pages."),
            "TOEIC formatter supports Question N header merging and A. B. C. D. punctuation");

        var commaVariant = "Who ordered the supplies? A, Maria did. B, Next Tuesday. C, In the cabinet. D, Yes, please.";
        var formattedComma = TranscriptToeicFormatter.Format(commaVariant);
        check(formattedComma.Contains("Who ordered the supplies?") &&
              formattedComma.Contains("  (A) Maria did.") &&
              formattedComma.Contains("  (B) Next Tuesday.") &&
              formattedComma.Contains("  (C) In the cabinet.") &&
              formattedComma.Contains("  (D) Yes, please."),
            "TOEIC formatter supports Whisper comma-separated A, B, C, D output");

        var optionKeywordVariant = "Option A: Take the elevator. Option B: On the desk. Option C: At noon. Option D: Sure, I can.";
        var formattedOptionKeyword = TranscriptToeicFormatter.Format(optionKeywordVariant);
        check(formattedOptionKeyword.Contains("  (A) Take the elevator.") &&
              formattedOptionKeyword.Contains("  (B) On the desk.") &&
              formattedOptionKeyword.Contains("  (C) At noon.") &&
              formattedOptionKeyword.Contains("  (D) Sure, I can."),
            "TOEIC formatter supports Option A..D keyword pattern");

        // 3. Multi-segment split with individual translations
        var multiSeg = "Where is the meeting room?\r\n  -> Phòng họp ở đâu?\r\n\r\n" +
                       "(A) On the second floor. (B) At 3 PM.\r\n\r\n" +
                       "(C) Yes, I met him.\r\n  -> Vâng, tôi đã gặp anh ấy.\r\n\r\n" +
                       "(D) In the binder.\r\n  -> Trong bìa hồ sơ.\r\n";
        var formattedMulti = TranscriptToeicFormatter.Format(multiSeg);
        check(formattedMulti.Contains("Where is the meeting room?\r\n  -> Phòng họp ở đâu?\r\n\r\n  (A) On the second floor.\r\n  (B) At 3 PM.\r\n  (C) Yes, I met him.\r\n      -> Vâng, tôi đã gặp anh ấy.\r\n  (D) In the binder.\r\n      -> Trong bìa hồ sơ."),
            "TOEIC formatter merges question and (A)-(D) options across multiple segments");

        // 4. Safe fallback for normal conversational text (must not add dividers or false-positive A/B/C/D)
        var normalChat = "I got an A. Then I went to the U.S.A. office.\r\n  -> Tôi đạt điểm A. Sau đó tôi đến văn phòng Mỹ.\r\n\r\n" +
                         "We need Vitamin A, B, C, D for health.\r\n";
        var formattedNormal = TranscriptToeicFormatter.Format(normalChat);
        check(!formattedNormal.Contains(TranscriptToeicFormatter.BlockDivider) &&
              !formattedNormal.Contains("  (A) ") &&
              formattedNormal.Contains("I got an A. Then I went to the U.S.A. office.") &&
              formattedNormal.Contains("We need Vitamin A, B, C, D for health."),
            "TOEIC formatter safely preserves normal conversational text without false-positive A/B/C/D splitting");

        // 5. ExtractRecentSegments with isToeic = true
        var twoBlocks = formattedSingle + "\r\n" + formattedDot;
        var recentOne = StreamGuard.MainWindow.ExtractRecentSegments(twoBlocks, count: 1, isSrt: false, isToeic: true);
        check(recentOne.Contains("Question 1. When will the report be ready?") && !recentOne.Contains("Where is the meeting room?"),
            "ExtractRecentSegments with isToeic=true extracts complete TOEIC blocks cleanly");

        // 6. Short numeric options, inline comma inside (A)-(D), trailing newline preservation, and trailing plain paragraph extraction
        var numericPrices = "How much is the ticket? (A) $5. (B) $10. (C) $15. (D) $20.";
        var formattedNumeric = TranscriptToeicFormatter.Format(numericPrices);
        check(formattedNumeric.Contains("  (A) $5.") && formattedNumeric.Contains("  (D) $20."),
            "TOEIC formatter accepts short numeric option bodies like $5.");

        var inlineCommaInOption = "Where is the meeting? (A) In Room A, on the left. (B) At 2 PM. (C) Yes, it is. (D) Near the lobby.";
        var formattedInlineComma = TranscriptToeicFormatter.Format(inlineCommaInOption);
        check(formattedInlineComma.Split(TranscriptToeicFormatter.BlockDivider, StringSplitOptions.None).Length == 2 &&
              formattedInlineComma.Contains("  (A) In Room A, on the left.") &&
              formattedInlineComma.Contains("  (B) At 2 PM."),
            "TOEIC formatter does not let inline 'A,' inside (A) split or break the (A)-(D) block");

        var noBlankLinesWithTrailingNewline = "Hello world.\nHow are you?\n";
        var formattedTwoLines = TranscriptToeicFormatter.Format(noBlankLinesWithTrailingNewline);
        check(formattedTwoLines.Contains("Hello world.\r\n\r\nHow are you?"),
            "TOEIC formatter keeps separate lines distinct when only a trailing newline is present");

        var lowerOpt = "option a: Take the bus. option b: Walk there.";
        var formattedLowerOpt = TranscriptToeicFormatter.Format(lowerOpt);
        check(formattedLowerOpt.Contains("  (A) Take the bus.") && formattedLowerOpt.Contains("  (B) Walk there."),
            "TOEIC formatter supports case-insensitive Option A..D keywords");

        var toeicThenPlain = formattedSingle + "Okay, let's take a break.\r\n\r\nWelcome back everyone.\r\n\r\n";
        var recentPlain = StreamGuard.MainWindow.ExtractRecentSegments(toeicThenPlain, count: 1, isSrt: false, isToeic: true);
        check(recentPlain == "Welcome back everyone.",
            "ExtractRecentSegments with isToeic=true separates trailing plain paragraphs after a TOEIC block");
    }
}
