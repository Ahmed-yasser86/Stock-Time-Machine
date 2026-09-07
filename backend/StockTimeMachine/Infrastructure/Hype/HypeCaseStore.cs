using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

public class HypeCaseStore : IHypeCaseStore
{
    private readonly StockTimeMachineDbContext _db;
    private readonly ILogger<HypeCaseStore> _logger;

    public HypeCaseStore(StockTimeMachineDbContext db, ILogger<HypeCaseStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<HypeCase> SaveAsync(HypeCase hypeCase, CancellationToken ct = default)
    {
        var existing = await _db.HypeCases.FindAsync(new object[] { hypeCase.Id }, ct);
        if (existing is null)
        {
            if (hypeCase.CreatedAtUtc == default)
                hypeCase.CreatedAtUtc = DateTime.UtcNow;
            _db.HypeCases.Add(hypeCase);
        }
        else
        {
            existing.DecisionDate = hypeCase.DecisionDate;
            existing.NewsSource = hypeCase.NewsSource;
            existing.Score = hypeCase.Score;
            existing.DailyReturnPct = hypeCase.DailyReturnPct;
            existing.FlagsCsv = hypeCase.FlagsCsv;
            existing.SentimentDirection = hypeCase.SentimentDirection;
            existing.Completeness = hypeCase.Completeness;
            existing.CaseJson = hypeCase.CaseJson;
        }
        await _db.SaveChangesAsync(ct);
        _logger.LogDebug("Hype case {Id} registered ({Completeness})", hypeCase.Id, hypeCase.Completeness);
        return hypeCase;
    }

    public async Task<HypeCase?> GetAsync(string symbol, DateOnly peakDate, CancellationToken ct = default)
    {
        var id = symbol.Trim().ToUpperInvariant() + ":" + peakDate.ToString("yyyy-MM-dd");
        return await _db.HypeCases.FirstOrDefaultAsync(h => h.Id == id, ct);
    }

    public async Task<IReadOnlyList<HypeCase>> ListBySymbolAsync(string symbol, int take = 20, CancellationToken ct = default)
    {
        var normalized = symbol.Trim().ToUpperInvariant();
        return await _db.HypeCases
            .Where(h => h.CompanySymbol == normalized)
            .OrderByDescending(h => h.PeakDate)
            .Take(Math.Max(1, take))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<HypeCase>> ListRecentAsync(int take = 50, CancellationToken ct = default) =>
        await _db.HypeCases
            .OrderByDescending(h => h.CreatedAtUtc)
            .Take(Math.Max(1, take))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<HypeCase>> ListAllAsync(CancellationToken ct = default) =>
        await _db.HypeCases
            .OrderBy(h => h.CompanySymbol)
            .ThenBy(h => h.PeakDate)
            .ToListAsync(ct);

    public async Task<int> CountAsync(CancellationToken ct = default) =>
        await _db.HypeCases.CountAsync(ct);
}
