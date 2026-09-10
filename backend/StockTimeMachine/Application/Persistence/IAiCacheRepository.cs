
namespace StockTimeMachine;

// Focused AI-cache persistence port (ISP split from IHistoricalDataRepository).
// Covers embedding vectors, relevance verdicts (+ review queue), and sentiment
// scores. Repeat investigations reuse cached rows instead of re-spending
// provider quota.
public interface IAiCacheRepository
{
    // Embedding vector cache (read-through). Keyed by article + model.
    Task<ArticleEmbedding?> GetEmbedding(string articleId, string model, CancellationToken ct = default);
    Task StoreEmbedding(ArticleEmbedding embedding, CancellationToken ct = default);
    Task<ArticleRelevance?> GetRelevance(string articleId, string symbol, CancellationToken ct = default);
    Task StoreRelevances(IEnumerable<ArticleRelevance> rows, CancellationToken ct = default);
    // Review queue: ALL cutoff-eligible uncertain verdicts, highest
    // confidence first. Deliberately uncapped (reason: an approval queue
    // must not silently hide workload behind a take).
    Task<IReadOnlyList<ArticleRelevance>> GetUncertain(string symbol, DateOnly asOfDate, CancellationToken ct = default);
    Task<bool> SetRelevanceDecision(string articleId, string symbol, string decision, string source, CancellationToken ct = default);
    Task<ArticleSentiment?> GetSentiment(string articleId, string model, CancellationToken ct = default);
    Task StoreSentiment(ArticleSentiment row, CancellationToken ct = default);
}
