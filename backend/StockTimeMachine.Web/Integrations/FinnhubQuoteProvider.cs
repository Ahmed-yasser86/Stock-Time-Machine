using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using StockTimeMachine;

namespace StockTimeMachine.Web.Integrations;

// Live (delayed) quotes from Finnhub for the "What Happened Afterwards" reveal.
// Server-side only: the Finnhub token never leaves the backend — the browser
// gets numbers, never credentials. Finnhub free tier allows 60 calls/minute;
// quotes are cached for 60 seconds per symbol to stay far under the limit.
public sealed class FinnhubQuoteProvider : IQuoteProvider
{
    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<FinnhubQuoteProvider> _logger;
    private readonly string _token;

    public FinnhubQuoteProvider(
        HttpClient http,
        IMemoryCache cache,
        IConfiguration config,
        ILogger<FinnhubQuoteProvider> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
        // Existing TradingOptions:FinnhubToken configuration — read, never moved,
        // never duplicated, never logged, never returned in API responses.
        _token = config["TradingOptions:FinnhubToken"] ?? "";
    }

    public async Task<LiveQuote?> GetQuoteAsync(string symbol, CancellationToken ct = default)
    {
        var normalized = symbol.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(_token))
        {
            _logger.LogDebug("Finnhub token not configured; live quote unavailable for {Symbol}", normalized);
            return null;
        }

        if (_cache.TryGetValue(CacheKey(normalized), out LiveQuote? cached) && cached is not null)
            return cached;

        try
        {
            var url = $"https://finnhub.io/api/v1/quote?symbol={Uri.EscapeDataString(normalized)}&token={_token}";
            using var response = await _http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!TryGetDecimal(root, "c", out var current) || current <= 0)
                return null;

            TryGetDecimal(root, "d", out var change);
            TryGetDecimal(root, "dp", out var percentChange);
            TryGetDecimal(root, "h", out var high);
            TryGetDecimal(root, "l", out var low);
            TryGetDecimal(root, "o", out var open);
            TryGetDecimal(root, "pc", out var prevClose);

            var quote = new LiveQuote(
                Symbol: normalized,
                CurrentPrice: current,
                Change: change,
                PercentChange: percentChange,
                High: high,
                Low: low,
                Open: open,
                PreviousClose: prevClose,
                AsOfUtc: DateTime.UtcNow,
                Source: "Finnhub");

            _cache.Set(CacheKey(normalized), quote, TimeSpan.FromSeconds(60));
            return quote;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // A live quote is enrichment, never load-bearing: failures degrade
            // to an honest empty state instead of failing the investigation.
            _logger.LogWarning(ex, "Finnhub quote unavailable for {Symbol}", normalized);
            return null;
        }
    }

    private static string CacheKey(string symbol) => $"finnhub:quote:{symbol}";

    private static bool TryGetDecimal(JsonElement root, string property, out decimal value)
    {
        value = 0;
        if (!root.TryGetProperty(property, out var el))
            return false;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out value))
            return true;
        return el.ValueKind == JsonValueKind.String &&
               decimal.TryParse(el.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }
}
