using System.IO;
using System.Text;
using StreamGuard.Services;

internal static class TranscriptChecks
{
    internal static void Run(Action<bool, string> check, string root)
    {
        var directory = Path.Combine(root, "transcript-reader");
        var store = new CaptionTranscriptStore(directory);
        static byte[] ReadLive(string path)
        { using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); using var memory = new MemoryStream(); input.CopyTo(memory); return memory.ToArray(); }
        check(store.List().Count == 0 && !Directory.Exists(directory), "Opening empty history never creates files or starts captions");
        Directory.CreateDirectory(directory);
        using (var writer = new CaptionTranscriptWriter(directory))
        {
            var id = Path.GetFileNameWithoutExtension(writer.TextPath);
            writer.Write("Tiếng Việt — phụ đề đang ghi", 1.2, 2.5);
            var before = ReadLive(writer.TextPath);
            check(store.List().Count == 1 && store.List()[0].Id == id && !store.List()[0].Label.StartsWith("transcript_"),
                "TXT and SRT form one session with a readable date label");
            check(store.Read(id, false).Text.Contains("Tiếng Việt"), "Reader shares the writer's active TXT without locking it");
            check(store.Read(id, true).Text.Contains("00:00:01,200 --> 00:00:02,500"), "SRT is displayed with timestamps intact");
            writer.Write("Newly appended caption", 3, 4);
            check(store.Read(id, false).Text.Contains("Newly appended"), "Refreshing the active file includes flushed new speech");
            check(ReadLive(writer.TextPath).AsSpan(0, before.Length).SequenceEqual(before), "Viewing leaves existing transcript bytes intact");
        }
        File.WriteAllText(Path.Combine(directory, "unrelated.txt"), "private unrelated file");
        var largeId = "transcript_20261104_210000_12345678";
        var largePath = Path.Combine(directory, largeId + ".txt");
        File.WriteAllText(largePath, string.Concat(Enumerable.Repeat("Tiếng Việt 🌟\n", 30000)), new UTF8Encoding(false));
        var large = store.Read(largeId, false);
        check(large.Truncated && Encoding.UTF8.GetByteCount(large.Text) <= CaptionTranscriptStore.MaximumBytes && !large.Text.Contains('\uFFFD'),
            "Large files are bounded and a cut UTF-8 character is skipped cleanly");
        check(new FileInfo(largePath).Length > CaptionTranscriptStore.MaximumBytes, "The complete large transcript is never truncated on disk");
        var incomplete = "transcript_20261104_210001_12345678";
        File.WriteAllBytes(Path.Combine(directory, incomplete + ".txt"), new byte[] { 65, 0xE1, 0xBB });
        check(store.Read(incomplete, false).Text == "A", "A partial UTF-8 write at EOF does not display a replacement character");
        var missing = "transcript_20261104_210002_12345678";
        File.WriteAllText(Path.Combine(directory, missing + ".srt"), "SRT only");
        check(store.List().Any(item => item.Id == missing) && store.List().All(item => item.Id != "unrelated"),
            "SRT-only sessions are listed and unrelated files are excluded");
        try { store.Read(missing, false); check(false, "A missing format must report an error"); } catch (FileNotFoundException) { check(true, "Missing format is recoverable"); }
        using (var locked = File.Open(largePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            try { store.Read(largeId, false); check(false, "Exclusive file locks must be respected"); } catch (IOException) { check(true, "An externally locked transcript fails without changing it"); }
        }
        check(store.Read(largeId, false).Truncated, "A locked file can be read again after it is released");
        foreach (var id in new[] { "../escape", "transcript_..\\escape", "C:\\escape", "other" })
        {
            try { store.Read(id, false); check(false, "Transcript reader accepted a path outside its session list"); } catch (IOException) { check(true, "Invalid session path rejected"); }
        }
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { store.Read(largeId, false, cancel.Token); check(false, "Canceled read must stop"); } catch (OperationCanceledException) { check(true, "Closing the reader cancels pending file reads"); }
        for (var i = 0; i < 205; i++) File.WriteAllText(Path.Combine(directory, $"transcript_history_{i:D4}.txt"), "saved");
        check(store.List().Count == 200, "The session selector keeps a bounded recent history");

        // Tests for ReadAllText, Delete, and ClearContent
        var fullText = store.ReadAllText(largeId, false);
        check(fullText.Length > CaptionTranscriptStore.MaximumBytes && fullText.Contains("Tiếng Việt"),
            "ReadAllText retrieves the entire file content even when display reader truncates");
        check(store.ReadAllText("../escape", false) == "", "ReadAllText rejects invalid session paths");

        var testDelId = "transcript_20261120_230000_12345678";
        var testTxt = Path.Combine(directory, testDelId + ".txt");
        var testSrt = Path.Combine(directory, testDelId + ".srt");
        File.WriteAllText(testTxt, "content to delete");
        File.WriteAllText(testSrt, "1\n00:00:01,000 --> 00:00:02,000\ncontent");
        check(File.Exists(testTxt) && File.Exists(testSrt), "Test session files created");
        check(store.Delete(testDelId), "DeleteSession successfully removes session files");
        check(!File.Exists(testTxt) && !File.Exists(testSrt), "Session files no longer exist on disk");
        check(!store.Delete("../escape"), "DeleteSession rejects invalid session paths");

        var testClearId = "transcript_20261120_230001_12345678";
        var testClearTxt = Path.Combine(directory, testClearId + ".txt");
        File.WriteAllText(testClearTxt, "content to clear");
        check(store.ClearContent(testClearId), "ClearContent succeeds on valid session");
        check(File.Exists(testClearTxt) && new FileInfo(testClearTxt).Length == 0, "ClearContent emptied file to 0 bytes");
        check(!store.ClearContent("../escape"), "ClearContent rejects invalid session paths");

        // Test DeletePreviousSessions
        var s1 = "transcript_20261120_231000_11111111";
        var s2 = "transcript_20261120_231001_22222222";
        var s3 = "transcript_20261120_231002_33333333";
        File.WriteAllText(Path.Combine(directory, s1 + ".txt"), "old 1");
        File.WriteAllText(Path.Combine(directory, s2 + ".txt"), "old 2");
        File.WriteAllText(Path.Combine(directory, s3 + ".txt"), "latest");
        var deletedOld = store.DeletePreviousSessions(s3);
        check(deletedOld >= 2, $"DeletePreviousSessions removed {deletedOld} older sessions");
        check(File.Exists(Path.Combine(directory, s3 + ".txt")), "DeletePreviousSessions preserved active/latest session");
        check(!File.Exists(Path.Combine(directory, s1 + ".txt")), "DeletePreviousSessions deleted older session 1");
        check(!File.Exists(Path.Combine(directory, s2 + ".txt")), "DeletePreviousSessions deleted older session 2");

        // ══════════════════════════════════════════════════════════════
        // Long Session Navigation & Copying Optimization Tests
        // ══════════════════════════════════════════════════════════════

        // 1. Keyword search algorithm: FindMatchIndices
        var searchSource = "Chào mừng bạn đến với StreamGuard! Chào mừng phiên chép lời STREAMGUARD.";
        var matches = StreamGuard.MainWindow.FindMatchIndices(searchSource, "chào mừng");
        check(matches.Count == 2 && matches[0] == 0 && matches[1] == 35, "FindMatchIndices finds case-insensitive matches at correct indices");

        var caseMatches = StreamGuard.MainWindow.FindMatchIndices(searchSource, "streamguard");
        check(caseMatches.Count == 2, "FindMatchIndices matches uppercase and mixed case keywords identically");

        var repeatSource = "aaaa";
        var repeatMatches = StreamGuard.MainWindow.FindMatchIndices(repeatSource, "aa");
        check(repeatMatches.Count == 2 && repeatMatches[0] == 0 && repeatMatches[1] == 2, "FindMatchIndices advances non-overlapping indices properly");

        var noMatches = StreamGuard.MainWindow.FindMatchIndices(searchSource, "không_tồn_tại");
        check(noMatches.Count == 0, "FindMatchIndices returns empty list when query does not match");

        check(StreamGuard.MainWindow.FindMatchIndices("", "test").Count == 0, "FindMatchIndices handles empty source string safely");
        check(StreamGuard.MainWindow.FindMatchIndices(searchSource, "").Count == 0, "FindMatchIndices handles empty query string safely");

        // 2. Extract recent segments: ExtractRecentSegments
        var txtSb = new StringBuilder();
        for (var i = 1; i <= 15; i++)
        {
            txtSb.AppendLine($"Câu số {i}");
            txtSb.AppendLine($"  -> Dịch câu số {i}");
            txtSb.AppendLine();
        }
        var txtContent = txtSb.ToString();
        var recent10Txt = StreamGuard.MainWindow.ExtractRecentSegments(txtContent, 10, isSrt: false);
        check(!recent10Txt.Contains("Câu số 5") && recent10Txt.Contains("Câu số 6") && recent10Txt.Contains("Câu số 15"),
            "ExtractRecentSegments extracts the last 10 TXT segments from 15 segments");

        var fewTxt = "Đoạn 1\r\n\r\nĐoạn 2";
        var recentFew = StreamGuard.MainWindow.ExtractRecentSegments(fewTxt, 10, isSrt: false);
        check(recentFew.Contains("Đoạn 1") && recentFew.Contains("Đoạn 2"),
            "ExtractRecentSegments returns all segments when count is less than requested limit");

        var srtSb = new StringBuilder();
        for (var i = 1; i <= 12; i++)
        {
            srtSb.AppendLine(i.ToString());
            srtSb.AppendLine($"00:00:{i:D2},000 --> 00:00:{i:D2},500");
            srtSb.AppendLine($"Sentence {i}");
            srtSb.AppendLine($"Câu dịch {i}");
            srtSb.AppendLine();
        }
        var srtContent = srtSb.ToString();
        var recent10Srt = StreamGuard.MainWindow.ExtractRecentSegments(srtContent, 10, isSrt: true);
        check(!recent10Srt.Contains("Sentence 2\r\n") && recent10Srt.Contains("Sentence 3") && recent10Srt.Contains("Sentence 12"),
            "ExtractRecentSegments extracts the last 10 SRT subtitle blocks accurately");

        check(StreamGuard.MainWindow.ExtractRecentSegments("", 10, false) == "", "ExtractRecentSegments returns empty on blank text");
        check(StreamGuard.MainWindow.ExtractRecentSegments(txtContent, 0, false) == "", "ExtractRecentSegments returns empty when count <= 0");

        var singleTxt = "Câu duy nhất\r\n  -> Bản dịch duy nhất\r\n\r\n";
        var recentSingle = StreamGuard.MainWindow.ExtractRecentSegments(singleTxt, 10, isSrt: false);
        check(recentSingle.Contains("Câu duy nhất\r\n  -> Bản dịch duy nhất") && !recentSingle.Contains("Câu duy nhất\r\n\r\n  ->"),
            "ExtractRecentSegments keeps single TXT sentence and its translation unified in one block");

        var plainLines = "Dòng 1\nDòng 2\nDòng 3";
        var recentPlain = StreamGuard.MainWindow.ExtractRecentSegments(plainLines, 2, isSrt: false);
        check(recentPlain.Contains("Dòng 2") && recentPlain.Contains("Dòng 3") && !recentPlain.Contains("Dòng 1"),
            "ExtractRecentSegments correctly handles text without blank lines");
    }
}
