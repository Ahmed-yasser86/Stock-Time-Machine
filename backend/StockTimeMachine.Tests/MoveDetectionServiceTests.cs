using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using StockTimeMachine;

namespace StockTimeMachine.Tests;

// Every test constructs MoveDetectionService directly: no controller, no
// HttpContext, no web infrastructure (same headless guarantee as US-28).
public class MoveDetectionServiceTests
{
    private sealed class StubNewsProvider : INewsProvider
    {
        private readonly IReadOnlyList<NewsArticle> _articles;
        public StubNewsProvider(IReadOnlyList<NewsArticle> articles) => _articles = articles;
        public Task<IReadOnlyList<NewsArticle>> SearchAsync(string symbol, DateOnly cutoffDate, CancellationToken ct = default) =>
            Task.FromResult(_articles);
    }

    private sealed class ThrowingSocialProvider : ISocialSignalProvider
    {
        public string ProviderName => "Throwing";
        public Task<IReadOnlyList<SocialSignal>> GetSignals(string symbol, string? companyName, DateOnly from, DateOnly to, CancellationToken ct = default) =>
            throw new HttpRequestException("Social down");
    }

    private static (StockTimeMachineDbContext db, Mock<IAlphaVantageProvider> av, StubCompanyDirectory directory) BuildDb()
    {
        var db = new StockTimeMachineDbContext(
            new DbContextOptionsBuilder<StockTimeMachineDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var av = new Mock<IAlphaVantageProvider>();
        av.Setup(x => x.GetDailyPrices(It.IsAny<string>(), It.IsAny<DateOnly?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PricePoint>());
        var directory = new StubCompanyDirectory(
            new CompanyInfo("TSLA", "Tesla, Inc.", "0001318605", "NASDAQ", "Consumer Discretionary", "Automobiles"));
        return (db, av, directory);
    }

    private static MoveDetectionService Sut(
        StockTimeMachineDbContext db, Mock<IAlphaVantageProvider> av, StubCompanyDirectory directory,
        INewsProvider news, IEnumerable<ISocialSignalProvider>? social = null) =>
        new(new CompanyRepository(db, NullLogger<CompanyRepository>.Instance),
            new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
            av.Object, directory,
            new FixedNewsProviderFactory(news),
            social ?? Array.Empty<ISocialSignalProvider>(),
            NullLogger<MoveDetectionService>.Instance);

    // 40 flat days at 100, then a +15% spike on high volume, then flat at 115.
    private static async Task SeedSpike(StockTimeMachineDbContext db, string symbol = "TSLA")
    {
        var start = new DateOnly(2020, 1, 2);
        var prices = new List<PricePoint>();
        for (int i = 0; i < 40; i++)
        {
            var date = start.AddDays(i);
            var spike = i == 30;
            var close = i < 30 ? 100m : 115m;
            prices.Add(new PricePoint
            {
                CompanySymbol = symbol, Date = date,
                Open = close, High = spike ? 116m : close + 1, Low = spike ? 99m : close - 1,
                Close = close, Volume = spike ? 5000 : 1000,
            });
        }
        await db.PricePoints.AddRangeAsync(prices);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetMoves_DetectsSpikeAsTopMove()
    {
        var (db, av, directory) = BuildDb();
        await SeedSpike(db);
        var sut = Sut(db, av, directory, new NullNewsProvider(NullLogger<NullNewsProvider>.Instance));

        var window = await sut.GetMoves("TSLA", new DateOnly(2020, 2, 20));

        Assert.True(window.Summary.SufficientHistory);
        Assert.NotEmpty(window.KeyMoves);
        var top = window.KeyMoves[0];
        Assert.Equal(new DateOnly(2020, 2, 1), top.Date); // start Jan-2 + 30 days
        Assert.Contains(MoveFlags.Spike, top.Flags);
        Assert.Contains(MoveFlags.HighVolume, top.Flags);
    }

    [Fact]
    public async Task GetMoves_AttachesArrivalMap()
    {
        var (db, av, directory) = BuildDb();
        await SeedSpike(db);
        var sut = Sut(db, av, directory, new NullNewsProvider(NullLogger<NullNewsProvider>.Instance));

        var window = await sut.GetMoves("TSLA", new DateOnly(2020, 2, 20));

        var first = window.EvidenceByDate.Values.First();
        Assert.Contains(first.Arrival, a => a.Layer == "market" && a.State == "observed");
        Assert.DoesNotContain(first.Arrival, a => a.State != "observed" && a.State != "silent");
    }

    [Fact]
    public async Task GetMoves_IsDeterministic()
    {
        var (db, av, directory) = BuildDb();
        await SeedSpike(db);
        var sut = Sut(db, av, directory, new NullNewsProvider(NullLogger<NullNewsProvider>.Instance));

        var first = await sut.GetMoves("TSLA", new DateOnly(2020, 2, 20));
        var second = await sut.GetMoves("TSLA", new DateOnly(2020, 2, 20));

        Assert.Equal(
            first.KeyMoves.Select(m => (m.Date, m.Score)),
            second.KeyMoves.Select(m => (m.Date, m.Score)));
    }

    [Fact]
    public async Task GetMoves_InsufficientHistory_ReturnsEmptyWindow()
    {
        var (db, av, directory) = BuildDb();
        await db.PricePoints.AddAsync(new PricePoint
        {
            CompanySymbol = "TSLA", Date = new DateOnly(2020, 1, 15),
            Open = 99m, High = 101m, Low = 98m, Close = 100m, Volume = 1000
        });
        await db.SaveChangesAsync();
        var sut = Sut(db, av, directory, new NullNewsProvider(NullLogger<NullNewsProvider>.Instance));

        var window = await sut.GetMoves("TSLA", new DateOnly(2020, 1, 15));

        Assert.False(window.Summary.SufficientHistory);
        Assert.Empty(window.KeyMoves);
    }

    [Fact]
    public async Task GetMoves_EvidenceRespectsEachMoveCutoff()
    {
        var (db, av, directory) = BuildDb();
        await SeedSpike(db);
        await db.SecFilings.AddRangeAsync(
            new SecFiling { CompanySymbol = "TSLA", FormType = "10-K", FiledAt = new DateTime(2020, 1, 28, 0, 0, 0, DateTimeKind.Utc), AccessionNumber = "past", Url = "https://example.com/past" },
            new SecFiling { CompanySymbol = "TSLA", FormType = "10-K", FiledAt = new DateTime(2020, 2, 10, 0, 0, 0, DateTimeKind.Utc), AccessionNumber = "future", Url = "https://example.com/future" });
        await db.SaveChangesAsync();
        var news = new StubNewsProvider(new[]
        {
            new NewsArticle { Id = "n1", Title = "Past news", Source = "GDELT", PublishedAt = new DateTime(2020, 1, 25, 0, 0, 0, DateTimeKind.Utc), Url = "https://example.com/n1", CompanySymbol = "TSLA" },
            new NewsArticle { Id = "n2", Title = "Future news", Source = "GDELT", PublishedAt = new DateTime(2020, 2, 15, 0, 0, 0, DateTimeKind.Utc), Url = "https://example.com/n2", CompanySymbol = "TSLA" },
        });
        var sut = Sut(db, av, directory, news);

        var window = await sut.GetMoves("TSLA", new DateOnly(2020, 2, 20));
        var moveKey = window.KeyMoves[0].Date.ToString("yyyy-MM-dd");
        var evidence = window.EvidenceByDate[moveKey];

        // Move is Feb-1: Jan filings/news eligible, Feb-10+/Feb-15 excluded.
        Assert.Contains(evidence.Filings, f => f.AccessionNumber == "past");
        Assert.DoesNotContain(evidence.Filings, f => f.AccessionNumber == "future");
        Assert.Contains(evidence.News, n => n.Id == "n1");
        Assert.DoesNotContain(evidence.News, n => n.Id == "n2");
        Assert.NotEmpty(evidence.Reaction);
    }

    [Fact]
    public async Task GetMoves_SocialFailure_MarksLayerUnavailable()
    {
        var (db, av, directory) = BuildDb();
        await SeedSpike(db);
        var sut = Sut(db, av, directory,
            new NullNewsProvider(NullLogger<NullNewsProvider>.Instance),
            new[] { new ThrowingSocialProvider() });

        var window = await sut.GetMoves("TSLA", new DateOnly(2020, 2, 20));

        Assert.NotEmpty(window.KeyMoves);
        var evidence = window.EvidenceByDate.Values.First();
        Assert.Contains("social", evidence.UnavailableLayers);
    }
}
