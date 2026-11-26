using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace StreamGuard.Services;

/// <summary>
/// Provides ultra-fast, resilient real-time translation from English to Vietnamese
/// with in-memory caching, HTML entity decoding, circuit breaker, and dual-engine fallback.
/// </summary>
public sealed class CaptionTranslationService : IDisposable
{
    private static readonly SocketsHttpHandler HttpHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        ConnectTimeout = TimeSpan.FromSeconds(1.5),
        EnableMultipleHttp2Connections = true
    };

    private static readonly HttpClient HttpClient = new(HttpHandler)
    {
        Timeout = TimeSpan.FromSeconds(2.5)
    };

    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, CancellationToken, Task<string?>>? _customTranslator;
    private const int MaxCacheEntries = 2000;

    private int _consecutiveFailures;
    private DateTime _circuitBreakUntilUtc = DateTime.MinValue;
    private bool _disposed;

    static CaptionTranslationService()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    public CaptionTranslationService(Func<string, CancellationToken, Task<string?>>? customTranslator = null)
    {
        _customTranslator = customTranslator;
    }

    /// <summary>
    /// Translates English text to Vietnamese asynchronously with low latency and cache lookup.
    /// Falls back to original text immediately if offline or on network failure.
    /// </summary>
    public async Task<string> TranslateToVietnameseAsync(string englishText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(englishText))
            return englishText;

        var clean = englishText.Trim();
        if (clean.Length == 0)
            return englishText;

        // Skip translation for pure numbers, symbols or punctuation
        if (!Regex.IsMatch(clean, @"\p{L}"))
            return clean;

        if (_cache.TryGetValue(clean, out var cached))
            return cached;

        // Custom translator for mocking or external engines
        if (_customTranslator != null)
        {
            try
            {
                var custom = await _customTranslator(clean, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(custom))
                {
                    var decodedCustom = WebUtility.HtmlDecode(custom.Trim());
                    CacheResult(clean, decodedCustom);
                    return decodedCustom;
                }
            }
            catch { }
            return clean;
        }

        // Fast-fail if Windows network adapter reports no connection
        if (!NetworkInterface.GetIsNetworkAvailable())
            return clean;

        // Fast-fail if Circuit Breaker is active due to recent consecutive network outages
        if (DateTime.UtcNow < _circuitBreakUntilUtc)
            return clean;

        // Engine 1: Google Clients5 Dict Chrome Extension endpoint with 1.2s bounded timeout
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(1200));

            var gResult = await QueryGoogleClients5Async(clean, timeoutCts.Token).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(gResult))
            {
                RecordSuccess();
                var decoded = WebUtility.HtmlDecode(gResult);
                CacheResult(clean, decoded);
                return decoded;
            }
        }
        catch { }

        // Engine 2: Fallback to MyMemory API with 1.2s bounded timeout
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(1200));

            var mResult = await QueryMyMemoryAsync(clean, timeoutCts.Token).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(mResult))
            {
                RecordSuccess();
                var decoded = WebUtility.HtmlDecode(mResult);
                CacheResult(clean, decoded);
                return decoded;
            }
        }
        catch { }

        // Record failure for Circuit Breaker and fallback safely
        RecordFailure();
        return clean;
    }

    private static async Task<string?> QueryGoogleClients5Async(string text, CancellationToken ct)
    {
        var url = "https://clients5.google.com/translate_a/t?client=dict-chrome-ex&sl=en&tl=vi&q=" + Uri.EscapeDataString(text);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
            return null;

        // Parse JSON array: may contain single string, array of strings, or array of nested arrays
        var token = JToken.Parse(json);
        if (token is JArray arr && arr.Count > 0)
        {
            var sb = new StringBuilder();
            foreach (var item in arr)
            {
                if (item is JArray nested && nested.Count > 0)
                {
                    sb.Append(nested[0]?.ToString());
                }
                else if (item is JValue val)
                {
                    sb.Append(val.ToString());
                }
            }
            var result = sb.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(result))
                return result;
        }

        return token.ToString().Trim();
    }

    private static async Task<string?> QueryMyMemoryAsync(string text, CancellationToken ct)
    {
        var url = "https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(text) + "&langpair=en|vi";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var root = JObject.Parse(json);
        var translated = root.SelectToken("responseData.translatedText")?.ToString();
        if (!string.IsNullOrWhiteSpace(translated) && !translated.StartsWith("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase))
        {
            return translated.Trim();
        }

        return null;
    }

    private void CacheResult(string key, string value)
    {
        if (_cache.Count >= MaxCacheEntries)
        {
            _cache.Clear();
        }
        _cache[key] = value;
    }

    private void RecordSuccess()
    {
        _consecutiveFailures = 0;
        _circuitBreakUntilUtc = DateTime.MinValue;
    }

    private void RecordFailure()
    {
        if (Interlocked.Increment(ref _consecutiveFailures) >= 2)
        {
            _circuitBreakUntilUtc = DateTime.UtcNow.AddSeconds(15);
        }
    }

    public bool IsCircuitBroken => DateTime.UtcNow < _circuitBreakUntilUtc;

    public void ResetCircuitBreaker()
    {
        _consecutiveFailures = 0;
        _circuitBreakUntilUtc = DateTime.MinValue;
    }

    public int CacheCount => _cache.Count;

    public void ClearCache() => _cache.Clear();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cache.Clear();
    }
}
