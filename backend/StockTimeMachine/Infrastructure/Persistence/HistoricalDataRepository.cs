using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

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
        var symbol = companySymbol.ToUpperInvariant();
        var existing = await _db.SecFilings
            .Where(f => f.CompanySymbol == symbol)
            .Select(f => f.AccessionNumber)
            .ToListAsync(ct);
        var existingSet = new HashSet<string>(existing, StringComparer.Ordinal);

        var newFilings = filings
            .Where(f => !string.IsNullOrEmpty(f.AccessionNumber) && !existingSet.Contains(f.AccessionNumber))
            .Select(f => { f.CompanySymbol = symbol; return f; })
            .ToList();
        if (newFilings.Count == 0) return;

        _db.SecFilings.AddRange(newFilings);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Stored {Count} new filings for {Symbol}", newFilings.Count, symbol);
    }

    public async Task StorePrices(string companySymbol, IEnumerable<PricePoint> prices, CancellationToken ct = default)
    {
        var symbol = companySymbol.ToUpperInvariant();
        var existingDates = await _db.PricePoints
            .Where(p => p.CompanySymbol == symbol)
            .Select(p => p.Date)
            .ToListAsync(ct);
        var existingSet = new HashSet<DateOnly>(existingDates);

        var newPrices = prices
            .Where(p => !existingSet.Contains(p.Date))
            .Select(p => { p.CompanySymbol = symbol; return p; })
            .ToList();
        if (newPrices.Count == 0) return;

        _db.PricePoints.AddRange(newPrices);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Stored {Count} new prices for {Symbol}", newPrices.Count, symbol);
    }

    public async Task StoreNews(string companySymbol, IEnumerable<NewsArticle> articles, CancellationToken ct = default)
    {
        var symbol = companySymbol.ToUpperInvariant();
        // Article ids are content hashes: GLOBAL, not per-symbol. The same URL
        // fetched under two symbols (shared/syndicated coverage) must not PK-
        // collide and void the whole batch — dedupe globally, first fetch wins
        // ownership. Reads stay symbol-partitioned as before.
        var incoming = articles.Where(a => !string.IsNullOrEmpty(a.Id)).ToList();
        if (incoming.Count == 0) return;
        var wanted = new HashSet<string>(incoming.Select(a => a.Id), StringComparer.Ordinal);
        var existingSet = new HashSet<string>(
            await _db.NewsArticles.Where(n => wanted.Contains(n.Id)).Select(n => n.Id).ToListAsync(ct),
            StringComparer.Ordinal);

        var fresh = incoming
            .Where(a => !existingSet.Contains(a.Id))
            .Select(a => { a.CompanySymbol = symbol; return a; })
            .ToList();
        if (fresh.Count == 0) return;

        _db.NewsArticles.AddRange(fresh);
        int stored;
        try
        {
            await _db.SaveChangesAsync(ct);
            stored = fresh.Count;
        }
        catch (DbUpdateException ex)
        {
            // Concurrent jobs can still race the check above: fall back to
            // row-by-row insertion, skipping conflicts, so one duplicate never
            // voids the batch. Tracker is reset first (failed SaveChanges
            // leaves it unusable).
            _logger.LogWarning(ex, "News batch insert collided for {Symbol}; retrying row-by-row", symbol);
            foreach (var entry in _db.ChangeTracker.Entries().ToList())
                entry.State = EntityState.Detached;
            stored = 0;
            foreach (var article in fresh)
            {
                try
                {
                    _db.NewsArticles.Add(article);
                    await _db.SaveChangesAsync(ct);
                    stored++;
                }
                catch (DbUpdateException dup)
                {
                    _logger.LogDebug(dup, "Skipping duplicate article {Id}", article.Id);
                    foreach (var entry in _db.ChangeTracker.Entries().ToList())
                        entry.State = EntityState.Detached;
                }
            }
        }
        _logger.LogInformation("Stored {Count} news articles for {Symbol}", stored, symbol);
    }

    public async Task<IReadOnlyList<SecFiling>> GetFilingsAsOf(string companySymbol, DateOnly asOfDate, CancellationToken ct = default)
    {
        // SEC filings carry calendar dates (stored as midnight UTC), so eligibility
        // is by filing day, not instant: filingDate <= asOfDate. See TemporalBoundary.
        var dayAfter = TemporalBoundary.StartOfDayAfterUtc(asOfDate);
        return await _db.SecFilings
            .Where(f => f.CompanySymbol == companySymbol.ToUpperInvariant() && f.FiledAt < dayAfter)
            .OrderByDescending(f => f.FiledAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PricePoint>> GetPricesAsOf(string companySymbol, DateOnly asOfDate, int days = 30, CancellationToken ct = default)
    {
        // Daily bars are dated by trading day; a bar dated D is knowable at
        // end-of-day D, which is inside the cutoff for selected date D.
        // Equivalent to bar-timestamp <= TemporalBoundary.GetCutoffUtc(asOfDate).
        return await _db.PricePoints
            .Where(p => p.CompanySymbol == companySymbol.ToUpperInvariant() && p.Date <= asOfDate)
            .OrderByDescending(p => p.Date)
            .Take(days)
            .ToListAsync(ct);
    }

    // Reads are UNCAPPED by row count: every cached row at or before the
    // cutoff is returned newest-first, so no consumer can silently lose older
    // days to a Take window. Downstream stages apply their own disclosed,
    // cost-driven bounds (narratives embedding cap, evidence slices, UI pager)
    // instead of the read hiding rows from them.
    public async Task<IReadOnlyList<NewsArticle>> GetNewsAsOf(string companySymbol, DateOnly asOfDate, CancellationToken ct = default)
    {
        var cutoff = TemporalBoundary.GetCutoffUtc(asOfDate);
        return await _db.NewsArticles
            .Where(n => n.CompanySymbol == companySymbol.ToUpperInvariant() && n.PublishedAt <= cutoff)
            .OrderByDescending(n => n.PublishedAt)
            .ToListAsync(ct);
    }

    public async Task<ArticleEmbedding?> GetEmbedding(string articleId, string model, CancellationToken ct = default) =>
        await _db.ArticleEmbeddings
            .FirstOrDefaultAsync(x => x.ArticleId == articleId && x.Model == model, ct);

    public async Task StoreEmbedding(ArticleEmbedding embedding, CancellationToken ct = default)
    {
        var existing = await _db.ArticleEmbeddings.FindAsync(new object[] { embedding.ArticleId }, ct);
        if (existing is null)
            await _db.ArticleEmbeddings.AddAsync(embedding, ct);
        else
        {
            existing.Model = embedding.Model;
            existing.VectorJson = embedding.VectorJson;
            existing.CachedAt = embedding.CachedAt;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<NewsArticle>> GetNewsAsOf(string companySymbol, DateOnly asOfDate, string? newsSource, CancellationToken ct = default)
    {
        var cutoff = TemporalBoundary.GetCutoffUtc(asOfDate);
        var query = _db.NewsArticles
            .Where(n => n.CompanySymbol == companySymbol.ToUpperInvariant() && n.PublishedAt <= cutoff);
        // Same membership rule as every service-side IsFromSource: cached rows
        // carry their origin in Source, so per-source reads never mix providers.
        if (newsSource == NewsSources.AlphaVantage)
            query = query.Where(n => n.Source.Contains("Alpha Vantage"));
        else if (newsSource == NewsSources.MarketAux)
            query = query.Where(n => n.Source.Contains("MarketAux"));
        else if (!string.IsNullOrEmpty(newsSource))
            query = query.Where(n => n.Source.Contains("GDELT"));
        return await query
            .OrderByDescending(n => n.PublishedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PricePoint>> GetPriceRange(string companySymbol, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        return await _db.PricePoints
            .Where(p => p.CompanySymbol == companySymbol.ToUpperInvariant() && p.Date >= from && p.Date <= to)
            .OrderBy(p => p.Date)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SecFiling>> GetFilingsAfter(string companySymbol, DateOnly fromDate, int days = 30, CancellationToken ct = default)
    {
        var fromDayAfter = TemporalBoundary.StartOfDayAfterUtc(fromDate);
        var toCutoff = TemporalBoundary.GetCutoffUtc(fromDate.AddDays(days));
        return await _db.SecFilings
            .Where(f => f.CompanySymbol == companySymbol.ToUpperInvariant() && f.FiledAt >= fromDayAfter && f.FiledAt <= toCutoff)
            .OrderBy(f => f.FiledAt)
            .Take(20)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PricePoint>> GetPricesAfter(string companySymbol, DateOnly fromDate, int days = 30, CancellationToken ct = default)
    {
        var toDate = fromDate.AddDays(days);
        return await _db.PricePoints
            .Where(p => p.CompanySymbol == companySymbol.ToUpperInvariant() && p.Date > fromDate && p.Date <= toDate)
            .OrderBy(p => p.Date)
            .ToListAsync(ct);
    }
}
