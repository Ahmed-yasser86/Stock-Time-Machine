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
    private readonly string _apiKey;
    private readonly string _baseUrl;

    public GdeltCloudNewsProvider(HttpClient http, ILogger<GdeltCloudNewsProvider> logger, IConfiguration config)
    {
        _http = http;
        _logger = logger;
        _apiKey = config["Gdelt:ApiKey"] ?? "";
        _baseUrl = (config["Gdelt:CloudBaseUrl"] ?? "https://gdeltcloud.com").TrimEnd('/');
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    public Task<IReadOnlyList<NewsArticle>> SearchAsync(string symbol, DateOnly cutoffDate, CancellationToken ct = default) =>
        SearchAsync(symbol, companyName: null, cutoffDate, ct);

    public async Task<IReadOnlyList<NewsArticle>> SearchAsync(string symbol, string? companyName, DateOnly cutoffDate, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _logger.LogDebug("GDELT Cloud API key not configured; Cloud unavailable for {Symbol}", symbol);
            return Array.Empty<NewsArticle>();
        }

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
            throw new RateLimitExceededException("GDELT Cloud rate limit exceeded.");
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

    private async Task<IReadOnlyList<NewsArticle>> SearchStories(string symbol, string entityId, DateOnly cutoffDate, CancellationToken ct)
    {
        var normalized = symbol.Trim().ToUpperInvariant();
        var cutoff = TemporalBoundary.GetCutoffUtc(cutoffDate);
        var start = cutoffDate.AddDays(-7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = cutoffDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{_baseUrl}/api/v2/stories?entity={Uri.EscapeDataString(entityId)}&date_start={start}&date_end={end}&sort=recent&limit=20");
        request.Headers.Add("Authorization", "Bearer " + _apiKey);

        _logger.LogInformation("Fetching GDELT Cloud stories for {Symbol} between {Start} and {End}", normalized, start, end);

        using var response = await _http.SendAsync(request, ct);
        if ((int)response.StatusCode == 429)
            throw new RateLimitExceededException("GDELT Cloud rate limit exceeded.");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var stories) || stories.ValueKind != JsonValueKind.Array)
            return Array.Empty<NewsArticle>();

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

                if (results.Count >= 20)
                    return results;
            }
        }

        _logger.LogInformation("Found {Count} GDELT Cloud articles for {Symbol}", results.Count, normalized);
        return results;
    }

    public static string DeterministicId(string url)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url.Trim().ToLowerInvariant()));
        return "gdc-" + Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }
}
