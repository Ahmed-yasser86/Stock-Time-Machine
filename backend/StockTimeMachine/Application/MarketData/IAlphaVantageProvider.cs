
namespace StockTimeMachine;

public interface IAlphaVantageProvider
{
    Task<IReadOnlyList<PricePoint>> GetDailyPrices(string symbol, DateOnly? asOfDate = null, int days = 365, CancellationToken ct = default);
}
