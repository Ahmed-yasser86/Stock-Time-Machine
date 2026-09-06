namespace StockTimeMachine;

public interface IRelevanceService
{
    // Classify cached articles for one investigation: cache-first per
    // (article, symbol), model-batched (≤25) with the investigation context.
    // Unknown (not classified / invalid model output / AI off) is a normal
    // outcome — callers fall back to deterministic heuristics, never to
    // relevant-by-default.
    Task<IReadOnlyDictionary<string, ArticleRelevance>> ClassifyAsync(
        string symbol, DateOnly asOfDate, string? companyName, string? sector,
        IReadOnlyList<NewsArticle> articles, CancellationToken ct = default);
}
