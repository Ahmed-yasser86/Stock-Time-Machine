using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

// GDELT Cloud news (authenticated REST): entity-anchored stories with article
// evidence. Procedure learned from the Cloud MCP + REST reference:
//   1. GET /api/v2/search?q={name}&type=organization → terminal entity id.
//      The candidate is accepted only when identifiers.ticker contains the
//      requested symbol — never trust a bare name match (e.g. ticker "V").
//   2. GET /api/v2/stories?entity={id}&date_start&date_end&sort=recent → stories,
//      flattened via top_articles[] (title/url/domain) with story_date + story
//      URL for citation.
// Honesty limits (documented, enforced):
// - Cloud corpus starts 2026-03-08 (meta.coverage.start). Older windows return
//   empty — never an error, never fabricated.
// - Story rows are settled/current representations; story_date bounds the
//   narrative, and items after the cutoff are still excluded client-side.
// - The key lives server-side (Gdelt:ApiKey); it is never logged or returned.
public class GdeltCloudNewsProvider : INewsProvider
{
    private readonly HttpClient _http;
    private readonly ILogger<GdeltCloudNewsProvider> _logger;
    private readonly IConfiguration _config;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    // Request pacing + throttled retries (see GdeltResilience): a throttled
    // provider must yield, never hammer. Singleton: safe for instance fields.
    private readonly int _minIntervalMs;
    private readonly SemaphoreSlim _paceGate = new(1, 1);
    private DateTime _lastCallUtc = DateTime.MinValue;

    public GdeltCloudNewsProvider(HttpClient http, ILogger<GdeltCloudNewsProvider> logger, IConfiguration config)
    {
        _http = http;
        _logger = logger;
        _config = config;
        _apiKey = config["Gdelt:ApiKey"] ?? "";
        _baseUrl = (config["Gdelt:CloudBaseUrl"] ?? "https://gdeltcloud.com").TrimEnd('/');
        _minIntervalMs = int.TryParse(config["Gdelt:MinRequestIntervalMs"], out var ms) && ms >= 0 ? ms : 3000;
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    public Task<IReadOnlyList<NewsArticle>> SearchAsync(string symbol, DateOnly cutoffDate, CancellationToken ct = default) =>
        SearchAsync(symbol, companyName: null, cutoffDate, ct);

    public Task<IReadOnlyList<NewsArticle>> SearchAsync(string symbol, string? companyName, DateOnly cutoffDate, CancellationToken ct = default) =>
        GdeltResilience.ExecuteAsync(
            ct2 => SearchCoreAsync(symbol, companyName, cutoffDate, ct2),
            _logger, $"GDELT Cloud {symbol}", _config, ct);

    private async Task PaceAsync(CancellationToken ct)
    {
        TimeSpan wait;
        await _paceGate.WaitAsync(ct);
        try
        {
            var now = DateTime.UtcNow;
            var next = _lastCallUtc + TimeSpan.FromMilliseconds(_minIntervalMs);
            wait = next > now ? next - now : TimeSpan.Zero;
            _lastCallUtc = now + wait;
        }
        finally
        {
            _paceGate.Release();
        }
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, ct);
    }

    private async Task<IReadOnlyList<NewsArticle>> SearchCoreAsync(string symbol, string? companyName, DateOnly cutoffDate, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            _logger.LogDebug("GDELT Cloud API key not configured; Cloud unavailable for {Symbol}", symbol);
            return Array.Empty<NewsArticle>();
        }

