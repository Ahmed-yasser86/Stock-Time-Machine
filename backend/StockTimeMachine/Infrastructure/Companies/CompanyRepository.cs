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
        // Provider-supplied strings are unbounded (SEC EDGAR returns full
        // exchange names like "NEW YORK STOCK EXCHANGE"); the columns are
        // not. Normalize + clamp here — the single DB write path — so one
        // long field can never void the whole save (and poison the tracked
        // context for every later save in the request).
        var rawExchange = company.Exchange;
        company.Exchange = NormalizeExchange(company.Exchange);
        if (!string.Equals(rawExchange?.Trim(), company.Exchange, StringComparison.OrdinalIgnoreCase))
            _logger.LogInformation("Normalized exchange '{Raw}' to '{Code}' for {Symbol}",
                rawExchange, company.Exchange, company.Symbol);
        company.Name = Clamp(company.Name, 200);
        company.Sector = Clamp(company.Sector, 100);
        company.Industry = Clamp(company.Industry, 200);

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

    // Exchange codes are display-only: map known full names to codes, then
    // hard-clamp to the nvarchar(20) column. Prefix matching (not exact)
    // because providers append venue variants ("New York Stock Exchange
    // Arca"). Unknown values survive truncated rather than killing the save.
    private static string NormalizeExchange(string? exchange)
    {
        var upper = (exchange ?? "").Trim().ToUpperInvariant();
        string code;
        if (upper.StartsWith("NEW YORK STOCK EXCHANGE", StringComparison.Ordinal))
            code = upper.Contains("ARCA", StringComparison.Ordinal) ? "NYSE ARCA" : "NYSE";
        else if (upper.StartsWith("NASDAQ", StringComparison.Ordinal))
            code = "NASDAQ";
        else if (upper is "NYSE ARCA" or "NYSE AMERICAN")
            code = upper;
        else if (upper is "CBOE BZX EXCHANGE" or "CBOE EDGX EXCHANGE" or "CBOE")
            code = "CBOE";
        else if (upper.StartsWith("OTC", StringComparison.Ordinal))
            code = "OTC";
        else
            code = upper;
        return code.Length > 20 ? code.Substring(0, 20) : code;
    }

    private static string Clamp(string? value, int max) =>
        string.IsNullOrEmpty(value) ? "" : value.Length > max ? value.Substring(0, max) : value;
}
