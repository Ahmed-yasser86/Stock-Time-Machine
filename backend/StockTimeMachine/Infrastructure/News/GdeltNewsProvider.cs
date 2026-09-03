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
    private readonly string _baseUrl;
    // Optional GDELT Cloud credential. Server-side only: read from configuration
    // (environment variable Gdelt__ApiKey), never logged, never sent to browsers.
    // When absent, the keyless GDELT Project API is used.
    private readonly string _cloudApiKey;

    public GdeltNewsProvider(HttpClient http, ILogger<GdeltNewsProvider> logger, IConfiguration config)
    {
        _http = http;
        _logger = logger;
        _baseUrl = (config["Gdelt:BaseUrl"] ?? "https://api.gdeltproject.org/api/v2").TrimEnd('/');
        _cloudApiKey = config["Gdelt:ApiKey"] ?? "";
    }

    public async Task<IReadOnlyList<NewsArticle>> SearchAsync(string symbol, DateOnly cutoffDate, CancellationToken ct = default)
    {
        var cutoff = TemporalBoundary.GetCutoffUtc(cutoffDate);
        var start = cutoffDate.AddDays(-7).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var end = cutoffDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var query = Uri.EscapeDataString(symbol);

        // Quoted multi-word queries reduce false positives (e.g. ticker "V").
        var url = $"{_baseUrl}/doc/search?query={query}&format=json&startdatetime={start}&enddatetime={end}&maxrows=20&sort=datedesc";
        if (!string.IsNullOrEmpty(_cloudApiKey))
            url += $"&key={Uri.EscapeDataString(_cloudApiKey)}";

        _logger.LogInformation("Fetching news from GDELT for {Symbol} between {Start} and {End}", symbol, start, end);

        try
        {
            using var response = await _http.GetAsync(url, ct);
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
