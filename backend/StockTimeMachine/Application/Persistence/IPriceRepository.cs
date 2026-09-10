
namespace StockTimeMachine;

// Focused price-bar persistence port (ISP split from IHistoricalDataRepository).
// Consumers needing only prices depend on this instead of the full composite.
public interface IPriceRepository
{
    Task StorePrices(string companySymbol, IEnumerable<PricePoint> prices, CancellationToken ct = default);
    Task<IReadOnlyList<PricePoint>> GetPricesAsOf(string companySymbol, DateOnly asOfDate, int days = 30, CancellationToken ct = default);
    Task<IReadOnlyList<PricePoint>> GetPriceRange(string companySymbol, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<PricePoint>> GetPricesAfter(string companySymbol, DateOnly fromDate, int days = 30, CancellationToken ct = default);
}
