using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

public class TimeMachineService : ITimeMachineService
{
    private readonly ICompanyRepository _companyRepo;
    private readonly IHistoricalDataRepository _dataRepo;
    private readonly ISecEdgarProvider _secEdgar;
    private readonly IAlphaVantageProvider _alphaVantage;
    private readonly ICompanyDirectory _directory;
    private readonly IEnumerable<ICompanyLookup> _fallbacks;
    private readonly INewsProviderFactory _newsFactory;
    private readonly ILogger<TimeMachineService> _logger;

    public TimeMachineService(
        ICompanyRepository companyRepo,
        IHistoricalDataRepository dataRepo,
        ISecEdgarProvider secEdgar,
        IAlphaVantageProvider alphaVantage,
        ICompanyDirectory directory,
        IEnumerable<ICompanyLookup> fallbacks,
        INewsProviderFactory newsFactory,
        ILogger<TimeMachineService> logger)
    {
        _companyRepo = companyRepo;
        _dataRepo = dataRepo;
        _secEdgar = secEdgar;
        _alphaVantage = alphaVantage;
        _directory = directory;
        _fallbacks = fallbacks;
        _newsFactory = newsFactory;
        _logger = logger;
    }

    // Backward-compatible overload (existing callers + tests): default news source.
    public Task<HistoricalSnapshot> GetSnapshot(string symbol, DateOnly asOfDate, CancellationToken ct = default) =>
        GetSnapshot(symbol, asOfDate, newsSource: null, ct);

    public Task<HistoricalSnapshot> GetSnapshot(string symbol, DateOnly asOfDate, string? newsSource, CancellationToken ct = default) =>
        GetSnapshot(symbol, asOfDate, newsSource, sections: null, progress: null, ct);

