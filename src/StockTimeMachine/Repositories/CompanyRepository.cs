using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockTimeMachine.Entities;

namespace StockTimeMachine.Repositories;

public class CompanyRepository : ICompanyRepository
{
    private readonly StockTimeMachineDbContext _db;
    private readonly ILogger<CompanyRepository> _logger;

    public CompanyRepository(StockTimeMachineDbContext db, ILogger<CompanyRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Company?> GetBySymbol(string symbol, CancellationToken ct = default)
    {
        return await _db.Companies.FirstOrDefaultAsync(c => c.Symbol == symbol.ToUpper(), ct);
    }

    public async Task<Company?> GetByCik(string cik, CancellationToken ct = default)
    {
        return await _db.Companies.FirstOrDefaultAsync(c => c.Cik == cik, ct);
    }

    public async Task<IReadOnlyList<Company>> Search(string query, CancellationToken ct = default)
    {
        var q = query.ToUpper();
        return await _db.Companies
            .Where(c => c.Symbol.Contains(q) || c.Name.ToUpper().Contains(q))
            .OrderBy(c => c.Symbol)
            .ToListAsync(ct);
    }

    public async Task<Company> Add(Company company, CancellationToken ct = default)
    {
        company.Symbol = company.Symbol.ToUpper();
        _db.Companies.Add(company);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Added company {Symbol} ({Name})", company.Symbol, company.Name);
        return company;
    }
}
