using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

// Local FinBERT analyzer over the Python sidecar (scripts/nlp/server.py).
// Contract: cached-first, cutoff-enforced, hash-bound, fail-soft. Jina bodies
// are NEVER input (retrieval-time text, not evidence-time text) — only stored
// title + description as captured by providers.
public class FinBertSentimentAnalyzer : IFinancialSentimentAnalyzer
{
    private const int BatchSize = 32;

    private readonly HttpClient _http;
    private readonly IAiCacheRepository _aiCache;
    private readonly ILogger<FinBertSentimentAnalyzer> _logger;
    private readonly string _endpoint;
    private readonly bool _enabled;

    public bool IsEnabled => _enabled;
    public string ModelId => "ProsusAI/finbert";

    public FinBertSentimentAnalyzer(
        HttpClient http,
        IAiCacheRepository aiCache,
        ILogger<FinBertSentimentAnalyzer> logger,
        IConfiguration config)
    {
        _http = http;
        _aiCache = aiCache;
        _logger = logger;
        _endpoint = (config["Nlp:Endpoint"] ?? "http://127.0.0.1:5252").TrimEnd('/');
        _enabled = (config["Nlp:Enabled"] ?? "true").Equals("true", StringComparison.OrdinalIgnoreCase);
        _http.Timeout = TimeSpan.FromSeconds(120);
    }

    public async Task<IReadOnlyList<ScoredArticle>> EnsureScoredAsync(
        IReadOnlyList<NewsArticle> articles, DateOnly cutoff, CancellationToken ct = default)
    {
        var scored = new List<ScoredArticle>();
        if (!_enabled)
            return scored;
        var cutoffUtc = TemporalBoundary.GetCutoffUtc(cutoff);
        var eligible = articles
            .Where(a => a.PublishedAt <= cutoffUtc && !string.IsNullOrWhiteSpace(a.Title + a.Description))
            .GroupBy(a => a.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();
        var missing = new List<(NewsArticle Article, string Hash)>();
        foreach (var article in eligible)
        {
            var hash = TextHash(article);
            ArticleSentiment? cached = null;
            try
            {
                cached = await _aiCache.GetSentiment(article.Id, ModelId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Sentiment cache read miss for {Article}", article.Id);
            }
            if (cached is not null && cached.TextHash == hash)
            {
                scored.Add(new ScoredArticle
                {
                    ArticleId = article.Id, Score = cached.Score, Confidence = cached.Confidence,
                    Model = cached.Model, Revision = "", FromCache = true,
                });
            }
            else
            {
                missing.Add((article, hash));
            }
        }
        for (int start = 0; start < missing.Count; start += BatchSize)
        {
            var batch = missing.Skip(start).Take(BatchSize).ToList();
            List<SidecarResult>? results = null;
            try
            {
                results = await AnalyzeBatchAsync(
                    batch.Select(b => b.Article.Title + "\n" + b.Article.Description).ToList(), ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FinBERT batch failed; keeping cached scores only");
                break;
            }
            if (results is null || results.Count != batch.Count)
            {
                _logger.LogWarning("FinBERT batch shape mismatch; keeping cached scores only");
                break;
            }
            for (int k = 0; k < batch.Count; k++)
            {
                var (article, hash) = batch[k];
                var r = results[k];
                var row = new ArticleSentiment
                {
                    ArticleId = article.Id, Model = ModelId, TextHash = hash,
                    Pos = r.Pos, Neu = r.Neu, Neg = r.Neg,
                    Score = r.Score, Confidence = r.Confidence, ScoredAt = DateTime.UtcNow,
                };
                try
                {
                    await _aiCache.StoreSentiment(row, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Sentiment store failed for {Article}", article.Id);
                }
                scored.Add(new ScoredArticle
                {
                    ArticleId = article.Id, Score = r.Score, Confidence = r.Confidence,
                    Model = ModelId, Revision = r.Revision, FromCache = false,
                });
            }
        }
        return scored;
    }

    private sealed class SidecarResult
    {
        public double Pos { get; set; }
        public double Neu { get; set; }
        public double Neg { get; set; }
        public double Score { get; set; }
        public double Confidence { get; set; }
        public string Revision { get; set; } = "";
    }

    private async Task<List<SidecarResult>?> AnalyzeBatchAsync(List<string> texts, CancellationToken ct)
    {
        using var resp = await _http.PostAsJsonAsync($"{_endpoint}/analyze", new { texts }, ct);
        resp.EnsureSuccessStatusCode();
        var raw = await resp.Content.ReadAsStringAsync(ct);
        _logger.LogDebug("FinBERT batch diagnostics: sent={Sent}, bytes={Bytes}, status={Status}",
            texts.Count, raw.Length, (int)resp.StatusCode);
        using var doc = JsonDocument.Parse(raw);
        if (!doc.RootElement.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array)
            return null;
        var revision = doc.RootElement.TryGetProperty("revision", out var rev)
            ? rev.GetString() ?? "" : "";
        var list = new List<SidecarResult>();
        foreach (var e in results.EnumerateArray())
        {
            double num(string name)
            {
                return e.TryGetProperty(name, out var v) && v.TryGetDouble(out var d) &&
                    !double.IsNaN(d) && !double.IsInfinity(d) ? d : 0;
            }
            list.Add(new SidecarResult
            {
                Pos = num("pos"), Neu = num("neu"), Neg = num("neg"),
                Score = Math.Clamp(num("score"), -1, 1),
                Confidence = Math.Clamp(num("confidence"), 0, 1),
                Revision = revision,
            });
        }
        return list;
    }

    public static string TextHash(NewsArticle article)
    {
        var bytes = Encoding.UTF8.GetBytes((article.Title ?? "") + "\n" + (article.Description ?? ""));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
