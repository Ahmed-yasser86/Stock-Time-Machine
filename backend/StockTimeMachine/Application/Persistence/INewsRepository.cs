
namespace StockTimeMachine;

// Focused news-cache persistence port (ISP split from IHistoricalDataRepository).
// Reads are always cutoff-filtered; the cache never leaks future items.
public interface INewsRepository
{
    // News cache (best-effort sources: GDELT, Alpha Vantage NEWS_SENTIMENT).
    Task StoreNews(string companySymbol, IEnumerable<NewsArticle> articles, CancellationToken ct = default);
    Task<IReadOnlyList<NewsArticle>> GetNewsAsOf(string companySymbol, DateOnly asOfDate, CancellationToken ct = default);
    // Source-filtered read: the filter applies INSIDE the query before Take,
    // so a burst of rows from one source can never push another source's rows
    // out of the window. Null/empty source keeps the legacy unfiltered read.
    Task<IReadOnlyList<NewsArticle>> GetNewsAsOf(string companySymbol, DateOnly asOfDate, string? newsSource, CancellationToken ct = default);
}
