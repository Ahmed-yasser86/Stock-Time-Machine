using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using StockTimeMachine;

namespace StockTimeMachine.Tests;

public class SnapshotEngineIntegrationTests
{
    private static (StockTimeMachineDbContext db, Mock<ISecEdgarProvider> sec, Mock<IAlphaVantageProvider> av, TimeMachineService sut) Build(StubCompanyDirectory directory)
    {
        var db = new StockTimeMachineDbContext(
            new DbContextOptionsBuilder<StockTimeMachineDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var sec = new Mock<ISecEdgarProvider>();
        var av = new Mock<IAlphaVantageProvider>();
        av.Setup(x => x.GetDailyPrices(It.IsAny<string>(), It.IsAny<DateOnly?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PricePoint>());
        sec.Setup(x => x.GetCompanyFilings(It.IsAny<string>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SecFiling>());
        sec.Setup(x => x.GetCompanyProfile(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Company?)null);

        var companyRepo = new CompanyRepository(db, NullLogger<CompanyRepository>.Instance);
        var dataRepo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);
        var sut = new TimeMachineService(companyRepo, dataRepo, sec.Object, av.Object, directory, Array.Empty<ICompanyLookup>(), new FixedNewsProviderFactory(new NullNewsProvider(NullLogger<NullNewsProvider>.Instance)), NullLogger<TimeMachineService>.Instance);
        return (db, sec, av, sut);
    }

    [Fact]
    public async Task GetSnapshot_FullPipeline_AssemblesExpectedShape()
    {
        var directory = new StubCompanyDirectory(
            new CompanyInfo("TSLA", "Tesla, Inc.", "0001318605", "NASDAQ", "Consumer Discretionary", "Automobiles"));

        var (db, sec, av, sut) = Build(directory);

        await db.Companies.AddAsync(new Company { Symbol = "TSLA", Name = "Tesla, Inc.", Cik = "0001318605" });
        await db.PricePoints.AddAsync(new PricePoint { CompanySymbol = "TSLA", Date = new DateOnly(2020, 1, 15), Open = 99m, High = 101m, Low = 98m, Close = 100m, Volume = 1000 });
        await db.SecFilings.AddAsync(new SecFiling
        {
            CompanySymbol = "TSLA",
            FormType = "10-K",
            FiledAt = new DateTime(2020, 1, 10),
            AccessionNumber = "10k-1",
            Url = "https://example.com/10k"
        });
        await db.SaveChangesAsync();

        var snapshot = await sut.GetSnapshot("TSLA", new DateOnly(2020, 1, 15));

        Assert.Equal("TSLA", snapshot.CompanySymbol);
        Assert.Equal("Tesla, Inc.", snapshot.Company!.Name);
        Assert.Equal(100m, snapshot.Price);
        Assert.Single(snapshot.RecentPrices);
        Assert.Single(snapshot.RecentFilings);
        // Historical path is served from the database; only the outcome
        // ("what happened afterwards") path reaches the provider.
        sec.Verify(x => x.GetCompanyFilings(It.IsAny<string>(), It.Is<DateOnly?>(d => d.HasValue && d.Value == new DateOnly(2020, 1, 15)), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetSnapshot_NoData_FallsBackToProviders_AndPersists()
    {
        var directory = new StubCompanyDirectory(
            new CompanyInfo("AAPL", "Apple Inc.", "0000320193", "NASDAQ", "Technology", "Consumer Electronics"));

        var (db, sec, av, sut) = Build(directory);

        av.Setup(x => x.GetDailyPrices("AAPL", It.IsAny<DateOnly?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PricePoint>
            {
                new() { CompanySymbol = "AAPL", Date = new DateOnly(2020, 1, 15), Open = 199m, High = 201m, Low = 198m, Close = 200m, Volume = 2000 }
            });

        sec.Setup(x => x.GetCompanyFilings("0000320193", It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SecFiling>
            {
                new() { CompanySymbol = "AAPL", FormType = "10-Q", FiledAt = new DateTime(2020, 1, 5), AccessionNumber = "10q-1", Url = "https://example.com/10q" }
            });

        var snapshot = await sut.GetSnapshot("AAPL", new DateOnly(2020, 1, 15));

        Assert.Equal(200m, snapshot.Price);
        Assert.Single(snapshot.RecentFilings);
        Assert.Equal("10-Q", snapshot.RecentFilings[0].FormType);
        av.Verify(x => x.GetDailyPrices("AAPL", It.IsAny<DateOnly?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        // Twice: once for the historical window, once for the outcome window.
        sec.Verify(x => x.GetCompanyFilings("0000320193", It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetSnapshot_TemporalBoundary_FutureDataExcluded()
    {
        var directory = new StubCompanyDirectory(
            new CompanyInfo("MSFT", "Microsoft", "0000789019", "NASDAQ", "Technology", "Software"));

        var (db, sec, av, sut) = Build(directory);

        await db.Companies.AddAsync(new Company { Symbol = "MSFT", Name = "Microsoft", Cik = "0000789019" });
        await db.SecFilings.AddRangeAsync(
            new SecFiling { CompanySymbol = "MSFT", FormType = "10-K", FiledAt = new DateTime(2019, 12, 31), AccessionNumber = "k-1", Url = "u" },
            new SecFiling { CompanySymbol = "MSFT", FormType = "10-K", FiledAt = new DateTime(2020, 6, 30), AccessionNumber = "k-2", Url = "u" }
        );
        await db.SaveChangesAsync();

        var snapshot = await sut.GetSnapshot("MSFT", new DateOnly(2020, 1, 15));

        Assert.Single(snapshot.RecentFilings);
        Assert.Equal("k-1", snapshot.RecentFilings[0].AccessionNumber);
    }

    [Fact]
    public async Task GetSnapshot_UnknownCompany_StillReturnsShell()
    {
        var directory = new StubCompanyDirectory();

        var (_, _, _, sut) = Build(directory);

        var snapshot = await sut.GetSnapshot("ZZZZ", new DateOnly(2020, 1, 15));

        Assert.Equal("ZZZZ", snapshot.CompanySymbol);
        Assert.NotNull(snapshot.Company);
        Assert.Equal("ZZZZ", snapshot.Company!.Name);
        Assert.Empty(snapshot.RecentFilings);
    }
}