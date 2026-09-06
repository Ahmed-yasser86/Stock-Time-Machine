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

    // Relevance gate: the single source of truth for "may this article enter
    // embeddings / threads / evidence". Passes RELEVANT + USER_APPROVED only.
    // When nothing is classified (AI off/failed), everything passes through —
    // unknown ≠ irrelevant, and the product must keep working.
    bool PassesGate(ArticleRelevance? row);

    // Bounded retrieval expansion (GDELT Project keyword queries only):
    // deterministic finance variations, single-range calls, cutoff-filtered,
    // classified before return. At most MaxVariations queries per call.
    Task<ExpansionResult> ExpandAsync(
        string symbol, DateOnly asOfDate, string? companyName, CancellationToken ct = default);

    Task<IReadOnlyList<ArticleRelevance>> CandidatesAsync(
        string symbol, DateOnly asOfDate, CancellationToken ct = default);
    Task<bool> ApproveAsync(string symbol, string articleId, CancellationToken ct = default);
    Task<bool> RejectAsync(string symbol, string articleId, CancellationToken ct = default);
}

public class ExpansionResult
{
    public int QueriesRun { get; set; }
    public int NewCandidates { get; set; }
    public int NewRelevant { get; set; }
    public List<string> FailedQueries { get; set; } = new();
}
