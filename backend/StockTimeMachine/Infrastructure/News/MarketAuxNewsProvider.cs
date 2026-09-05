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
    private readonly AdaptiveRateLimiter _limiter;

    public MarketAuxNewsProvider(HttpClient http, ILogger<MarketAuxNewsProvider> logger, IConfiguration config)
    {
        _http = http;
        _logger = logger;
        _apiKey = config["MarketAux:ApiKey"] ?? "";
        _baseUrl = (config["MarketAux:BaseUrl"] ?? "https://api.marketaux.com").TrimEnd('/');
        _limiter = RateLimiterRegistry.Get("marketaux", config);
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    public Task<IReadOnlyList<NewsArticle>> SearchAsync(string symbol, DateOnly cutoffDate, CancellationToken ct = default) =>
        SearchAsync(symbol, companyName: null, cutoffDate, ct);

    // Interval-complete retrieval: the trailing window is traversed one
    // deterministic day at a time (limit is per-request, so one range query
    // lets the busiest day starve the rest). Days merged, deduped by article
    // id, newest-first. A 429 aborts remaining days; other per-day failures
    // degrade loudly without voiding good days.
    private const int WindowDaysBack = 7;
    private const int DayLimit = 20;

    public async Task<IReadOnlyList<NewsArticle>> SearchAsync(string symbol, string? companyName, DateOnly cutoffDate, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _logger.LogDebug("MarketAux API key not configured; unavailable for {Symbol}", symbol);
            return Array.Empty<NewsArticle>();
        }

        var normalized = symbol.Trim().ToUpperInvariant();
        var cutoff = TemporalBoundary.GetCutoffUtc(cutoffDate);
        var days = Enumerable.Range(0, WindowDaysBack + 1)
            .Select(i => cutoffDate.AddDays(-WindowDaysBack + i))
            .ToList();

        var results = new List<NewsArticle>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var failedDays = new List<DateOnly>();
        foreach (var day in days)
        {
            await _limiter.AcquireAsync(0, ct);
            try
            {
                foreach (var article in await FetchDay(normalized, day, cutoff, ct))
                {
                    if (seen.Add(article.Id))
                        results.Add(article);
                }
            }
            catch (RateLimitExceededException)
            {
                throw;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failedDays.Add(day);
                _logger.LogWarning(ex, "MarketAux day {Day} failed for {Symbol}; keeping other days", day, normalized);
            }
        }

        var represented = results
            .Select(n => DateOnly.FromDateTime(n.PublishedAt)).Distinct().OrderBy(d => d).ToList();
        _logger.LogInformation(
            "MarketAux coverage for {Symbol}: requested [{Start}..{End}], traversed {Traversed}/{Total} days, {Articles} articles, represented [{Represented}], failed [{Failed}]",
            normalized, days.First(), days.Last(), days.Count - failedDays.Count, days.Count, results.Count,
            string.Join(",", represented), string.Join(",", failedDays));

        return results
            .OrderByDescending(n => n.PublishedAt)
            .ThenBy(n => n.Id)
            .ToList();
    }

    private async Task<IReadOnlyList<NewsArticle>> FetchDay(
        string normalized, DateOnly day, DateTime cutoff, CancellationToken ct)
    {
        // Per-day bounds with the established +1-day overlap; the exact cutoff
        // is enforced client-side below regardless of provider windowing.
        var after = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var before = day.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var url = $"{_baseUrl}/v1/news/all?symbols={Uri.EscapeDataString(normalized)}" +
                  $"&filter_entities=true&language=en&published_after={after}&published_before={before}" +
                  $"&limit={DayLimit}&api_token={Uri.EscapeDataString(_apiKey)}";

        try
        {
            using var response = await _http.GetAsync(url, ct);
            if ((int)response.StatusCode == 429)
            {
                var retryAfter = RateLimitHeaders.ParseRetryAfter(response.Headers);
                _limiter.ReportThrottled(retryAfter);
                throw new RateLimitExceededException("MarketAux rate limit exceeded.", retryAfter);
            }
            response.EnsureSuccessStatusCode();
            _limiter.ReportSuccess();

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
                    CompanySymbol = normalized,
                    SentimentScore = ExtractSentiment(item, normalized),
                });

                if (results.Count >= DayLimit)
                    break;
            }

            return results;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "MarketAux request timed out for {Symbol}", normalized);
            return Array.Empty<NewsArticle>();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "MarketAux request failed for {Symbol}", normalized);
            return Array.Empty<NewsArticle>();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "MarketAux JSON parse failed for {Symbol}", normalized);
            return Array.Empty<NewsArticle>();
        }
    }

    // Per-entity sentiment for the requested symbol (-1..+1), null when the
    // article carries no entity match or no score.
    private static decimal? ExtractSentiment(JsonElement item, string symbol)
    {
        if (!item.TryGetProperty("entities", out var entities) || entities.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var entity in entities.EnumerateArray())
        {
            var entitySymbol = entity.TryGetProperty("symbol", out var sym) ? sym.GetString() ?? "" : "";
            if (!string.Equals(entitySymbol, symbol, StringComparison.OrdinalIgnoreCase))
                continue;
            if (entity.TryGetProperty("sentiment_score", out var score) &&
                score.ValueKind == JsonValueKind.Number &&
                score.TryGetDecimal(out var value))
                return Math.Round(value, 4);
            return null;
        }
        return null;
    }

    public static string DeterministicId(string url)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url.Trim().ToLowerInvariant()));
        return "mux-" + Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }
}
