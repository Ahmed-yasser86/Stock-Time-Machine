using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using StockTimeMachine.Entities;
using StockTimeMachine.ProviderContracts;
using StockTimeMachine.Repositories;
using StockTimeMachine.RepositoryContracts;
using StockTimeMachine.Services;

namespace StockTimeMachine.Tests;

public class TimeMachineServiceTests
{
    private readonly StockTimeMachineDbContext _db;
    private readonly Mock<ISecEdgarProvider> _secEdgarMock = new();
    private readonly Mock<IAlphaVantageProvider> _alphaMock = new();
    private readonly TimeMachineService _sut;

    public TimeMachineServiceTests()
    {
        _db = new StockTimeMachineDbContext(
            new DbContextOptionsBuilder<StockTimeMachineDbContext>()
                .UseInMemoryDatabase("TimeMachineServiceTests").Options);

        _sut = new TimeMachineService(
            new CompanyRepository(_db, NullLogger<CompanyRepository>.Instance),
            new HistoricalDataRepository(_db, NullLogger<HistoricalDataRepository>.Instance),
            _secEdgarMock.Object,
            _alphaMock.Object,
            NullLogger<TimeMachineService>.Instance);
    }

    [Fact]
    public async Task GetSnapshot_FutureDate_Throws()
    {
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.GetSnapshot("TSLA", futureDate));
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

        _alphaMock.Verify(x => x.GetDailyPrices("AAPL", It.IsAny<DateOnly?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
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

        _secEdgarMock.Verify(x => x.GetCompanyFilings(It.IsAny<string>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()), Times.Never);
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
}
