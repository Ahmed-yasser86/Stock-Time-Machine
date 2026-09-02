using StockTimeMachine.Entities;

namespace StockTimeMachine.Repositories;

public interface ICompanyRepository
{
    Task<Company?> GetBySymbol(string symbol, CancellationToken ct = default);
    Task<Company?> GetByCik(string cik, CancellationToken ct = default);
    Task<IReadOnlyList<Company>> Search(string query, CancellationToken ct = default);
    Task<Company> Add(Company company, CancellationToken ct = default);
}