    public async Task<HistoricalSnapshot> GetSnapshot(string symbol, DateOnly asOfDate, string? newsSource, IReadOnlySet<string>? sections, IProgress<SnapshotProgress>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new InvalidHistoricalDateException("Symbol is required.");

        var historicalDate = HistoricalDate.Create(asOfDate);
        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        var selectedNewsSource = NewsSources.Normalize(newsSource ?? _newsFactory.DefaultSource);

        Report(progress, SnapshotStages.Company, SnapshotProgress.Started);
        var company = await ResolveCompany(normalizedSymbol, ct);
        Report(progress, SnapshotStages.Company, SnapshotProgress.Complete, company.Name);

        // US-21: provider failures are isolated per section. One source failing
        // degrades to an honest empty/partial section, never a failed investigation.
        // Rescoped investigations skip unrequested sections entirely (no DB, no providers).
        var failedSections = new List<string>();
        var prices = await ResolveSection(sections, SnapshotSections.Prices, SnapshotStages.Prices, failedSections,
            () => ResolvePrices(normalizedSymbol, company, historicalDate.Date, ct), progress, ct);
        var filings = await ResolveSection(sections, SnapshotSections.Filings, SnapshotStages.Filings, failedSections,
            () => ResolveFilings(company, historicalDate.Date, ct), progress, ct);
        Report(progress, SnapshotStages.Disclosures, SnapshotProgress.Complete,
            $"{filings.Count(f => f.IsMaterialDisclosure)} material disclosures", filings.Count);
        IReadOnlyList<PricePoint> outcomePrices;
        IReadOnlyList<SecFiling> outcomeFilings;
        if (SnapshotSections.Includes(sections, SnapshotSections.Outcome))
        {
            Report(progress, SnapshotStages.Outcome, SnapshotProgress.Started);
            outcomePrices = await Isolate("outcome", failedSections,
                () => ResolveOutcomePrices(normalizedSymbol, company, historicalDate.Date, ct), ct);
            outcomeFilings = await Isolate("outcome", failedSections,
                () => ResolveOutcomeFilings(company, historicalDate.Date, ct), ct);
            Report(progress, SnapshotStages.Outcome,
                failedSections.Contains("outcome") ? SnapshotProgress.Failed : SnapshotProgress.Complete,
                failedSections.Contains("outcome") ? "provider unavailable"
                    : $"{outcomePrices.Count} prices, {outcomeFilings.Count} filings",
                outcomePrices.Count + outcomeFilings.Count);
        }
        else
        {
            Report(progress, SnapshotStages.Outcome, SnapshotProgress.Skipped, "not requested");
            outcomePrices = Array.Empty<PricePoint>();
            outcomeFilings = Array.Empty<SecFiling>();
        }
        var news = await ResolveSection(sections, SnapshotSections.News, SnapshotStages.News, failedSections,
            () => ResolveNews(normalizedSymbol, company.Name, selectedNewsSource, historicalDate.Date, ct), progress, ct);

        // Defense in depth: regardless of what providers returned, the application
        // layer re-enforces the temporal boundary before assembling the snapshot.
        // Filings are date-only evidence (calendar-day rule); news carries true
        // timestamps (end-of-day Eastern cutoff rule).
        var cutoff = TemporalBoundary.GetCutoffUtc(historicalDate.Date);
        Report(progress, SnapshotStages.Boundary, SnapshotProgress.Complete, cutoff.ToString("o"));
        var filingDayAfter = TemporalBoundary.StartOfDayAfterUtc(historicalDate.Date);
        filings = filings.Where(f => f.FiledAt < filingDayAfter).ToList();
        news = news.Where(n => n.PublishedAt <= cutoff).ToList();

        var latestPrice = prices.FirstOrDefault(p => p.Date <= historicalDate.Date);

        Report(progress, SnapshotStages.Assembly, SnapshotProgress.Started);
        var assembled = new HistoricalSnapshot
        {
            CompanySymbol = normalizedSymbol,
            SnapshotDate = historicalDate.Date,
            HasMarketData = latestPrice is not null,
            PriceDate = latestPrice?.Date,
            NewsSource = selectedNewsSource,
            Price = latestPrice?.Close ?? 0,
            Open = latestPrice?.Open ?? 0,
            High = latestPrice?.High ?? 0,
            Low = latestPrice?.Low ?? 0,
            Volume = latestPrice?.Volume ?? 0,
            RecentPrices = prices.Take(30).ToList(),
            RecentFilings = filings.Take(10).ToList(),
            RecentNews = news.Take(20).ToList(),
            Company = company,
            OutcomePrices = outcomePrices.ToList(),
            OutcomePrice = outcomePrices.LastOrDefault()?.Close,
            OutcomeFilings = outcomeFilings.ToList(),
            FailedSections = failedSections,
        };
        Report(progress, SnapshotStages.Assembly, SnapshotProgress.Complete,
            $"{assembled.RecentPrices.Count + assembled.RecentFilings.Count + assembled.RecentNews.Count} evidence items");
        return assembled;
    }

    private static void Report(IProgress<SnapshotProgress>? progress, string stage, string state, string? detail = null, int? count = null) =>
        progress?.Report(new SnapshotProgress(stage, state, detail, count));

    // Resolves one section with honest progress: skipped when rescoped out,
    // failed (never a false success) when its provider fails.
    private async Task<IReadOnlyList<T>> ResolveSection<T>(
        IReadOnlySet<string>? sections,
        string section,
        string stage,
        List<string> failedSections,
        Func<Task<IReadOnlyList<T>>> resolve,
        IProgress<SnapshotProgress>? progress,
        CancellationToken ct)
    {
        if (!SnapshotSections.Includes(sections, section))
        {
            Report(progress, stage, SnapshotProgress.Skipped, "not requested");
            return Array.Empty<T>();
        }

        Report(progress, stage, SnapshotProgress.Started);
        var items = await Isolate(section, failedSections, resolve, ct);
        var failed = failedSections.Contains(section);
        Report(progress, stage, failed ? SnapshotProgress.Failed : SnapshotProgress.Complete,
            failed ? "provider unavailable" : $"{items.Count} items", items.Count);
        return items;
    }

