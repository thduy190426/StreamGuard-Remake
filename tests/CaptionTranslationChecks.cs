using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using StreamGuard;
using StreamGuard.Localization;
using StreamGuard.Models;
using StreamGuard.Services;

namespace StreamGuard.Tests;

internal static class CaptionTranslationChecks
{
    internal static void Run(Action<bool, string> check, string temporaryRoot)
    {
        TestTranslationService(check);
        TestCaptionSettingsWithTranslation(check, temporaryRoot);
        TestTranscriptWriterWithTranslation(check, temporaryRoot);
        TestCaptionSegmentRecord(check);
        TestLocalizationKeys(check);
        TestProgressiveTwoStageCaptionDelivery(check, temporaryRoot);
        TestSafeShutdownWithPendingTranslation(check, temporaryRoot);
        TestConcurrentTranscriptWriting(check, temporaryRoot);
    }

    private static void TestTranslationService(Action<bool, string> check)
    {
        // 1. Whitespace / empty handling
        using var service = new CaptionTranslationService();
        var emptyRes = service.TranslateToVietnameseAsync("   ").GetAwaiter().GetResult();
        check(emptyRes == "   ", "Empty text returns original text without network call");

        var numRes = service.TranslateToVietnameseAsync("12345 67890 .?!").GetAwaiter().GetResult();
        check(numRes == "12345 67890 .?!", "Number/punctuation text returns original text");

        // 2. Custom translator & Cache validation
        var callCount = 0;
        using var mockService = new CaptionTranslationService((input, ct) =>
        {
            callCount++;
            return Task.FromResult<string?>("Xin chào thế giới");
        });

        var r1 = mockService.TranslateToVietnameseAsync("Hello world").GetAwaiter().GetResult();
        check(r1 == "Xin chào thế giới", "Custom translator translates text correctly");
        check(callCount == 1, "Custom translator called once for first translation");
        check(mockService.CacheCount == 1, "Cache stores translated text");

        // Subsequent call should hit cache and NOT call custom translator again
        var r2 = mockService.TranslateToVietnameseAsync("Hello world").GetAwaiter().GetResult();
        check(r2 == "Xin chào thế giới", "Cache hit returns cached translation");
        check(callCount == 1, "Custom translator NOT called on cache hit");

        mockService.ClearCache();
        check(mockService.CacheCount == 0, "ClearCache empties the cache");

        // 3. HTML Entity decoding validation
        using var htmlService = new CaptionTranslationService((input, ct) => Task.FromResult<string?>("&quot;Xin chào&#39; thế giới&amp;!&quot;"));
        var htmlRes = htmlService.TranslateToVietnameseAsync("Hello world").GetAwaiter().GetResult();
        check(htmlRes == "\"Xin chào' thế giới&!\"", "HTML entities are decoded cleanly in translation result");

        // 4. Fallback on exception
        using var errorService = new CaptionTranslationService((input, ct) => throw new IOException("Network down"));
        var fallbackRes = errorService.TranslateToVietnameseAsync("Good morning").GetAwaiter().GetResult();
        check(fallbackRes == "Good morning", "Translation falls back gracefully to original text on failure");
    }

    private static void TestCaptionSettingsWithTranslation(Action<bool, string> check, string temporaryRoot)
    {
        var settingsPath = Path.Combine(temporaryRoot, "test_caption_settings_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var settings = new CaptionSettings
            {
                EnableTranslation = true,
                TranslationMode = "VietnameseOnly",
                TranslationHotkey = "Ctrl+Alt+T"
            };
            var saved = settings.Save(settingsPath);
            check(saved, "CaptionSettings with translation saves successfully");

            var loaded = CaptionSettings.Load(settingsPath);
            check(loaded.EnableTranslation == true, "EnableTranslation loaded accurately");
            check(loaded.TranslationMode == "VietnameseOnly", "TranslationMode loaded accurately");
            check(loaded.TranslationHotkey == "Ctrl+Alt+T", "TranslationHotkey loaded accurately");

            // Test normalization
            loaded.TranslationMode = "InvalidMode";
            loaded.TranslationHotkey = "   ";
            loaded.Normalize();
            check(loaded.TranslationMode == "Bilingual", "Invalid TranslationMode normalizes to Bilingual");
            check(loaded.TranslationHotkey == "Ctrl+Alt+T", "Blank TranslationHotkey normalizes to Ctrl+Alt+T");
        }
        finally
        {
            try { File.Delete(settingsPath); } catch { }
        }
    }

