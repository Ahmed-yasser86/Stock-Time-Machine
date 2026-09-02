using StockTimeMachine.Entities;

namespace StockTimeMachine.Providers;

public interface ISecEdgarProvider
{
    Task<IReadOnlyList<SecFiling>> GetCompanyFilings(string cik, DateOnly? asOfDate = null, CancellationToken ct = default);
    Task<Company?> GetCompanyProfile(string cik, CancellationToken ct = default);
}

public interface IAlphaVantageProvider
{
    Task<IReadOnlyList<PricePoint>> GetDailyPrices(string symbol, DateOnly? asOfDate = null, int days = 365, CancellationToken ct = default);
}
