using System.Text;
using System.Text.RegularExpressions;

namespace StreamGuard.Services;

/// <summary>
/// Pure formatter that detects TOEIC / multiple-choice (A, B, C, D) Q&amp;A patterns
/// in raw .txt caption transcripts and formats them into clean, readable blocks
/// without modifying the underlying saved files.
/// </summary>
public static partial class TranscriptToeicFormatter
{
    public const string BlockDivider = "────────────────────────────";

    private enum MarkerKind
    {
        Parenthesized, // (A)
        OptionKeyword, // Option A
        Punctuated,    // A. / A) / A:
        Comma          // A,
    }

    private sealed record RawSegment(string Text, string? Translation);

    private sealed record ParsedMarker(
        int Index,
        int Length,
        char Letter,
        MarkerKind Kind);

    private sealed record FlowItem(
        int SegmentIndex,
        bool IsOption,
        char Letter,
        MarkerKind Kind,
        string Text,
        string? Translation);

    private sealed record OptionEntry(
        char Letter,
        string Text,
        string? Translation);

    // Matches:
    //  1) (A) .. (D)
    //  2) Option A[:.,-]? .. Option D[:.,-]?
    //  3) A. / A) / A: / A, .. D. / D) / D: / D,
    // Preceded by start of string, whitespace, or sentence/opening punctuation (not '.', so U.S.A. is excluded).
    [GeneratedRegex(
        @"(?<=^|[\s\?\!\,\;\:\-\""\(\[])(?:" +
        @"(?<paren>\((?<pLetter>[A-D])\)\s*)" +
        @"|(?<opt>(?i:Option)\s+(?<oLetter>[A-Da-d])(?:\s*[:\.\,\-])?\s+)" +
        @"|(?<punct>(?<qLetter>[A-D])(?<qSep>[\.\)\:])\s+)" +
        @"|(?<comma>(?<cLetter>[A-D]),\s+)" +
        @")",
        RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex OptionMarkerRegex();

    [GeneratedRegex(
        @"^(?:Question|Number|No\.?|Part)\s*\d+[\.\:\-]?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuestionHeaderOnlyRegex();

    public static string Format(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return "";

        var segments = ParseRawSegments(rawText);
        if (segments.Count == 0)
            return "";

        var items = BuildFlowItems(segments);
        if (items.Count == 0)
            return "";

        // Validate which option runs are genuine TOEIC blocks vs stray prose letters.
        var validOptionFlags = ValidateOptionRuns(items);

        return RenderFormatted(segments, items, validOptionFlags);
    }

    private static List<RawSegment> ParseRawSegments(string rawText)
    {
        var normalized = rawText.Replace("\r\n", "\n").Replace('\r', '\n').Trim('\n');
        var lines = normalized.Split('\n');
        var hasBlankLines = lines.Any(string.IsNullOrWhiteSpace);

        var segments = new List<RawSegment>();
        var currentText = new StringBuilder();
        var currentTrans = new StringBuilder();

        void Flush()
        {
            var text = currentText.ToString().Trim();
            var trans = currentTrans.ToString().Trim();
            if (text.Length > 0)
            {
                segments.Add(new RawSegment(text, trans.Length > 0 ? trans : null));
            }
            else if (trans.Length > 0)
            {
                segments.Add(new RawSegment("-> " + trans, null));
            }
            currentText.Clear();
            currentTrans.Clear();
        }

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();
            if (trimmed.Length == 0)
            {
                Flush();
                continue;
            }

            if (trimmed.StartsWith("->", StringComparison.Ordinal))
            {
                var transPart = trimmed[2..].Trim();
                if (transPart.Length > 0)
                {
                    if (currentTrans.Length > 0) currentTrans.Append(' ');
                    currentTrans.Append(transPart);
                }
                continue;
            }

            // If a new text line starts right after a translation without a blank line, flush previous segment.
            if (currentTrans.Length > 0 || (!hasBlankLines && currentText.Length > 0))
            {
                Flush();
            }

            if (currentText.Length > 0) currentText.Append(' ');
            currentText.Append(trimmed);
        }

        Flush();
        return segments;
    }

    private static List<ParsedMarker> FindMarkers(string text)
    {
        var list = new List<ParsedMarker>();
        if (string.IsNullOrWhiteSpace(text)) return list;

        foreach (Match m in OptionMarkerRegex().Matches(text))
        {
            if (m.Groups["paren"].Success)
            {
                list.Add(new ParsedMarker(m.Index, m.Length, m.Groups["pLetter"].Value[0], MarkerKind.Parenthesized));
            }
            else if (m.Groups["opt"].Success)
            {
                var letter = char.ToUpperInvariant(m.Groups["oLetter"].Value[0]);
                list.Add(new ParsedMarker(m.Index, m.Length, letter, MarkerKind.OptionKeyword));
            }
            else if (m.Groups["punct"].Success)
            {
                list.Add(new ParsedMarker(m.Index, m.Length, m.Groups["qLetter"].Value[0], MarkerKind.Punctuated));
            }
            else if (m.Groups["comma"].Success)
            {
                list.Add(new ParsedMarker(m.Index, m.Length, m.Groups["cLetter"].Value[0], MarkerKind.Comma));
            }
        }

        return list;
    }

    private static bool HasMeaningfulOptionBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;
        var trimmed = body.Trim();
        // Reject empty or single-letter/punctuation fragments like "A, B, C, D",
        // while allowing short numeric answers like "$5.", "5%.", or "9".
        var alnumCount = 0;
        var hasDigit = false;
        foreach (var ch in trimmed)
        {
            if (char.IsLetterOrDigit(ch)) alnumCount++;
            if (char.IsDigit(ch)) hasDigit = true;
        }
        return alnumCount >= 2 || hasDigit;
    }

    private static List<FlowItem> BuildFlowItems(List<RawSegment> segments)
    {
        var items = new List<FlowItem>();

        for (var segIndex = 0; segIndex < segments.Count; segIndex++)
        {
            var seg = segments[segIndex];
            var markers = FindMarkers(seg.Text);
            if (markers.Any(m => m.Kind is MarkerKind.Parenthesized or MarkerKind.OptionKeyword))
            {
                markers = markers.Where(m => m.Kind != MarkerKind.Comma).ToList();
            }
            if (markers.Count == 0)
            {
                items.Add(new FlowItem(segIndex, false, '\0', MarkerKind.Parenthesized, seg.Text, seg.Translation));
                continue;
            }

            // Filter out markers that produce empty bodies (e.g., "A, B, C, D").
            var validMarkers = new List<ParsedMarker>();
            for (var i = 0; i < markers.Count; i++)
            {
                var start = markers[i].Index + markers[i].Length;
                var end = i + 1 < markers.Count ? markers[i + 1].Index : seg.Text.Length;
                var body = seg.Text[start..end].Trim();
                if (HasMeaningfulOptionBody(body))
                {
                    validMarkers.Add(markers[i]);
                }
            }

            if (validMarkers.Count == 0)
            {
                items.Add(new FlowItem(segIndex, false, '\0', MarkerKind.Parenthesized, seg.Text, seg.Translation));
                continue;
            }

            // Try to split translation if it also contains matching option markers.
            string? prefixTrans = null;
            Dictionary<char, string>? optionTransMap = null;
            if (!string.IsNullOrWhiteSpace(seg.Translation))
            {
                var transMarkers = FindMarkers(seg.Translation);
                if (transMarkers.Count > 0)
                {
                    var transPrefix = seg.Translation[..transMarkers[0].Index].Trim();
                    if (transPrefix.Length > 0) prefixTrans = transPrefix;

                    optionTransMap = new Dictionary<char, string>();
                    for (var i = 0; i < transMarkers.Count; i++)
                    {
                        var start = transMarkers[i].Index + transMarkers[i].Length;
                        var end = i + 1 < transMarkers.Count ? transMarkers[i + 1].Index : seg.Translation.Length;
                        var body = seg.Translation[start..end].Trim();
                        if (body.Length > 0 && !optionTransMap.ContainsKey(transMarkers[i].Letter))
                        {
                            optionTransMap[transMarkers[i].Letter] = body;
                        }
                    }
                }
            }

            var prefixText = seg.Text[..validMarkers[0].Index].Trim();
            if (prefixText.Length > 0)
            {
                var leadTrans = prefixTrans ?? (optionTransMap == null ? seg.Translation : null);
                items.Add(new FlowItem(segIndex, false, '\0', MarkerKind.Parenthesized, prefixText, leadTrans));
            }

            for (var i = 0; i < validMarkers.Count; i++)
            {
                var marker = validMarkers[i];
                var start = marker.Index + marker.Length;
                var end = i + 1 < validMarkers.Count ? validMarkers[i + 1].Index : seg.Text.Length;
                var body = seg.Text[start..end].Trim();

                string? itemTrans = null;
                if (optionTransMap != null && optionTransMap.TryGetValue(marker.Letter, out var mappedTrans))
                {
                    itemTrans = mappedTrans;
                }
                else if (optionTransMap == null && prefixText.Length == 0 && validMarkers.Count == 1)
                {
                    // Single option in this segment with a single translation line.
                    itemTrans = seg.Translation;
                }

                items.Add(new FlowItem(segIndex, true, marker.Letter, marker.Kind, body, itemTrans));
            }
        }

        return items;
    }

    private static bool[] ValidateOptionRuns(List<FlowItem> items)
    {
        var valid = new bool[items.Count];
        var i = 0;

        while (i < items.Count)
        {
            if (!items[i].IsOption || items[i].Letter != 'A')
            {
                i++;
                continue;
            }

            // Collect sequential A -> B -> C -> D run.
            var runIndices = new List<int> { i };
            var expectedNext = 'B';
            var j = i + 1;

            while (j < items.Count && expectedNext <= 'D')
            {
                if (items[j].IsOption && items[j].Letter == expectedNext)
                {
                    runIndices.Add(j);
                    expectedNext++;
                    j++;
                }
                else
                {
                    break;
                }
            }

            // Determine if this run is a genuine TOEIC option block:
            // - Parenthesized (A) or Option A: unambiguous even for 1+ items (supports live streaming).
            // - Punctuated (A. / A) / A:): requires at least A and B in sequence.
            // - Comma (A,): requires at least A, B, C in sequence to avoid false positives in prose.
            var firstKind = items[i].Kind;
            var isGenuine = firstKind switch
            {
                MarkerKind.Parenthesized => true,
                MarkerKind.OptionKeyword => true,
                MarkerKind.Punctuated => runIndices.Count >= 2,
                MarkerKind.Comma => runIndices.Count >= 3,
                _ => false
            };

            if (isGenuine)
            {
                foreach (var idx in runIndices)
                {
                    valid[idx] = true;
                }
            }

            i = j;
        }

        return valid;
    }

    private static string RenderFormatted(List<RawSegment> segments, List<FlowItem> items, bool[] validOptionFlags)
    {
        var sb = new StringBuilder();
        var consumedAsLeadIn = new bool[items.Count];
        var segmentHasValidOption = new bool[segments.Count];
        for (var i = 0; i < items.Count; i++)
        {
            if (validOptionFlags[i])
                segmentHasValidOption[items[i].SegmentIndex] = true;
        }

        // Pre-mark lead-in questions that immediately precede a valid 'A' option.
        for (var i = 0; i < items.Count; i++)
        {
            if (validOptionFlags[i] && items[i].Letter == 'A' && i > 0 && !validOptionFlags[i - 1])
            {
                consumedAsLeadIn[i - 1] = true;
                // Also include an immediately preceding short "Question N." / "Number N." header if present.
                if (i >= 2 && !validOptionFlags[i - 2] && !consumedAsLeadIn[i - 2] &&
                    QuestionHeaderOnlyRegex().IsMatch(items[i - 2].Text))
                {
                    consumedAsLeadIn[i - 2] = true;
                }
            }
        }

        var idx = 0;
        while (idx < items.Count)
        {
            if (validOptionFlags[idx] && items[idx].Letter == 'A')
            {
                // Start of a TOEIC block without a preceding lead-in text.
                AppendToeicBlock(sb, leadIns: null, items, validOptionFlags, ref idx);
                continue;
            }

            if (consumedAsLeadIn[idx])
            {
                var leadIns = new List<FlowItem>();
                while (idx < items.Count && consumedAsLeadIn[idx])
                {
                    leadIns.Add(items[idx]);
                    idx++;
                }
                AppendToeicBlock(sb, leadIns, items, validOptionFlags, ref idx);
                continue;
            }

            // Plain conversational segment (non-TOEIC): if the segment had false-positive candidate markers
            // that were all rejected, emit the original untouched segment text and skip its split items.
            var segIdx = items[idx].SegmentIndex;
            if (!segmentHasValidOption[segIdx])
            {
                var rawSeg = segments[segIdx];
                sb.AppendLine(rawSeg.Text);
                if (!string.IsNullOrWhiteSpace(rawSeg.Translation))
                {
                    sb.Append("  -> ").AppendLine(rawSeg.Translation);
                }
                sb.AppendLine();
                while (idx < items.Count && items[idx].SegmentIndex == segIdx && !consumedAsLeadIn[idx] && !validOptionFlags[idx])
                {
                    idx++;
                }
                continue;
            }

            var plain = items[idx];
            sb.AppendLine(plain.Text);
            if (!string.IsNullOrWhiteSpace(plain.Translation))
            {
                sb.Append("  -> ").AppendLine(plain.Translation);
            }
            sb.AppendLine();
            idx++;
        }

        return sb.ToString();
    }

    private static void AppendToeicBlock(
        StringBuilder sb,
        List<FlowItem>? leadIns,
        List<FlowItem> items,
        bool[] validOptionFlags,
        ref int idx)
    {
        sb.AppendLine(BlockDivider);

        if (leadIns != null && leadIns.Count > 0)
        {
            if (leadIns.Count == 2 && QuestionHeaderOnlyRegex().IsMatch(leadIns[0].Text))
            {
                // Merge "Question 1." + "Where is the meeting room?" cleanly.
                var combinedQ = leadIns[0].Text.Trim() + " " + leadIns[1].Text.Trim();
                sb.AppendLine(combinedQ);
                var combinedTrans = CombineTranslations(leadIns[0].Translation, leadIns[1].Translation);
                if (!string.IsNullOrWhiteSpace(combinedTrans))
                {
                    sb.Append("  -> ").AppendLine(combinedTrans);
                }
            }
            else
            {
                foreach (var lead in leadIns)
                {
                    sb.AppendLine(lead.Text);
                    if (!string.IsNullOrWhiteSpace(lead.Translation))
                    {
                        sb.Append("  -> ").AppendLine(lead.Translation);
                    }
                }
            }
            sb.AppendLine();
        }

        var options = new List<OptionEntry>();
        while (idx < items.Count && validOptionFlags[idx])
        {
            if (options.Count > 0 && items[idx].Letter == 'A')
                break;

            options.Add(new OptionEntry(items[idx].Letter, items[idx].Text, items[idx].Translation));
            idx++;
        }

        foreach (var opt in options)
        {
            sb.Append("  (").Append(opt.Letter).Append(") ").AppendLine(opt.Text);
            if (!string.IsNullOrWhiteSpace(opt.Translation))
            {
                sb.Append("      -> ").AppendLine(opt.Translation);
            }
        }

        sb.AppendLine();
    }

    private static string? CombineTranslations(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first)) return second;
        if (string.IsNullOrWhiteSpace(second)) return first;
        return first.Trim() + " " + second.Trim();
    }
}
