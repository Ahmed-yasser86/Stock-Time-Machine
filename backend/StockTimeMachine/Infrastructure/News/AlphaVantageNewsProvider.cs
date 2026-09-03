using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

public class AlphaVantageNewsProvider : INewsProvider
{
    private readonly HttpClient _http;
    private readonly ILogger<AlphaVantageNewsProvider> _logger;
    private readonly string _apiKey;
    private readonly string _baseUrl;

    public AlphaVantageNewsProvider(HttpClient http, ILogger<AlphaVantageNewsProvider> logger, IConfiguration config)
    {
        _http = http;
        _logger = logger;
        // Server-side only. Never logged, never returned in API responses.
        _apiKey = config["AlphaVantage:ApiKey"] ?? "";
        _baseUrl = config["AlphaVantage:BaseUrl"] ?? "https://www.alphavantage.co/query";
    }

    public async Task<IReadOnlyList<NewsArticle>> SearchAsync(string symbol, DateOnly cutoffDate, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogWarning("Alpha Vantage API key not configured; news unavailable for {Symbol}", symbol);
            return Array.Empty<NewsArticle>();
        }

        // Free tier: NEWS_SENTIMENT is one request; the DB cache in front of this
        // provider keeps it to one call per (symbol, week) at most.
        var cutoff = TemporalBoundary.GetCutoffUtc(cutoffDate);
        var from = cutoffDate.AddDays(-7).ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "T0000";
        var url = $"{_baseUrl}?function=NEWS_SENTIMENT&symbol={Uri.EscapeDataString(symbol)}&time_from={from}&limit=50&apikey={_apiKey}";
        _logger.LogInformation("Fetching news from Alpha Vantage for {Symbol}", symbol);

        try
        {
            using var resp = await _http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("feed", out var feed) || feed.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("Alpha Vantage returned no news feed for {Symbol}", symbol);
                return Array.Empty<NewsArticle>();
            }

            var normalizedSymbol = symbol.ToUpperInvariant();
            var results = new List<NewsArticle>();

            foreach (var item in feed.EnumerateArray())
            {
                if (!item.TryGetProperty("time_published", out var tp))
                    continue;
                if (!DateTime.TryParseExact(tp.GetString() ?? "", "yyyyMMddTHHmmss",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var published))
                    continue;

                published = DateTime.SpecifyKind(published, DateTimeKind.Utc);
                if (published > cutoff)
                    continue;

                var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var summary = item.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
                var articleUrl = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                var source = item.TryGetProperty("source", out var src) ? src.GetString() ?? "" : "";

                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(articleUrl))
                    continue;

                results.Add(new NewsArticle
                {
                    Id = DeterministicId(articleUrl),
                    Title = title,
                    Description = summary,
                    Source = string.IsNullOrEmpty(source) ? "Alpha Vantage" : $"Alpha Vantage via {source}",
                    PublishedAt = published,
                    Url = articleUrl,
                    CompanySymbol = normalizedSymbol
                });
            }

            _logger.LogInformation("Alpha Vantage news for {Symbol}: {Count} articles before cutoff {Cutoff}", symbol, results.Count, cutoffDate);
            return results;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException ex)
        {
            // Provider timeout degrades to empty; genuine cancellation propagates.
            _logger.LogWarning(ex, "Alpha Vantage news request timed out for {Symbol}; returning empty (best-effort)", symbol);
            return Array.Empty<NewsArticle>();
        }
        catch (Exception ex)
        {
            // Best-effort source: provider failures degrade to an honest empty
            // state in the snapshot, never a failed investigation.
            _logger.LogWarning(ex, "Alpha Vantage news fetch failed for {Symbol}; returning empty (best-effort)", symbol);
            return Array.Empty<NewsArticle>();
        }
    }

    // Stable across calls so cached rows and repeated snapshots agree.
    public static string DeterministicId(string url)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url.Trim().ToLowerInvariant()));
        return "avnews-" + Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }
}
