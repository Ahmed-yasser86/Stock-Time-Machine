
namespace StockTimeMachine;

public interface ISecEdgarProvider
{
    Task<IReadOnlyList<SecFiling>> GetCompanyFilings(string cik, DateOnly? asOfDate = null, CancellationToken ct = default);
    Task<Company?> GetCompanyProfile(string cik, CancellationToken ct = default);
}
