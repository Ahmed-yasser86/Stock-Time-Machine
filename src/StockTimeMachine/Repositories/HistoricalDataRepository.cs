using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockTimeMachine.Entities;
using StockTimeMachine.RepositoryContracts;

namespace StockTimeMachine.Repositories;

public class HistoricalDataRepository : IHistoricalDataRepository
{
    private readonly StockTimeMachineDbContext _db;
    private readonly ILogger<HistoricalDataRepository> _logger;

    public HistoricalDataRepository(StockTimeMachineDbContext db, ILogger<HistoricalDataRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task StoreFilings(string companySymbol, IEnumerable<SecFiling> filings, CancellationToken ct = default)
    {
        var symbol = companySymbol.ToUpper();
        var existing = await _db.SecFilings
            .Where(f => f.CompanySymbol == symbol)
            .Select(f => f.AccessionNumber)
            .ToListAsync(ct);
        var existingSet = new HashSet<string>(existing);

        var newFilings = filings.Where(f => !existingSet.Contains(f.AccessionNumber)).ToList();
        if (newFilings.Count == 0) return;

        foreach (var filing in newFilings)
            filing.CompanySymbol = symbol;

        _db.SecFilings.AddRange(newFilings);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Stored {Count} new filings for {Symbol}", newFilings.Count, symbol);
    }

    public async Task StorePrices(string companySymbol, IEnumerable<PricePoint> prices, CancellationToken ct = default)
    {
        var symbol = companySymbol.ToUpper();
        var existingDates = await _db.PricePoints
            .Where(p => p.CompanySymbol == symbol)
            .Select(p => p.Date)
            .ToListAsync(ct);
        var existingSet = new HashSet<DateOnly>(existingDates);

        var newPrices = prices.Where(p => !existingSet.Contains(p.Date)).ToList();
        if (newPrices.Count == 0) return;

        foreach (var price in newPrices)
            price.CompanySymbol = symbol;

        _db.PricePoints.AddRange(newPrices);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Stored {Count} new prices for {Symbol}", newPrices.Count, symbol);
    }

    public async Task<IReadOnlyList<SecFiling>> GetFilingsAsOf(string companySymbol, DateOnly asOfDate, CancellationToken ct = default)
    {
        var cutoff = asOfDate.ToDateTime(TimeOnly.MinValue);
        return await _db.SecFilings
            .Where(f => f.CompanySymbol == companySymbol.ToUpper() && f.FiledAt.Date <= cutoff)
            .OrderByDescending(f => f.FiledAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PricePoint>> GetPricesAsOf(string companySymbol, DateOnly asOfDate, int days = 30, CancellationToken ct = default)
    {
        return await _db.PricePoints
            .Where(p => p.CompanySymbol == companySymbol.ToUpper() && p.Date <= asOfDate)
            .OrderByDescending(p => p.Date)
            .Take(days)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PricePoint>> GetPriceRange(string companySymbol, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        return await _db.PricePoints
            .Where(p => p.CompanySymbol == companySymbol.ToUpper() && p.Date >= from && p.Date <= to)
            .OrderBy(p => p.Date)
            .ToListAsync(ct);
    }
}
