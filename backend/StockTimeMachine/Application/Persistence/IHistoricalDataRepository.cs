
namespace StockTimeMachine;

public interface IHistoricalDataRepository
{
    Task StoreFilings(string companySymbol, IEnumerable<SecFiling> filings, CancellationToken ct = default);
    Task StorePrices(string companySymbol, IEnumerable<PricePoint> prices, CancellationToken ct = default);
    // News cache (best-effort sources: GDELT, Alpha Vantage NEWS_SENTIMENT).
    // Reads are always cutoff-filtered; the cache never leaks future items.
    Task StoreNews(string companySymbol, IEnumerable<NewsArticle> articles, CancellationToken ct = default);
    Task<IReadOnlyList<NewsArticle>> GetNewsAsOf(string companySymbol, DateOnly asOfDate, CancellationToken ct = default);
    Task<IReadOnlyList<SecFiling>> GetFilingsAsOf(string companySymbol, DateOnly asOfDate, CancellationToken ct = default);
    Task<IReadOnlyList<PricePoint>> GetPricesAsOf(string companySymbol, DateOnly asOfDate, int days = 30, CancellationToken ct = default);
    Task<IReadOnlyList<PricePoint>> GetPriceRange(string companySymbol, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<PricePoint>> GetPricesAfter(string companySymbol, DateOnly fromDate, int days = 30, CancellationToken ct = default);
    // Post-cutoff regulatory evidence for the "What Happened Afterwards" reveal.
    // Strictly after the cutoff of fromDate, up to days later.
    Task<IReadOnlyList<SecFiling>> GetFilingsAfter(string companySymbol, DateOnly fromDate, int days = 30, CancellationToken ct = default);
}
