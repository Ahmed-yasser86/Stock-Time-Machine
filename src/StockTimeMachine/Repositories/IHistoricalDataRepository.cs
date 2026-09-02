using StockTimeMachine.Entities;

namespace StockTimeMachine.Repositories;

public interface IHistoricalDataRepository
{
    Task StoreFilings(string companySymbol, IEnumerable<SecFiling> filings, CancellationToken ct = default);
    Task StorePrices(string companySymbol, IEnumerable<PricePoint> prices, CancellationToken ct = default);
    Task<IReadOnlyList<SecFiling>> GetFilingsAsOf(string companySymbol, DateOnly asOfDate, CancellationToken ct = default);
    Task<IReadOnlyList<PricePoint>> GetPricesAsOf(string companySymbol, DateOnly asOfDate, int days = 30, CancellationToken ct = default);
    Task<IReadOnlyList<PricePoint>> GetPriceRange(string companySymbol, DateOnly from, DateOnly to, CancellationToken ct = default);
}