    // Runs one section's resolution. Provider-side failures (network, 5xx,
    // malformed payloads, rate limits) are recorded and degraded to empty;
    // cancellation and domain validation errors still propagate.
    private async Task<IReadOnlyList<T>> Isolate<T>(
        string section,
        List<string> failedSections,
        Func<Task<IReadOnlyList<T>>> resolve,
        CancellationToken ct)
    {
        try
        {
            return await resolve();
        }
        catch (OperationCanceledException) { throw; }
        catch (InvalidHistoricalDateException) { throw; }
        catch (HistoricalDataNotFoundException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Section {Section} unavailable; continuing with partial snapshot", section);
            failedSections.Add(section);
            return Array.Empty<T>();
        }
    }

    private async Task<Company> ResolveCompany(string symbol, CancellationToken ct)
    {
        var company = await _companyRepo.GetBySymbol(symbol, ct);
        if (company is not null)
            return company;

        if (_directory.TryGetCik(symbol, out var cik))
        {
            _logger.LogInformation("Fetching company profile from SEC EDGAR for {Symbol}", symbol);
            try
            {
                var profile = await _secEdgar.GetCompanyProfile(cik, ct);
                if (profile is not null)
                {
                    await _companyRepo.Add(profile, ct);
                    return profile;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SEC EDGAR profile lookup failed for {Symbol}", symbol);
            }
        }
        else
        {
            _logger.LogDebug("No CIK mapping for {Symbol} in directory; trying fallback lookups", symbol);
        }

        foreach (var fallback in _fallbacks)
        {
            try
            {
                var profile = await fallback.GetCompanyProfileAsync(symbol, ct);
                if (profile is not null)
                {
                    await _companyRepo.Add(profile, ct);
                    return profile;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallback company lookup {Type} failed for {Symbol}", fallback.GetType().Name, symbol);
            }
        }

        return _directory.TryGet(symbol, out var info) && info is not null
            ? new Company { Symbol = info.Symbol, Name = info.Name, Cik = info.Cik, Exchange = info.Exchange, Sector = info.Sector, Industry = info.Industry }
            : new Company { Symbol = symbol, Name = symbol };
    }

    private async Task<IReadOnlyList<PricePoint>> ResolvePrices(string symbol, Company company, DateOnly asOfDate, CancellationToken ct)
    {
        // Free-tier discipline: the database is always consulted before any
        // external call, so repeated investigations cost zero provider requests.
        var prices = await _dataRepo.GetPricesAsOf(symbol, asOfDate, 30, ct);
        if (prices.Count > 0)
            return prices;

        // Live fetch requires a CIK-backed identity (directory or resolved
        // company) so typos and unknown symbols never burn provider quota.
        var cik = company.Cik;
        if (string.IsNullOrEmpty(cik) && _directory.TryGetCik(symbol, out var mappedCik))
            cik = mappedCik;
        if (string.IsNullOrEmpty(cik))
        {
            _logger.LogWarning("No CIK mapping for {Symbol}, cannot fetch prices", symbol);
            return prices;
        }

        _logger.LogInformation("Fetching prices from Alpha Vantage for {Symbol}", symbol);
        var freshPrices = await _alphaVantage.GetDailyPrices(symbol, asOfDate, 365, ct);
        if (freshPrices.Count > 0)
        {
            await _dataRepo.StorePrices(symbol, freshPrices, ct);
            return await _dataRepo.GetPricesAsOf(symbol, asOfDate, 30, ct);
        }

        return prices;
    }

    private async Task<IReadOnlyList<SecFiling>> ResolveFilings(Company company, DateOnly asOfDate, CancellationToken ct)
    {
        var cik = company.Cik;
        if (string.IsNullOrEmpty(cik) && _directory.TryGetCik(company.Symbol, out var mappedCik))
            cik = mappedCik;

        if (string.IsNullOrEmpty(cik))
        {
            _logger.LogWarning("No CIK for {Symbol}, skipping filings", company.Symbol);
            return new List<SecFiling>();
        }

        var filings = await _dataRepo.GetFilingsAsOf(company.Symbol, asOfDate, ct);
        if (filings.Count > 0)
            return filings;

        _logger.LogInformation("Fetching filings from SEC EDGAR for {Symbol}", company.Symbol);
        var freshFilings = await _secEdgar.GetCompanyFilings(cik, asOfDate, ct);
        if (freshFilings.Count > 0)
        {
            await _dataRepo.StoreFilings(company.Symbol, freshFilings, ct);
            return await _dataRepo.GetFilingsAsOf(company.Symbol, asOfDate, ct);
        }

        return filings;
    }

    private async Task<IReadOnlyList<NewsArticle>> ResolveNews(string symbol, string? companyName, string newsSource, DateOnly asOfDate, CancellationToken ct)
    {
        var cached = await _dataRepo.GetNewsAsOf(symbol, asOfDate, ct);
        var fromSelectedSource = cached.Where(n => IsFromSource(n, newsSource)).ToList();
        if (fromSelectedSource.Count > 0)
            return fromSelectedSource;

        var provider = _newsFactory.Get(newsSource);
        var fresh = await provider.SearchAsync(symbol, companyName, asOfDate, ct);
        if (fresh.Count > 0)
        {
            await _dataRepo.StoreNews(symbol, fresh, ct);
            var reread = await _dataRepo.GetNewsAsOf(symbol, asOfDate, ct);
            return reread.Where(n => IsFromSource(n, newsSource)).ToList();
        }

        return fromSelectedSource;
    }

    private static bool IsFromSource(NewsArticle article, string newsSource)
    {
        var source = article.Source ?? "";
        if (newsSource == NewsSources.AlphaVantage)
            return source.Contains("Alpha Vantage", StringComparison.OrdinalIgnoreCase);
        if (newsSource == NewsSources.MarketAux)
            return source.Contains("MarketAux", StringComparison.OrdinalIgnoreCase);
        return source.Contains("GDELT", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<IReadOnlyList<SecFiling>> ResolveOutcomeFilings(Company company, DateOnly asOfDate, CancellationToken ct)
    {
        var outcomeFilings = await _dataRepo.GetFilingsAfter(company.Symbol, asOfDate, 30, ct);
        if (outcomeFilings.Count > 0)
            return outcomeFilings;

        var cik = company.Cik;
        if (string.IsNullOrEmpty(cik) && _directory.TryGetCik(company.Symbol, out var mappedCik))
            cik = mappedCik;
        if (string.IsNullOrEmpty(cik))
            return new List<SecFiling>();

        _logger.LogInformation("Fetching outcome filings from SEC EDGAR for {Symbol}", company.Symbol);
        var windowEnd = asOfDate.AddDays(30);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (windowEnd > today)
            windowEnd = today;
        var freshFilings = await _secEdgar.GetCompanyFilings(cik, windowEnd, ct);
        if (freshFilings.Count > 0)
        {
            await _dataRepo.StoreFilings(company.Symbol, freshFilings, ct);
            return await _dataRepo.GetFilingsAfter(company.Symbol, asOfDate, 30, ct);
        }

        return new List<SecFiling>();
    }

    private async Task<IReadOnlyList<PricePoint>> ResolveOutcomePrices(string symbol, Company company, DateOnly asOfDate, CancellationToken ct)
    {
        var outcomePrices = await _dataRepo.GetPricesAfter(symbol, asOfDate, 30, ct);
        if (outcomePrices.Count > 0)
            return outcomePrices.ToList();

        var cik = company.Cik;
        if (string.IsNullOrEmpty(cik) && _directory.TryGetCik(symbol, out var mappedCik))
            cik = mappedCik;
        if (string.IsNullOrEmpty(cik))
            return new List<PricePoint>();

        _logger.LogInformation("Fetching outcome prices from Alpha Vantage for {Symbol}", symbol);
        var futureDate = asOfDate.AddDays(30);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (futureDate > today)
            futureDate = today;
        var freshPrices = await _alphaVantage.GetDailyPrices(symbol, futureDate, 60, ct);
        if (freshPrices is not null && freshPrices.Count > 0)
        {
            await _dataRepo.StorePrices(symbol, freshPrices, ct);
            var result = await _dataRepo.GetPricesAfter(symbol, asOfDate, 30, ct);
            return result.ToList();
        }

        return new List<PricePoint>();
    }
}
