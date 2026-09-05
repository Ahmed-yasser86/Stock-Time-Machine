using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

public class GdeltNewsProvider : INewsProvider
{
    private readonly HttpClient _http;
    private readonly ILogger<GdeltNewsProvider> _logger;
    private readonly IConfiguration _config;
    private readonly string _baseUrl;
    // Optional GDELT Cloud credential. Server-side only: read from configuration
    // (environment variable Gdelt__ApiKey), never logged, never sent to browsers.
    // When absent, the keyless GDELT Project API is used.
    private readonly string _cloudApiKey;
    private readonly AdaptiveRateLimiter _limiter;

    public GdeltNewsProvider(HttpClient http, ILogger<GdeltNewsProvider> logger, IConfiguration config)
    {
        _http = http;
        _logger = logger;
        _config = config;
        _baseUrl = (config["Gdelt:BaseUrl"] ?? "https://api.gdeltproject.org/api/v2").TrimEnd('/');
        _cloudApiKey = config["Gdelt:ApiKey"] ?? "";
        _limiter = RateLimiterRegistry.Get("gdelt", config);
    }

    public Task<IReadOnlyList<NewsArticle>> SearchAsync(string symbol, DateOnly cutoffDate, CancellationToken ct = default) =>
        GdeltResilience.ExecuteAsync(
            ct2 => SearchCoreAsync(symbol, cutoffDate, ct2),
            _logger, $"GDELT Project {symbol}", _config, ct);

    // Interval-complete retrieval, same contract as the Cloud provider: the
    // trailing window is traversed one deterministic day at a time so a
    // single maxrows-bounded query cannot starve quieter days. Keyless tier
    // stays at 20 rows/day; days merged, deduped by article id, newest-first.
    private const int WindowDaysBack = 7;
    private const int DayMaxRows = 20;

    private async Task<IReadOnlyList<NewsArticle>> SearchCoreAsync(string symbol, DateOnly cutoffDate, CancellationToken ct)
    {
        var cutoff = TemporalBoundary.GetCutoffUtc(cutoffDate);
        var query = Uri.EscapeDataString(symbol);
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
                foreach (var article in await FetchDay(query, symbol, day, cutoff, ct))
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
                // Genuine cancellation propagates; provider timeouts (OCE
                // with a live token) still degrade per-day below.
                throw;
            }
            catch (Exception ex)
            {
                failedDays.Add(day);
                _logger.LogWarning(ex, "GDELT day {Day} failed for {Symbol}; keeping other days", day, symbol);
            }
        }

        var represented = results
            .Select(n => DateOnly.FromDateTime(n.PublishedAt)).Distinct().OrderBy(d => d).ToList();
        _logger.LogInformation(
            "GDELT coverage for {Symbol}: requested [{Start}..{End}], traversed {Traversed}/{Total} days, {Articles} articles, represented [{Represented}], failed [{Failed}]",
            symbol, days.First(), days.Last(), days.Count - failedDays.Count, days.Count, results.Count,
            string.Join(",", represented), string.Join(",", failedDays));

        return results
            .OrderByDescending(n => n.PublishedAt)
            .ThenBy(n => n.Id)
            .ToList();
    }

    private async Task<IReadOnlyList<NewsArticle>> FetchDay(
        string query, string symbol, DateOnly day, DateTime cutoff, CancellationToken ct)
    {
        // Explicit full-day bounds (HHMMSS): date-only bounds are ambiguous
        // in the DOC API and could collapse a day to an empty instant.
        // Client-side cutoff filtering below still enforces the boundary.
        var from = day.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "000000";
        var to = day.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "235959";

        // Quoted multi-word queries reduce false positives (e.g. ticker "V").
        var url = $"{_baseUrl}/doc/search?query={query}&format=json&startdatetime={from}&enddatetime={to}&maxrows={DayMaxRows}&sort=datedesc";
        if (!string.IsNullOrEmpty(_cloudApiKey))
            url += $"&key={Uri.EscapeDataString(_cloudApiKey)}";

        try
        {
            using var response = await _http.GetAsync(url, ct);
            if ((int)response.StatusCode == 429)
                throw new RateLimitExceededException("GDELT Project rate limit exceeded.",
                    RateLimitHeaders.ParseRetryAfter(response.Headers));
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("articles", out var articles) ||
                articles.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("No articles returned from GDELT for {Symbol}", symbol);
                return Array.Empty<NewsArticle>();
            }

            var normalizedSymbol = symbol.ToUpperInvariant();
            var results = new List<NewsArticle>();
            foreach (var article in articles.EnumerateArray())
            {
                var title = GetString(article, "title");
                var urlVal = GetString(article, "url");
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(urlVal))
                    continue;

                // GDELT date shapes vary ("published_date", "seendate", "date").
                // Unparseable dates are SKIPPED — never coerced to the cutoff,
                // which would corrupt temporal integrity.
                var publishedAt = ParsePublishedAt(article);
                if (!publishedAt.HasValue)
                {
                    _logger.LogDebug("Skipping GDELT article with unparseable date for {Symbol}: {Title}", symbol, title);
                    continue;
                }
                if (publishedAt.Value > cutoff)
                    continue;

                results.Add(new NewsArticle
                {
                    Id = DeterministicId(urlVal),
                    Title = title,
                    Description = GetString(article, "snippet", "description", "excerpt"),
                    Source = string.IsNullOrEmpty(GetString(article, "domain"))
                        ? "GDELT Project"
                        : $"GDELT via {GetString(article, "domain")}",
                    PublishedAt = publishedAt.Value,
                    Url = urlVal,
                    CompanySymbol = normalizedSymbol
                });
            }

            _logger.LogInformation("Found {Count} news articles from GDELT for {Symbol}", results.Count, symbol);
            return results;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException ex)
        {
            // Provider timeout (HttpClient.Timeout): best-effort source degrades
            // to honest empty state. Genuine request cancellation still propagates.
            _logger.LogWarning(ex, "GDELT request timed out for {Symbol}", symbol);
            return Array.Empty<NewsArticle>();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "GDELT request failed for {Symbol}", symbol);
            return Array.Empty<NewsArticle>();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "GDELT JSON parse failed for {Symbol}", symbol);
            return Array.Empty<NewsArticle>();
        }
    }

    private static string GetString(JsonElement el, params string[] names)
    {
        foreach (var name in names)
            if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString() ?? "";
        return "";
    }

    private static readonly string[] DateFormats =
    {
        "yyyyMMddTHHmmss",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-ddTHH:mm:ssZ",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-dd",
        "yyyyMMdd",
        "o",
    };

    private static DateTime? ParsePublishedAt(JsonElement article)
    {
        var raw = GetString(article, "published_date", "seendate", "date", "publishedAt");
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (DateTime.TryParseExact(raw, DateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);

        // Last resort: generic invariant parse, still required to be UTC-anchored.
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var fallback))
            return DateTime.SpecifyKind(fallback, DateTimeKind.Utc);

        return null;
    }

    // Stable across calls so cached rows and repeated snapshots agree.
    public static string DeterministicId(string url)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url.Trim().ToLowerInvariant()));
        return "gdelt-" + Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }
}