        await PaceAsync(ct);
        try
        {
            // Resolve by company name first (the documented resolver input),
            // falling back to the raw symbol. Either way the ticker-identifiers
            // check below must accept the candidate.
            string? entityId = null;
            foreach (var query in ResolutionQueries(symbol, companyName))
            {
                entityId = await ResolveEntityId(query, symbol, ct);
                if (entityId is not null)
                    break;
            }
            if (entityId is null)
            {
                _logger.LogInformation("No GDELT Cloud entity with ticker {Symbol}; honest empty", symbol);
                return Array.Empty<NewsArticle>();
            }

            return await SearchStories(symbol, entityId, cutoffDate, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "GDELT Cloud request timed out for {Symbol}", symbol);
            return Array.Empty<NewsArticle>();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "GDELT Cloud request failed for {Symbol}", symbol);
            return Array.Empty<NewsArticle>();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "GDELT Cloud JSON parse failed for {Symbol}", symbol);
            return Array.Empty<NewsArticle>();
        }
    }

    private static IEnumerable<string> ResolutionQueries(string symbol, string? companyName)
    {
        if (!string.IsNullOrWhiteSpace(companyName))
            yield return companyName.Trim();
        var normalized = symbol.Trim();
        if (!string.IsNullOrWhiteSpace(companyName) &&
            string.Equals(companyName.Trim(), normalized, StringComparison.OrdinalIgnoreCase))
            yield break;
        yield return normalized;
    }

    private async Task<string?> ResolveEntityId(string query, string symbol, CancellationToken ct)
    {
        var normalized = symbol.Trim().ToUpperInvariant();
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{_baseUrl}/api/v2/search?q={Uri.EscapeDataString(query)}&type=organization&limit=10");
        request.Headers.Add("Authorization", "Bearer " + _apiKey);

        using var response = await _http.SendAsync(request, ct);
        if ((int)response.StatusCode == 429)
            throw new RateLimitExceededException("GDELT Cloud rate limit exceeded.",
                RateLimitHeaders.ParseRetryAfter(response.Headers));
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var candidate in data.EnumerateArray())
        {
            if (!candidate.TryGetProperty("entity_id", out var idEl))
                continue;
            var id = idEl.GetString() ?? "";
            if (string.IsNullOrEmpty(id))
                continue;

            // Ticker match inside identifiers is the disambiguator: accept only
            // the entity that actually claims this symbol.
            if (candidate.TryGetProperty("identifiers", out var ids) &&
                ids.TryGetProperty("ticker", out var tickers) &&
                tickers.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in tickers.EnumerateArray())
                {
                    if (string.Equals(t.GetString(), normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("Resolved {Symbol} to GDELT Cloud entity {Entity}", normalized, id);
                        return id;
                    }
                }
            }
        }

        return null;
    }

    // Interval-complete retrieval: the trailing window is traversed ONE DAY AT
    // A TIME (deterministic, contiguous, gapless) instead of one sort=recent
    // query whose page limit lets the busiest days starve the rest. Per-day
    // limit 100; days merged, deduped by article id, newest-first.
    // A 429 aborts the remaining days (rethrow — hammering a throttle helps no
    // one; the outer resilience wrapper backs off and retries the whole
    // fetch). Any other per-day failure degrades loudly but never voids the
    // days that succeeded.
    private const int WindowDaysBack = 7;
    private const int DayLimit = 100;

    private async Task<IReadOnlyList<NewsArticle>> SearchStories(string symbol, string entityId, DateOnly cutoffDate, CancellationToken ct)
    {
        var normalized = symbol.Trim().ToUpperInvariant();
        var cutoff = TemporalBoundary.GetCutoffUtc(cutoffDate);
        var days = Enumerable.Range(0, WindowDaysBack + 1)
            .Select(i => cutoffDate.AddDays(-WindowDaysBack + i))
            .ToList();
        var limiter = RateLimiterRegistry.Get("gdelt", _config);

        var results = new List<NewsArticle>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var perDay = new List<(DateOnly Day, int Count)>();
        var failedDays = new List<DateOnly>();
        foreach (var day in days)
        {
            await limiter.AcquireAsync(0, ct);
            try
            {
                int added = 0;
                foreach (var article in await FetchDay(symbol, entityId, day, cutoff, ct))
                {
                    if (seen.Add(article.Id))
                    {
                        results.Add(article);
                        added++;
                    }
                }
                perDay.Add((day, added));
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
                _logger.LogWarning(ex, "GDELT Cloud day {Day} failed for {Symbol}; keeping other days", day, normalized);
            }
        }

        // Coverage validation: requested vs traversed vs represented. A
        // zero-row day is information (searched, nothing there); a failed day
        // is a warning. Never conflate the two.
        var represented = results
            .Select(n => DateOnly.FromDateTime(n.PublishedAt)).Distinct().OrderBy(d => d).ToList();
        var missing = days.Where(d => !represented.Contains(d)).ToList();
        _logger.LogInformation(
            "GDELT Cloud coverage for {Symbol}: requested [{Start}..{End}], traversed {Traversed}/{Total} days, {Articles} articles, represented [{Represented}], zero-row [{Missing}], failed [{Failed}]",
            normalized, days.First(), days.Last(), perDay.Count, days.Count, results.Count,
            string.Join(",", represented), string.Join(",", missing),
            string.Join(",", failedDays));

        return results
            .OrderByDescending(n => n.PublishedAt)
            .ThenBy(n => n.Id)
            .ToList();
    }

    private async Task<List<NewsArticle>> FetchDay(
        string symbol, string entityId, DateOnly day, DateTime cutoff, CancellationToken ct)
    {
        var normalized = symbol.Trim().ToUpperInvariant();
        var stamp = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{_baseUrl}/api/v2/stories?entity={Uri.EscapeDataString(entityId)}&date_start={stamp}&date_end={stamp}&sort=recent&limit={DayLimit}");
        request.Headers.Add("Authorization", "Bearer " + _apiKey);

        using var response = await _http.SendAsync(request, ct);
        if ((int)response.StatusCode == 429)
            throw new RateLimitExceededException("GDELT Cloud rate limit exceeded.",
                RateLimitHeaders.ParseRetryAfter(response.Headers));
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var stories) || stories.ValueKind != JsonValueKind.Array)
            return new List<NewsArticle>();

        var results = new List<NewsArticle>();
        foreach (var story in stories.EnumerateArray())
        {
            // story_date bounds the narrative; items after the cutoff are excluded.
            var storyDate = story.TryGetProperty("story_date", out var sd) ? sd.GetString() ?? "" : "";
            if (!DateOnly.TryParseExact(storyDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var pubDay))
                continue;
            var publishedAt = new DateTime(pubDay.Year, pubDay.Month, pubDay.Day, 0, 0, 0, DateTimeKind.Utc);
            if (publishedAt > cutoff)
                continue;

            if (!story.TryGetProperty("top_articles", out var articles) || articles.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var article in articles.EnumerateArray())
            {
                var title = article.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var urlVal = article.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                var domain = article.TryGetProperty("domain", out var dm) ? dm.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(urlVal))
                    continue;

                results.Add(new NewsArticle
                {
                    Id = DeterministicId(urlVal),
                    Title = title,
                    Description = "",
                    Source = string.IsNullOrEmpty(domain) ? "GDELT Cloud" : $"GDELT Cloud via {domain}",
                    PublishedAt = publishedAt,
                    Url = urlVal,
                    CompanySymbol = normalized
                });

                if (results.Count >= DayLimit)
                    return results;
            }
        }

        return results;
    }

    public static string DeterministicId(string url)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url.Trim().ToLowerInvariant()));
        return "gdc-" + Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }
}
