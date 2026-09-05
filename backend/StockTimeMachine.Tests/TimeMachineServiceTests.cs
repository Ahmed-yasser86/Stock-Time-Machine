using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using StockTimeMachine;

namespace StockTimeMachine.Tests;

public class TimeMachineServiceTests
{
    private readonly StockTimeMachineDbContext _db;
    private readonly Mock<ISecEdgarProvider> _secEdgarMock = new();
    private readonly Mock<IAlphaVantageProvider> _alphaMock = new();
    private readonly ICompanyDirectory _directory;
    private readonly TimeMachineService _sut;

    public TimeMachineServiceTests()
    {
        _db = new StockTimeMachineDbContext(
            new DbContextOptionsBuilder<StockTimeMachineDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        _directory = new StubCompanyDirectory(
            new CompanyInfo("TSLA", "Tesla, Inc.", "0001318605", "NASDAQ", "Consumer Discretionary", "Automobiles"),
            new CompanyInfo("AAPL", "Apple Inc.", "0000320193", "NASDAQ", "Technology", "Consumer Electronics"),
            new CompanyInfo("MSFT", "Microsoft Corporation", "0000789019", "NASDAQ", "Technology", "Software"),
            new CompanyInfo("GOOGL", "Alphabet Inc.", "0001652044", "NASDAQ", "Communication Services", "Interactive Media & Services"),
            new CompanyInfo("AMZN", "Amazon.com, Inc.", "0001018724", "NASDAQ", "Consumer Discretionary", "Internet & Direct Marketing Retail"),
            new CompanyInfo("NVDA", "NVIDIA Corporation", "0001045810", "NASDAQ", "Technology", "Semiconductors"),
            new CompanyInfo("NFLX", "Netflix, Inc.", "0001065280", "NASDAQ", "Communication Services", "Entertainment"),
            new CompanyInfo("DIS", "Walt Disney Co.", "0001001039", "NYSE", "Communication Services", "Entertainment"),
            new CompanyInfo("ZZZZ", "ZZZZ Unknown", "", "", "", "")
        );

        _alphaMock.Setup(x => x.GetDailyPrices(It.IsAny<string>(), It.IsAny<DateOnly?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PricePoint>());

        _secEdgarMock.Setup(x => x.GetCompanyFilings(It.IsAny<string>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SecFiling>());

        _secEdgarMock.Setup(x => x.GetCompanyProfile(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Company?)null);

        _sut = new TimeMachineService(
            new CompanyRepository(_db, NullLogger<CompanyRepository>.Instance),
            new HistoricalDataRepository(_db, NullLogger<HistoricalDataRepository>.Instance),
            _secEdgarMock.Object,
            _alphaMock.Object,
            _directory,
            Array.Empty<ICompanyLookup>(),
            new FixedNewsProviderFactory(new NullNewsProvider(NullLogger<NullNewsProvider>.Instance)),
            NullLogger<TimeMachineService>.Instance);
    }

    private sealed class CountingNewsProvider : INewsProvider
    {
        private readonly IReadOnlyList<NewsArticle> _articles;
        public int Calls { get; private set; }
        public CountingNewsProvider(IReadOnlyList<NewsArticle> articles) => _articles = articles;
        public Task<IReadOnlyList<NewsArticle>> SearchAsync(string symbol, DateOnly cutoffDate, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(_articles);
        }
    }

    [Fact]
    public async Task GetSnapshot_StaleNewsCache_RefreshesOnce()
    {
        var db = new StockTimeMachineDbContext(
            new DbContextOptionsBuilder<StockTimeMachineDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);
        await repo.StoreNews("TSLA", new[]
        {
            new NewsArticle { Id = "old", Title = "Market notes", Source = "GDELT", PublishedAt = new DateTime(2020, 1, 20), Url = "https://example.com/old", CompanySymbol = "TSLA" },
        });
        var news = new CountingNewsProvider(new[]
        {
            new NewsArticle { Id = "fresh", Title = "Tesla recall expands", Source = "GDELT", PublishedAt = new DateTime(2020, 2, 10), Url = "https://example.com/fresh", CompanySymbol = "TSLA" },
        });
        var sut = new TimeMachineService(
            new CompanyRepository(db, NullLogger<CompanyRepository>.Instance),
            repo, _secEdgarMock.Object, _alphaMock.Object, _directory,
            Array.Empty<ICompanyLookup>(),
            new FixedNewsProviderFactory(news),
            NullLogger<TimeMachineService>.Instance);

        var snapshot = await sut.GetSnapshot("TSLA", new DateOnly(2020, 2, 20), NewsSources.Gdelt);

        // Jan-20 cache vs Feb-20 cutoff: stale → exactly one refresh; the
        // company-naming article leads even though it is older than nothing here.
        Assert.Equal(1, news.Calls);
        Assert.Contains(snapshot.RecentNews, n => n.Id == "fresh");
        Assert.Equal("fresh", snapshot.RecentNews[0].Id);
    }

    [Fact]
    public async Task GetSnapshot_FutureDate_Throws()
    {
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        await Assert.ThrowsAsync<InvalidHistoricalDateException>(() => _sut.GetSnapshot("TSLA", futureDate));
    }

    [Fact]
    public async Task GetSnapshot_KnownCompany_ReturnsCompanyInfo()
    {
        var company = new Company { Symbol = "TSLA", Name = "Tesla Inc", Cik = "0001318605" };
        await _db.Companies.AddAsync(company);
        await _db.SaveChangesAsync();

        _alphaMock.Setup(x => x.GetDailyPrices("TSLA", It.IsAny<DateOnly?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PricePoint>
            {
                new() { Date = new DateOnly(2020, 1, 15), Close = 100m, Open = 99m, High = 101m, Low = 98m, Volume = 1000, CompanySymbol = "TSLA" }
            });

        _secEdgarMock.Setup(x => x.GetCompanyFilings("0001318605", It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SecFiling>());

        var snapshot = await _sut.GetSnapshot("TSLA", new DateOnly(2020, 1, 15));

        Assert.Equal("TSLA", snapshot.CompanySymbol);
        Assert.Equal("Tesla Inc", snapshot.Company!.Name);
        Assert.Equal(100m, snapshot.Price);
    }

    [Fact]
    public async Task GetSnapshot_NoDataInDb_FetchesFromProviders()
    {
        var company = new Company { Symbol = "AAPL", Name = "Apple Inc", Cik = "0000320193" };
        await _db.Companies.AddAsync(company);
        await _db.SaveChangesAsync();

        _alphaMock.Setup(x => x.GetDailyPrices("AAPL", It.IsAny<DateOnly?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PricePoint>
            {
                new() { Date = new DateOnly(2020, 1, 15), Close = 200m, Open = 199m, High = 201m, Low = 198m, Volume = 2000, CompanySymbol = "AAPL" }
            });

        _secEdgarMock.Setup(x => x.GetCompanyFilings("0000320193", It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SecFiling>());

        var snapshot = await _sut.GetSnapshot("AAPL", new DateOnly(2020, 1, 15));

        _alphaMock.Verify(x => x.GetDailyPrices("AAPL", It.IsAny<DateOnly?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.AtLeast(1));
        Assert.Equal(200m, snapshot.Price);
    }

    [Fact]
    public async Task GetSnapshot_FilingsFromDb_NotReFetched()
    {
        var company = new Company { Symbol = "MSFT", Name = "Microsoft", Cik = "0000789019" };
        await _db.Companies.AddAsync(company);

        var filing = new SecFiling
        {
            AccessionNumber = "0001-msft-10k",
            CompanySymbol = "MSFT",
            FormType = "10-K",
            FiledAt = new DateTime(2019, 12, 31),
            Url = "https://example.com/filing.pdf"
        };
        await _db.SecFilings.AddAsync(filing);

        var price = new PricePoint
        {
            CompanySymbol = "MSFT",
            Date = new DateOnly(2020, 1, 10),
            Close = 160m, Open = 159m, High = 161m, Low = 158m, Volume = 3000
        };
        await _db.PricePoints.AddAsync(price);
        await _db.SaveChangesAsync();

        var snapshot = await _sut.GetSnapshot("MSFT", new DateOnly(2020, 1, 15));

        // Historical path is served from the database; only the outcome
        // ("what happened afterwards") path reaches the provider.
        _secEdgarMock.Verify(x => x.GetCompanyFilings(It.IsAny<string>(), It.Is<DateOnly?>(d => d.HasValue && d.Value == new DateOnly(2020, 1, 15)), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Single(snapshot.RecentFilings);
    }

    [Fact]
    public async Task GetSnapshot_TemporalFiltering_FutureFilingsExcluded()
    {
        var company = new Company { Symbol = "GOOGL", Name = "Alphabet", Cik = "0001652044" };
        await _db.Companies.AddAsync(company);

        var pastFiling = new SecFiling
        {
            AccessionNumber = "0001-past",
            CompanySymbol = "GOOGL",
            FormType = "10-Q",
            FiledAt = new DateTime(2019, 10, 15),
            Url = "https://example.com/past"
        };
        var futureFiling = new SecFiling
        {
            AccessionNumber = "0002-future",
            CompanySymbol = "GOOGL",
            FormType = "10-Q",
            FiledAt = new DateTime(2020, 6, 15),
            Url = "https://example.com/future"
        };
        await _db.SecFilings.AddRangeAsync(pastFiling, futureFiling);

        var pastPrice = new PricePoint
        {
            CompanySymbol = "GOOGL",
            Date = new DateOnly(2019, 12, 30),
            Close = 90m, Open = 89m, High = 91m, Low = 88m, Volume = 500
        };
        await _db.PricePoints.AddAsync(pastPrice);
        await _db.SaveChangesAsync();

        var snapshot = await _sut.GetSnapshot("GOOGL", new DateOnly(2020, 1, 15));

        Assert.Single(snapshot.RecentFilings);
        Assert.Equal("https://example.com/past", snapshot.RecentFilings[0].Url);
    }

    [Fact]
    public async Task GetSnapshot_TemporalFiltering_FuturePricesExcluded()
    {
        var company = new Company { Symbol = "AMZN", Name = "Amazon", Cik = "0001018724" };
        await _db.Companies.AddAsync(company);

        var pastPrice = new PricePoint
        {
            CompanySymbol = "AMZN",
            Date = new DateOnly(2019, 12, 30),
            Close = 90m, Open = 89m, High = 91m, Low = 88m, Volume = 500
        };
        var futurePrice = new PricePoint
        {
            CompanySymbol = "AMZN",
            Date = new DateOnly(2020, 1, 20),
            Close = 110m, Open = 109m, High = 111m, Low = 108m, Volume = 700
        };
        await _db.PricePoints.AddRangeAsync(pastPrice, futurePrice);

        var pastFiling = new SecFiling
        {
            CompanySymbol = "AMZN",
            FormType = "10-K",
            FiledAt = new DateTime(2019, 11, 15),
            Url = "https://example.com/amzn-10k"
        };
        await _db.SecFilings.AddAsync(pastFiling);
        await _db.SaveChangesAsync();

        var snapshot = await _sut.GetSnapshot("AMZN", new DateOnly(2020, 1, 15));

        Assert.Single(snapshot.RecentPrices);
        Assert.Equal(90m, snapshot.RecentPrices[0].Close);
    }

    [Fact]
    public async Task GetSnapshot_UnknownCompany_ReturnsFallbackCompanyInfo()
    {
        _alphaMock.Setup(x => x.GetDailyPrices("NVDA", It.IsAny<DateOnly?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PricePoint>
            {
                new() { Date = new DateOnly(2020, 1, 15), Close = 50m, Open = 49m, High = 51m, Low = 48m, Volume = 100, CompanySymbol = "NVDA" }
            });

        var snapshot = await _sut.GetSnapshot("NVDA", new DateOnly(2020, 1, 15));
        Assert.Equal("NVDA", snapshot.CompanySymbol);
        Assert.Equal(50m, snapshot.Price);
    }

    [Fact]
    public async Task GetSnapshot_EmptyDbNoProviders_ReturnsEmptySnapshot()
    {
        var snapshot = await _sut.GetSnapshot("ZZZZ", new DateOnly(2020, 1, 15));
        Assert.Equal("ZZZZ", snapshot.CompanySymbol);
        Assert.Equal(0m, snapshot.Price);
        Assert.Empty(snapshot.RecentFilings);
    }

    [Fact]
    public async Task GetSnapshot_WithOutcomePrices_PopulatesOutcomeData()
    {
        await _db.PricePoints.AddRangeAsync(
            new PricePoint { CompanySymbol = "NFLX", Date = new DateOnly(2020, 1, 15), Close = 100m, Open = 99m, High = 101m, Low = 98m, Volume = 1000 },
            new PricePoint { CompanySymbol = "NFLX", Date = new DateOnly(2020, 1, 20), Close = 110m, Open = 109m, High = 111m, Low = 108m, Volume = 1200 }
        );
        await _db.SaveChangesAsync();

        var snapshot = await _sut.GetSnapshot("NFLX", new DateOnly(2020, 1, 15));
        Assert.Single(snapshot.OutcomePrices);
        Assert.Equal(110m, snapshot.OutcomePrice);
    }

    [Fact]
    public async Task GetSnapshot_NoOutcomeData_OutcomePriceIsNull()
    {
        await _db.PricePoints.AddAsync(
            new PricePoint { CompanySymbol = "DIS", Date = new DateOnly(2020, 1, 15), Close = 100m, Open = 99m, High = 101m, Low = 98m, Volume = 1000 }
        );
        await _db.SaveChangesAsync();

        var snapshot = await _sut.GetSnapshot("DIS", new DateOnly(2020, 1, 15));
        Assert.Null(snapshot.OutcomePrice);
        Assert.Empty(snapshot.OutcomePrices);
    }

    [Fact]
    public async Task GetSnapshot_PriceProviderThrows_ReturnsPartialSnapshotWithFilings()
    {
        // US-21: a market-data failure must not destroy the investigation.
        await _db.Companies.AddAsync(new Company { Symbol = "AAPL", Name = "Apple Inc", Cik = "0000320193" });
        await _db.SecFilings.AddAsync(new SecFiling
        {
            CompanySymbol = "AAPL",
            FormType = "10-K",
            FiledAt = new DateTime(2020, 1, 10),
            AccessionNumber = "partial-10k",
            Url = "https://example.com/partial"
        });
        await _db.SaveChangesAsync();

        _alphaMock.Setup(x => x.GetDailyPrices(It.IsAny<string>(), It.IsAny<DateOnly?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ExternalProviderException("Provider down"));

        var snapshot = await _sut.GetSnapshot("AAPL", new DateOnly(2020, 1, 15));

        Assert.Contains("prices", snapshot.FailedSections);
        Assert.Single(snapshot.RecentFilings);
        Assert.False(snapshot.HasMarketData);
    }
}
