using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

// Institutional/finance news via MarketAux (entity-tagged articles with
// per-entity sentiment). Query: symbols + filter_entities + language +
// published_after/before window. Free tier: 100 req/day, ~3 articles/req.
// Point-in-time capable: every article carries its own published_at (UTC);
// items after the cutoff are excluded client-side regardless of the
// published_before bound. The token travels in MarketAux's query-string scheme
// server-side only: never logged (URLs are never logged here), never returned.
public class MarketAuxNewsProvider : INewsProvider
{
    private readonly HttpClient _http;
    private readonly ILogger<MarketAuxNewsProvider> _logger;
    private readonly string _apiKey;
    private readonly string _baseUrl;

    public MarketAuxNewsProvider(HttpClient http, ILogger<MarketAuxNewsProvider> logger, IConfiguration config)
    {
        _http = http;
        _logger = logger;
        _apiKey = config["MarketAux:ApiKey"] ?? "";
        _baseUrl = (config["MarketAux:BaseUrl"] ?? "https://api.marketaux.com").TrimEnd('/');
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    public Task<IReadOnlyList<NewsArticle>> SearchAsync(string symbol, DateOnly cutoffDate, CancellationToken ct = default) =>
        SearchAsync(symbol, companyName: null, cutoffDate, ct);

    public async Task<IReadOnlyList<NewsArticle>> SearchAsync(string symbol, string? companyName, DateOnly cutoffDate, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _logger.LogDebug("MarketAux API key not configured; unavailable for {Symbol}", symbol);
            return Array.Empty<NewsArticle>();
        }

        var normalized = symbol.Trim().ToUpperInvariant();
        var cutoff = TemporalBoundary.GetCutoffUtc(cutoffDate);
        // published_before is day-granular-safe: push a day out, then enforce the
        // exact cutoff client-side (defense in depth, same as other providers).
        var after = cutoffDate.AddDays(-7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var before = cutoffDate.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var url = $"{_baseUrl}/v1/news/all?symbols={Uri.EscapeDataString(normalized)}" +
                  $"&filter_entities=true&language=en&published_after={after}&published_before={before}" +
                  $"&limit=20&api_token={Uri.EscapeDataString(_apiKey)}";
        _logger.LogInformation("Fetching MarketAux news for {Symbol} between {Start} and {End}", normalized, after, before);

        try
        {
            using var response = await _http.GetAsync(url, ct);
            if ((int)response.StatusCode == 429)
                throw new RateLimitExceededException("MarketAux rate limit exceeded.");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return Array.Empty<NewsArticle>();

            var results = new List<NewsArticle>();
            foreach (var item in data.EnumerateArray())
            {
                var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var urlVal = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(urlVal))
                    continue;

                var rawDate = item.TryGetProperty("published_at", out var p) ? p.GetString() ?? "" : "";
                if (!DateTime.TryParse(rawDate, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var published))
                    continue;
                published = DateTime.SpecifyKind(published, DateTimeKind.Utc);
                if (published > cutoff)
                    continue;

                var source = item.TryGetProperty("source", out var s) ? s.GetString() ?? "" : "";
                var snippet = item.TryGetProperty("snippet", out var sn) ? sn.GetString() ?? "" : "";
                var uuid = item.TryGetProperty("uuid", out var id) ? id.GetString() ?? "" : "";

                results.Add(new NewsArticle
                {
                    Id = string.IsNullOrEmpty(uuid) ? DeterministicId(urlVal) : "mux-" + uuid,
                    Title = title,
                    Description = snippet,
                    Source = string.IsNullOrEmpty(source) ? "MarketAux" : $"MarketAux via {source}",
                    PublishedAt = published,
                    Url = urlVal,
                    CompanySymbol = normalized
                });

                if (results.Count >= 20)
                    break;
            }

            _logger.LogInformation("Found {Count} MarketAux articles for {Symbol}", results.Count, normalized);
            return results;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "MarketAux request timed out for {Symbol}", symbol);
            return Array.Empty<NewsArticle>();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "MarketAux request failed for {Symbol}", symbol);
            return Array.Empty<NewsArticle>();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "MarketAux JSON parse failed for {Symbol}", symbol);
            return Array.Empty<NewsArticle>();
        }
    }

    public static string DeterministicId(string url)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url.Trim().ToLowerInvariant()));
        return "mux-" + Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }
}
