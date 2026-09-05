
namespace StockTimeMachine;

public interface IHistoricalDataRepository
{
    Task StoreFilings(string companySymbol, IEnumerable<SecFiling> filings, CancellationToken ct = default);
    Task StorePrices(string companySymbol, IEnumerable<PricePoint> prices, CancellationToken ct = default);
    // News cache (best-effort sources: GDELT, Alpha Vantage NEWS_SENTIMENT).
    // Reads are always cutoff-filtered; the cache never leaks future items.
    Task StoreNews(string companySymbol, IEnumerable<NewsArticle> articles, CancellationToken ct = default);
    Task<IReadOnlyList<NewsArticle>> GetNewsAsOf(string companySymbol, DateOnly asOfDate, CancellationToken ct = default);
    // Source-filtered read: the filter applies INSIDE the query before Take,
    // so a burst of rows from one source can never push another source's rows
    // out of the window. Null/empty source keeps the legacy unfiltered read.
    Task<IReadOnlyList<NewsArticle>> GetNewsAsOf(string companySymbol, DateOnly asOfDate, string? newsSource, CancellationToken ct = default);
    // Embedding vector cache (read-through): repeat investigations reuse
    // vectors instead of re-spending provider quota. Keyed by article + model.
    Task<ArticleEmbedding?> GetEmbedding(string articleId, string model, CancellationToken ct = default);
    Task StoreEmbedding(ArticleEmbedding embedding, CancellationToken ct = default);
    Task<IReadOnlyList<SecFiling>> GetFilingsAsOf(string companySymbol, DateOnly asOfDate, CancellationToken ct = default);
    Task<IReadOnlyList<PricePoint>> GetPricesAsOf(string companySymbol, DateOnly asOfDate, int days = 30, CancellationToken ct = default);
    Task<IReadOnlyList<PricePoint>> GetPriceRange(string companySymbol, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<PricePoint>> GetPricesAfter(string companySymbol, DateOnly fromDate, int days = 30, CancellationToken ct = default);
    // Post-cutoff regulatory evidence for the "What Happened Afterwards" reveal.
    // Strictly after the cutoff of fromDate, up to days later.
    Task<IReadOnlyList<SecFiling>> GetFilingsAfter(string companySymbol, DateOnly fromDate, int days = 30, CancellationToken ct = default);
}