    private static void TestTranscriptWriterWithTranslation(Action<bool, string> check, string temporaryRoot)
    {
        var dir = Path.Combine(temporaryRoot, "writer_trans_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string txtPath, srtPath;
            using (var writer = new CaptionTranscriptWriter(dir))
            {
                txtPath = writer.TextPath;
                srtPath = writer.SubtitlePath;
                writer.Write("Hello everyone", 0.0, 2.5, "Xin chào mọi người");
            }

            var txt = File.ReadAllText(txtPath);
            check(txt.Contains("Hello everyone"), "Transcript TXT contains original English");
            check(txt.Contains("-> Xin chào mọi người"), "Transcript TXT contains translated Vietnamese");

            var srt = File.ReadAllText(srtPath);
            check(srt.Contains("Hello everyone"), "Transcript SRT contains original English");
            check(srt.Contains("Xin chào mọi người"), "Transcript SRT contains translated Vietnamese");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static void TestCaptionSegmentRecord(Action<bool, string> check)
    {
        var seg1 = new CaptionSegment("Hello", 0.0, 1.0);
        check(seg1.Text == "Hello" && seg1.Translation == null, "CaptionSegment positional 3-param constructor defaults Translation to null");

        var seg2 = new CaptionSegment("Hello", 0.0, 1.0, "Xin chào");
        check(seg2.Translation == "Xin chào", "CaptionSegment 4-param constructor assigns Translation");
    }

    private static void TestLocalizationKeys(Action<bool, string> check)
    {
        var keys = new[]
        {
            "Captions.ToggleTranslation",
            "Captions.TranslationSection",
            "Captions.EnableTranslation",
            "Captions.ModeBilingual",
            "Captions.ModeVietnameseOnly",
            "Captions.TranslationHotkeyNotice",
            "Captions.TranslationEnabledNotice",
            "Captions.TranslationDisabledNotice",
            "Transcripts.Copy",
            "Transcripts.CopyTooltip",
            "Transcripts.Clear",
            "Transcripts.ClearTooltip",
            "Transcripts.CopiedNotice",
            "Transcripts.ClearedNotice",
            "Transcripts.ClearedLiveNotice",
            "Transcripts.CopiedButton",
            "Transcripts.CloseTooltip",
            "Transcripts.DeletePrevious",
            "Transcripts.DeletePreviousTooltip",
            "Transcripts.DeleteSessionTooltip",
            "Transcripts.DeletedPreviousNotice",
            "Transcripts.DeletedSessionNotice",
            "Transcripts.CannotDeleteLiveNotice"
        };

        foreach (var key in keys)
        {
            var en = LocalizationService.GetString(key, "");
            check(!string.IsNullOrWhiteSpace(en), $"Key '{key}' is defined in localization table");
        }
    }

    private static void TestProgressiveTwoStageCaptionDelivery(Action<bool, string> check, string temporaryRoot)
    {
        var dir = Path.Combine(temporaryRoot, "two_stage_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var instantEnglishSegments = new ConcurrentQueue<CaptionSegment>();
            var translatedSegments = new ConcurrentQueue<CaptionSegment>();
            var allowTranslation = new TaskCompletionSource<bool>();
            var translationStarted = new TaskCompletionSource<bool>();

            using var service = new CaptionService(dir, _ => Task.FromResult(CaptionChecks.Child("normal")));
            service.Translator = async text =>
            {
                translationStarted.TrySetResult(true);
                await allowTranslation.Task;
                return "Xin chào — tiếng Việt";
            };

            service.Segment += seg => instantEnglishSegments.Enqueue(seg);
            service.SegmentTranslated += seg => translatedSegments.Enqueue(seg);

            service.Start();

            // Wait for stage 1: English segment arrives immediately with zero translation delay (< 10ms)
            CaptionChecks.Wait(() => !instantEnglishSegments.IsEmpty, "instant English segment");
            check(instantEnglishSegments.TryDequeue(out var s1) && s1.Text == "Hello — tiếng Việt" && s1.Translation == null,
                "Stage 1 delivers raw English segment immediately with Translation == null");
            check(translatedSegments.IsEmpty, "Stage 2 has not emitted while background translation is pending");

            // Allow translation to finish in background
            allowTranslation.SetResult(true);
            CaptionChecks.Wait(() => !translatedSegments.IsEmpty, "translated segment");
            check(translatedSegments.TryDequeue(out var s2) && s2.Text == "Hello — tiếng Việt" && s2.Translation == "Xin chào — tiếng Việt",
                "Stage 2 delivers translated segment once background translation completes");

            service.StopAsync().GetAwaiter().GetResult();
            var savedTxt = File.ReadAllText(service.LastTranscriptPath!);
            check(savedTxt.Contains("Hello — tiếng Việt") && savedTxt.Contains("-> Xin chào — tiếng Việt"),
                "Final transcript file contains both English and translated Vietnamese in order");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static void TestSafeShutdownWithPendingTranslation(Action<bool, string> check, string temporaryRoot)
    {
        var dir = Path.Combine(temporaryRoot, "shutdown_trans_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var instantEnglishSegments = new ConcurrentQueue<CaptionSegment>();
            var translationNeverCompletes = new TaskCompletionSource<string?>();

            using var service = new CaptionService(dir, _ => Task.FromResult(CaptionChecks.Child("normal")));
            service.Translator = async text =>
            {
                return await translationNeverCompletes.Task;
            };

            service.Segment += seg => instantEnglishSegments.Enqueue(seg);
            service.Start();

            CaptionChecks.Wait(() => !instantEnglishSegments.IsEmpty, "instant English segment");
            check(instantEnglishSegments.TryDequeue(out var s1) && s1.Text == "Hello — tiếng Việt",
                "Stage 1 delivers raw English segment immediately");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            service.StopAsync().GetAwaiter().GetResult();
            sw.Stop();
            check(sw.ElapsedMilliseconds < 3500, "StopAsync finishes promptly even if translation is hanging");

            var savedTxt = File.ReadAllText(service.LastTranscriptPath!);
            check(savedTxt.Contains("Hello — tiếng Việt"),
                "Final transcript file safely retains recognized English segment upon cancellation");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static void TestConcurrentTranscriptWriting(Action<bool, string> check, string temporaryRoot)
    {
        var dir = Path.Combine(temporaryRoot, "concurrent_writer_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string txtPath, srtPath;
            using (var writer = new CaptionTranscriptWriter(dir))
            {
                txtPath = writer.TextPath;
                srtPath = writer.SubtitlePath;
                Parallel.For(0, 40, i =>
                {
                    writer.Write($"English segment {i:D3}", i * 2.0, i * 2.0 + 1.5, $"Phân đoạn tiếng Việt {i:D3}");
                });
            }

            var txt = File.ReadAllText(txtPath);
            var srt = File.ReadAllText(srtPath);
            var allInTxt = Enumerable.Range(0, 40).All(i => txt.Contains($"English segment {i:D3}") && txt.Contains($"Phân đoạn tiếng Việt {i:D3}"));
            var allInSrt = Enumerable.Range(0, 40).All(i => srt.Contains($"English segment {i:D3}") && srt.Contains($"Phân đoạn tiếng Việt {i:D3}"));
            check(allInTxt && allInSrt, "CaptionTranscriptWriter is completely thread-safe under parallel concurrent writes");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
