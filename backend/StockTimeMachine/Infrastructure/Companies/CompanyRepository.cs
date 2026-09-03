using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

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
        return await _db.Companies.FirstOrDefaultAsync(c => c.Symbol == symbol.ToUpperInvariant(), ct);
    }

    public async Task<Company?> GetByCik(string cik, CancellationToken ct = default)
    {
        var normalized = NormalizeCik(cik);
        return await _db.Companies.FirstOrDefaultAsync(c => c.Cik == cik || c.Cik == normalized, ct);
    }

    public async Task<IReadOnlyList<Company>> Search(string query, CancellationToken ct = default)
    {
        var q = query.Trim().ToUpperInvariant();
        return await _db.Companies
            .Where(c => c.Symbol.ToUpper().Contains(q) || c.Name.ToUpper().Contains(q))
            .OrderBy(c => c.Symbol)
            .ToListAsync(ct);
    }

    public async Task<Company> Add(Company company, CancellationToken ct = default)
    {
        company.Symbol = company.Symbol.ToUpperInvariant();
        if (!string.IsNullOrEmpty(company.Cik))
            company.Cik = NormalizeCik(company.Cik);

        var existing = await _db.Companies.FindAsync(new object[] { company.Symbol }, ct);
        if (existing is not null)
            throw new InvalidOperationException($"Company {company.Symbol} already exists.");

        _db.Companies.Add(company);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Added company {Symbol} ({Name})", company.Symbol, company.Name);
        return company;
    }

    private static string NormalizeCik(string cik) =>
        new string(cik.Where(char.IsDigit).ToArray()).PadLeft(10, '0');
}
