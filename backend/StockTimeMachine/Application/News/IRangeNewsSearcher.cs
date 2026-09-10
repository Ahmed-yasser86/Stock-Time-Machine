namespace StockTimeMachine;

// Bounded single-range keyword retrieval over an explicit day window.
// Relevance expansion is the only consumer: deterministic finance keyword
// variations, one bounded call each, cutoff-filtered by the implementation.
// GdeltNewsProvider is currently the only transport offering keyword search;
// consumers depend on this port, never on the concrete provider, so a future
// transport can serve expansion without touching RelevanceService.
public interface IRangeNewsSearcher
{
    Task<IReadOnlyList<NewsArticle>> SearchRangeAsync(
        string query, string symbol, DateOnly fromDay, DateOnly toDay, DateOnly cutoffDate, int maxRows, CancellationToken ct = default);
}
