using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace StreamGuard.Services;

/// <summary>
/// Provides intelligent vocabulary priming, phonetic mistake correction,
/// and technical jargon preservation across live captions and translations.
/// </summary>
public sealed class CaptionVocabularyService
{
    private static readonly (string Pattern, string Replacement)[] BuiltInCorrections = new[]
    {
        (@"\banti[\s\-]gravity\b", "Antigravity"),
        (@"\bjiminy\b", "Gemini"),
        (@"\bdriver[\s\-]?booster\b", "Driver Booster"),
        (@"\bstream[\s\-]?guard\b", "StreamGuard"),
        (@"\bc[\s\-]sharp\b", "C#"),
        (@"\bdot[\s\-]net\b", ".NET"),
        (@"\bdotnet\b", ".NET"),
        (@"\bpy[\s\-]torch\b", "PyTorch"),
        (@"\btensor[\s\-]flow\b", "TensorFlow"),
        (@"\bopen[\s\-]ai\b", "OpenAI"),
        (@"\b(coobernetes|kube[\s\-]netes)\b", "Kubernetes"),
        (@"\bchat[\s\-]gpt\b", "ChatGPT"),
        (@"\bdeep[\s\-]mind\b", "DeepMind"),
        (@"\bweb[\s\-]assembly\b", "WebAssembly"),
    };

    private readonly List<(Regex Regex, string Replacement)> _compiledRules = new();
    private readonly HashSet<string> _jargonKeywords = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _syncLock = new();

    public const int MaxKeywordsCount = 100;
    public const int MaxTotalKeywordsLength = 2000;
    public const int MinKeywordLength = 2;
    public const int MaxKeywordLength = 64;

    public CaptionVocabularyService(string? customKeywords = null)
    {
        UpdateKeywords(customKeywords);
    }

    public static bool TryValidateKeywords(string? rawInput, out string normalizedKeywords, out string errorKey)
    {
        normalizedKeywords = string.Empty;
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            errorKey = "Captions.VocabErrorEmpty";
            return false;
        }

        if (rawInput.Length > MaxTotalKeywordsLength)
        {
            errorKey = "Captions.VocabErrorTooLong";
            return false;
        }

        // Reject control characters (newlines/tabs/nulls) that break single-line keyword rules
        if (rawInput.Any(c => char.IsControl(c)))
        {
            errorKey = "Captions.VocabErrorInvalidChars";
            return false;
        }

        var rawSegments = rawInput.Split(',');
        var validTerms = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawSegment in rawSegments)
        {
            var term = rawSegment.Trim();
            if (string.IsNullOrEmpty(term))
                continue;

            if (term.Length < MinKeywordLength || term.Length > MaxKeywordLength || !term.Any(char.IsLetterOrDigit))
            {
                errorKey = "Captions.VocabErrorInvalidTerm";
                return false;
            }

            if (seen.Add(term))
            {
                validTerms.Add(term);
            }
        }

        if (validTerms.Count == 0)
        {
            errorKey = "Captions.VocabErrorEmpty";
            return false;
        }

        if (validTerms.Count > MaxKeywordsCount)
        {
            errorKey = "Captions.VocabErrorTooMany";
            return false;
        }

        normalizedKeywords = string.Join(", ", validTerms);
        errorKey = string.Empty;
        return true;
    }

    /// <summary>
    /// Updates custom user keywords and rebuilds the correction rules.
    /// </summary>
    public void UpdateKeywords(string? customKeywords)
    {
        lock (_syncLock)
        {
            _compiledRules.Clear();
            _jargonKeywords.Clear();

            // 1. Add built-in rules
            foreach (var (pattern, replacement) in BuiltInCorrections)
            {
                _compiledRules.Add((new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled), replacement));
                _jargonKeywords.Add(replacement);
            }

            // 2. Parse and add user custom keywords
            if (!string.IsNullOrWhiteSpace(customKeywords))
            {
                var terms = customKeywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var term in terms)
                {
                    if (term.Length < 2) continue;
                    _jargonKeywords.Add(term);

                    // Add regex for word boundaries to enforce proper casing
                    var escaped = Regex.Escape(term);
                    var pattern = $@"\b{escaped}\b";
                    _compiledRules.Add((new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled), term));
                }
            }
        }
    }

    /// <summary>
    /// Corrects common acoustic recognition mistakes and enforces proper keyword capitalization.
    /// </summary>
    public string CorrectText(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        var result = input;
        lock (_syncLock)
        {
            foreach (var (regex, replacement) in _compiledRules)
            {
                if (regex.IsMatch(result))
                {
                    result = regex.Replace(result, replacement);
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Ensures that important technical terms and brand names present in the English original
    /// are not mangled or mistranslated literally in the Vietnamese text.
    /// </summary>
    public string PreserveJargonInTranslation(string englishOriginal, string vietnameseTranslation)
    {
        if (string.IsNullOrWhiteSpace(englishOriginal) || string.IsNullOrWhiteSpace(vietnameseTranslation))
            return vietnameseTranslation;

        var result = vietnameseTranslation;
        lock (_syncLock)
        {
            foreach (var term in _jargonKeywords)
            {
                if (englishOriginal.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    // If the term was completely omitted or translated literally, ensure it remains preserved.
                    // For example: "bộ tăng áp trình điều khiển" -> "Driver Booster"
                    if (term.Equals("Driver Booster", StringComparison.OrdinalIgnoreCase))
                    {
                        result = Regex.Replace(result, @"\bbộ tăng áp( trình điều khiển| driver)?\b", "Driver Booster", RegexOptions.IgnoreCase);
                    }
                    else if (term.Equals("Antigravity", StringComparison.OrdinalIgnoreCase))
                    {
                        result = Regex.Replace(result, @"\bchống trọng lực\b", "Antigravity", RegexOptions.IgnoreCase);
                    }
                    else if (term.Equals("StreamGuard", StringComparison.OrdinalIgnoreCase))
                    {
                        result = Regex.Replace(result, @"\bbảo vệ luồng\b", "StreamGuard", RegexOptions.IgnoreCase);
                    }
                }
            }
        }
        return result;
    }

    public IReadOnlyCollection<string> JargonKeywords
    {
        get
        {
            lock (_syncLock)
            {
                return _jargonKeywords.ToList();
            }
        }
    }
}
