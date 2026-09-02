using Microsoft.Extensions.Logging;
using StockTimeMachine.Entities;
using StockTimeMachine.ProviderContracts;
using StockTimeMachine.RepositoryContracts;
using StockTimeMachine.ServiceContracts;

namespace StockTimeMachine.Services;

public class TimeMachineService : ITimeMachineService
{
    private readonly ICompanyRepository _companyRepo;
    private readonly IHistoricalDataRepository _dataRepo;
    private readonly ISecEdgarProvider _secEdgar;
    private readonly IAlphaVantageProvider _alphaVantage;
    private readonly ILogger<TimeMachineService> _logger;

    private static readonly Dictionary<string, string> SymbolToCik = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TSLA"] = "0001318605",
        ["AAPL"] = "0000320193",
        ["MSFT"] = "0000789019",
        ["GOOGL"] = "0001652044",
        ["AMZN"] = "0001018724",
        ["NVDA"] = "0001045810",
        ["META"] = "0001326801",
        ["NFLX"] = "0001065280",
        ["BRK.B"] = "0001067983",
        ["JPM"] = "0000019617",
        ["V"] = "0001403172",
        ["JNJ"] = "0000200406",
        ["WMT"] = "0000024024",
        ["PG"] = "0000080424",
        ["MA"] = "0001141391",
        ["UNH"] = "0000731766",
        ["HD"] = "0000354950",
        ["DIS"] = "0001001039",
        ["BAC"] = "000070858",
        ["XOM"] = "0000034088",
    };

    public TimeMachineService(
        ICompanyRepository companyRepo,
        IHistoricalDataRepository dataRepo,
        ISecEdgarProvider secEdgar,
        IAlphaVantageProvider alphaVantage,
        ILogger<TimeMachineService> logger)
    {
        _companyRepo = companyRepo;
        _dataRepo = dataRepo;
        _secEdgar = secEdgar;
        _alphaVantage = alphaVantage;
        _logger = logger;
    }

    public async Task<HistoricalSnapshot> GetSnapshot(string symbol, DateOnly asOfDate, CancellationToken ct = default)
    {
        var historicalDate = HistoricalDate.Create(asOfDate);

        var company = await ResolveCompany(symbol, ct);
        var prices = await ResolvePrices(symbol, historicalDate.Date, ct);
        var filings = await ResolveFilings(company, historicalDate.Date, ct);

        var latestPrice = prices.FirstOrDefault(p => p.Date <= historicalDate.Date);

        return new HistoricalSnapshot
        {
            CompanySymbol = symbol.ToUpper(),
            SnapshotDate = historicalDate.Date,
            Price = latestPrice?.Close ?? 0,
            Open = latestPrice?.Open ?? 0,
            High = latestPrice?.High ?? 0,
            Low = latestPrice?.Low ?? 0,
            Volume = latestPrice?.Volume ?? 0,
            RecentPrices = prices.Take(30).ToList(),
            RecentFilings = filings.Take(10).ToList(),
            RecentNews = new(),
            Company = company,
        };
    }

    private async Task<Company> ResolveCompany(string symbol, CancellationToken ct)
    {
        var company = await _companyRepo.GetBySymbol(symbol, ct);
        if (company is not null)
            return company;

        if (!SymbolToCik.TryGetValue(symbol, out var cik))
        {
            _logger.LogWarning("No CIK mapping for {Symbol}, skipping company lookup", symbol);
            return new Company { Symbol = symbol.ToUpper(), Name = symbol.ToUpper() };
        }

        _logger.LogInformation("Fetching company profile from SEC EDGAR for {Symbol}", symbol);
        var profile = await _secEdgar.GetCompanyProfile(cik, ct);
        if (profile is not null)
        {
            await _companyRepo.Add(profile, ct);
            return profile;
        }

        return new Company { Symbol = symbol.ToUpper(), Name = symbol.ToUpper(), Cik = cik };
    }

    private async Task<IReadOnlyList<PricePoint>> ResolvePrices(string symbol, DateOnly asOfDate, CancellationToken ct)
    {
        var prices = await _dataRepo.GetPricesAsOf(symbol, asOfDate, 30, ct);
        if (prices.Count > 0)
            return prices;

        if (!SymbolToCik.ContainsKey(symbol))
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
        if (string.IsNullOrEmpty(cik) && SymbolToCik.TryGetValue(company.Symbol, out var mappedCik))
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
}
