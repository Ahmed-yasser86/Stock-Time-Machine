using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using StockTimeMachine;

namespace StockTimeMachine.Tests;

// Documents US-28 throughout: every test calls TimeMachineService directly,
// with no controller, no HttpContext, and no web infrastructure.
public class InvestigationBehaviorTests
{
    private sealed class StubNewsProvider : INewsProvider
    {
        private readonly IReadOnlyList<NewsArticle> _articles;
        public int Calls { get; private set; }
        public StubNewsProvider(IReadOnlyList<NewsArticle> articles) => _articles = articles;
        public Task<IReadOnlyList<NewsArticle>> SearchAsync(string symbol, DateOnly cutoffDate, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(_articles);
        }
    }

    private sealed class MapNewsFactory : INewsProviderFactory
    {
        private readonly Dictionary<string, INewsProvider> _map;
        public MapNewsFactory(Dictionary<string, INewsProvider> map) => _map = map;
        public string DefaultSource => NewsSources.Gdelt;
        public INewsProvider Get(string? source) => _map[NewsSources.Normalize(source)];
    }

    private static (StockTimeMachineDbContext db, Mock<ISecEdgarProvider> sec, Mock<IAlphaVantageProvider> av) BuildDb()
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
        return (db, sec, av);
    }

    private static StubCompanyDirectory Directory() => new(
        new CompanyInfo("TSLA", "Tesla, Inc.", "0001318605", "NASDAQ", "Consumer Discretionary", "Automobiles"));

    private static TimeMachineService Sut(
        StockTimeMachineDbContext db, Mock<ISecEdgarProvider> sec, Mock<IAlphaVantageProvider> av, INewsProviderFactory news) =>
        new(new CompanyRepository(db, NullLogger<CompanyRepository>.Instance),
            new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
            sec.Object, av.Object, Directory(),
            Array.Empty<ICompanyLookup>(), news,
            NullLogger<TimeMachineService>.Instance);

    [Fact]
    public void SnapshotSections_Parse_NullOrEmptyMeansAll()
    {
        Assert.Null(SnapshotSections.Parse(null));
        Assert.Null(SnapshotSections.Parse(""));
        Assert.Null(SnapshotSections.Parse("  "));
    }

    [Fact]
    public void SnapshotSections_Parse_AcceptsKnownKeysCaseInsensitively()
    {
        var sections = SnapshotSections.Parse("Prices,NEWS");
        Assert.NotNull(sections);
        Assert.True(SnapshotSections.Includes(sections, SnapshotSections.Prices));
        Assert.True(SnapshotSections.Includes(sections, SnapshotSections.News));
        Assert.False(SnapshotSections.Includes(sections, SnapshotSections.Filings));
        Assert.True(SnapshotSections.Includes(null, SnapshotSections.Filings));
    }

    [Fact]
    public void SnapshotSections_Parse_UnknownKeyThrows()
    {
        var ex = Assert.Throws<InvalidHistoricalDateException>(() => SnapshotSections.Parse("prices,bogus"));
        Assert.Contains("bogus", ex.Message);
        Assert.Contains("filings", ex.Message);
    }

    [Fact]
    public async Task GetSnapshot_RescopedToPrices_SkipsOtherSectionsEntirely()
    {
        var (db, sec, av) = BuildDb();
        await db.PricePoints.AddAsync(new PricePoint
        {
            CompanySymbol = "TSLA", Date = new DateOnly(2020, 1, 15),
            Open = 99m, High = 101m, Low = 98m, Close = 100m, Volume = 1000
        });
        await db.SaveChangesAsync();
        var stub = new StubNewsProvider(Array.Empty<NewsArticle>());
        var sut = Sut(db, sec, av, new FixedNewsProviderFactory(stub));

        var snapshot = await sut.GetSnapshot("TSLA", new DateOnly(2020, 1, 15), NewsSources.Gdelt,
            new HashSet<string>(StringComparer.Ordinal) { SnapshotSections.Prices });

        Assert.True(snapshot.HasMarketData);
        Assert.Empty(snapshot.RecentFilings);
        Assert.Empty(snapshot.RecentNews);
        Assert.Empty(snapshot.OutcomePrices);
        Assert.Empty(snapshot.FailedSections);
        sec.Verify(x => x.GetCompanyFilings(It.IsAny<string>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()), Times.Never);
        av.Verify(x => x.GetDailyPrices(It.IsAny<string>(), It.IsAny<DateOnly?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(0, stub.Calls);
    }

    private sealed class CaptureProgress : IProgress<SnapshotProgress>
    {
        public readonly List<SnapshotProgress> Events = new();
        public void Report(SnapshotProgress value) => Events.Add(value);
    }

    [Fact]
    public async Task GetSnapshot_ReportsHonestStageProgress()
    {
        var (db, sec, av) = BuildDb();
        await db.PricePoints.AddAsync(new PricePoint
        {
            CompanySymbol = "TSLA", Date = new DateOnly(2020, 1, 15),
            Open = 99m, High = 101m, Low = 98m, Close = 100m, Volume = 1000
        });
        await db.SaveChangesAsync();
        var sut = Sut(db, sec, av, new FixedNewsProviderFactory(new StubNewsProvider(Array.Empty<NewsArticle>())));
        var progress = new CaptureProgress();

        await sut.GetSnapshot("TSLA", new DateOnly(2020, 1, 15), NewsSources.Gdelt, null, progress);

        string State(string stage) => progress.Events.Last(e => e.Stage == stage).State;
        Assert.Equal(SnapshotProgress.Complete, State(SnapshotStages.Company));
        Assert.Equal(SnapshotProgress.Complete, State(SnapshotStages.Prices));
        Assert.Equal(SnapshotProgress.Complete, State(SnapshotStages.Boundary));
        Assert.Equal(SnapshotProgress.Complete, State(SnapshotStages.Assembly));
        // No false successes: every reported terminal state is complete here.
        Assert.DoesNotContain(progress.Events, e =>
            e.State == SnapshotProgress.Failed && e.Stage != SnapshotStages.Outcome);
    }

    [Fact]
    public async Task GetSnapshot_Rescoped_ReportsSkippedStages()
    {
        var (db, sec, av) = BuildDb();
        var sut = Sut(db, sec, av, new FixedNewsProviderFactory(new StubNewsProvider(Array.Empty<NewsArticle>())));
        var progress = new CaptureProgress();

        await sut.GetSnapshot("TSLA", new DateOnly(2020, 1, 15), NewsSources.Gdelt,
            new HashSet<string>(StringComparer.Ordinal) { SnapshotSections.Prices }, progress);

        string State(string stage) => progress.Events.Last(e => e.Stage == stage).State;
        Assert.Equal(SnapshotProgress.Complete, State(SnapshotStages.Prices));
        Assert.Equal(SnapshotProgress.Skipped, State(SnapshotStages.Filings));
        Assert.Equal(SnapshotProgress.Skipped, State(SnapshotStages.News));
        Assert.Equal(SnapshotProgress.Skipped, State(SnapshotStages.Outcome));
    }

    [Fact]
    public async Task GetSnapshot_FailedSection_ReportsFailedStage()
    {
        var (db, sec, av) = BuildDb();
        await db.Companies.AddAsync(new Company { Symbol = "TSLA", Name = "Tesla, Inc.", Cik = "0001318605" });
        await db.SaveChangesAsync();
        av.Setup(x => x.GetDailyPrices(It.IsAny<string>(), It.IsAny<DateOnly?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ExternalProviderException("Provider down"));
        var sut = Sut(db, sec, av, new FixedNewsProviderFactory(new StubNewsProvider(Array.Empty<NewsArticle>())));
        var progress = new CaptureProgress();

        await sut.GetSnapshot("TSLA", new DateOnly(2020, 1, 15), NewsSources.Gdelt, null, progress);

        string State(string stage) => progress.Events.Last(e => e.Stage == stage).State;
        Assert.Equal(SnapshotProgress.Failed, State(SnapshotStages.Prices));
    }

    [Fact]
    public void NewsSources_Normalize_DefaultsToGdelt()
    {
        Assert.Equal(NewsSources.Gdelt, NewsSources.Normalize(null));
        Assert.Equal(NewsSources.Gdelt, NewsSources.Normalize(""));
        Assert.Equal(NewsSources.Gdelt, NewsSources.Normalize("unknown-provider"));
        Assert.Equal(NewsSources.AlphaVantage, NewsSources.Normalize("alphavantage"));
        Assert.Equal(NewsSources.AlphaVantage, NewsSources.Normalize("AlphaVantage"));
    }

    [Fact]
    public void SecFiling_MaterialDisclosure_OnlyEightK()
    {
        Assert.True(new SecFiling { FormType = "8-K" }.IsMaterialDisclosure);
        Assert.True(new SecFiling { FormType = "8-k/a" }.IsMaterialDisclosure);
        Assert.False(new SecFiling { FormType = "10-K" }.IsMaterialDisclosure);
        Assert.False(new SecFiling { FormType = "10-Q" }.IsMaterialDisclosure);
    }

    [Fact]
    public void TemporalBoundary_HandlesEstAndEdt()
    {
        // Jan 15 2020 23:59:59 EST (UTC-5) -> Jan 16 04:59:59 UTC.
        Assert.Equal(new DateTime(2020, 1, 16, 4, 59, 59, DateTimeKind.Utc),
            TemporalBoundary.GetCutoffUtc(new DateOnly(2020, 1, 15)));
        // Jul 15 2020 23:59:59 EDT (UTC-4) -> Jul 16 03:59:59 UTC.
        Assert.Equal(new DateTime(2020, 7, 16, 3, 59, 59, DateTimeKind.Utc),
            TemporalBoundary.GetCutoffUtc(new DateOnly(2020, 7, 15)));
    }

    [Fact]
    public async Task GetSnapshot_UsesSelectedNewsSource_AndNeverMixes()
    {
        var (db, sec, av) = BuildDb();
        var gdeltArticle = new NewsArticle
        {
            Id = "gdelt-1", Title = "GDELT story", Source = "GDELT Project",
            PublishedAt = new DateTime(2020, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            Url = "https://example.com/g", CompanySymbol = "TSLA"
        };
        var avArticle = new NewsArticle
        {
            Id = "avnews-1", Title = "AV story", Source = "Alpha Vantage",
            PublishedAt = new DateTime(2020, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            Url = "https://example.com/a", CompanySymbol = "TSLA"
        };
        var factory = new MapNewsFactory(new Dictionary<string, INewsProvider>
        {
            [NewsSources.Gdelt] = new StubNewsProvider(new[] { gdeltArticle }),
            [NewsSources.AlphaVantage] = new StubNewsProvider(new[] { avArticle }),
        });
        var sut = Sut(db, sec, av, factory);

        var gdeltSnap = await sut.GetSnapshot("TSLA", new DateOnly(2020, 1, 15), NewsSources.Gdelt);
        var avSnap = await sut.GetSnapshot("TSLA", new DateOnly(2020, 1, 15), NewsSources.AlphaVantage);

        Assert.Equal(NewsSources.Gdelt, gdeltSnap.NewsSource);
        Assert.Equal("GDELT story", Assert.Single(gdeltSnap.RecentNews).Title);
        Assert.Equal(NewsSources.AlphaVantage, avSnap.NewsSource);
        Assert.Equal("AV story", Assert.Single(avSnap.RecentNews).Title);
    }

    [Fact]
    public async Task GetSnapshot_FutureNewsFromProvider_IsExcludedByService()
    {
        var (db, sec, av) = BuildDb();
        var future = new NewsArticle
        {
            Id = "gdelt-x", Title = "Leaked future", Source = "GDELT Project",
            PublishedAt = new DateTime(2020, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            Url = "https://example.com/x", CompanySymbol = "TSLA"
        };
        var sut = Sut(db, sec, av, new FixedNewsProviderFactory(new StubNewsProvider(new[] { future })));

        var snapshot = await sut.GetSnapshot("TSLA", new DateOnly(2020, 1, 15));

        Assert.Empty(snapshot.RecentNews);
    }

    [Fact]
    public async Task GetSnapshot_WeekendDate_SetsPriceDateBehindSnapshotDate()
    {
        var (db, sec, av) = BuildDb();
        // Saturday Jan 18 2020: no Friday data in DB missing; Friday Jan 17 present.
        await db.PricePoints.AddAsync(new PricePoint
        {
            CompanySymbol = "TSLA", Date = new DateOnly(2020, 1, 17),
            Open = 99m, High = 101m, Low = 98m, Close = 100m, Volume = 1000
        });
        await db.SaveChangesAsync();
        var sut = Sut(db, sec, av, new FixedNewsProviderFactory(new StubNewsProvider(Array.Empty<NewsArticle>())));

        var snapshot = await sut.GetSnapshot("TSLA", new DateOnly(2020, 1, 18));

        Assert.True(snapshot.HasMarketData);
        Assert.Equal(new DateOnly(2020, 1, 17), snapshot.PriceDate);
    }

    [Fact]
    public async Task GetSnapshot_CachesNewsInDatabase()
    {
        var (db, sec, av) = BuildDb();
        var article = new NewsArticle
        {
            Id = GdeltNewsProvider.DeterministicId("https://example.com/cached"),
            Title = "Cached story", Source = "GDELT Project",
            PublishedAt = new DateTime(2020, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            Url = "https://example.com/cached", CompanySymbol = "TSLA"
        };
        var stub = new StubNewsProvider(new[] { article });
        var sut = Sut(db, sec, av, new FixedNewsProviderFactory(stub, NewsSources.Gdelt));

        await sut.GetSnapshot("TSLA", new DateOnly(2020, 1, 15));
        await sut.GetSnapshot("TSLA", new DateOnly(2020, 1, 15));

        Assert.Equal(1, stub.Calls);
        Assert.Equal(1, await db.NewsArticles.CountAsync());
    }

    [Fact]
    public async Task GetSnapshot_NextDayFiling_ExcludedFromHistory_IncludedInOutcome()
    {
        var (db, sec, av) = BuildDb();
        await db.Companies.AddAsync(new Company { Symbol = "TSLA", Name = "Tesla, Inc.", Cik = "0001318605" });
        await db.SecFilings.AddRangeAsync(
            // Midnight UTC of Jan 16 = evening of Jan 15 ET: must NOT leak into Jan 15 history.
            new SecFiling { CompanySymbol = "TSLA", FormType = "8-K", FiledAt = new DateTime(2020, 1, 16, 0, 0, 0, DateTimeKind.Utc), AccessionNumber = "next-day", Url = "https://example.com/n" },
            new SecFiling { CompanySymbol = "TSLA", FormType = "10-K", FiledAt = new DateTime(2020, 1, 10, 0, 0, 0, DateTimeKind.Utc), AccessionNumber = "hist", Url = "https://example.com/h" },
            // Beyond the 30-day outcome window.
            new SecFiling { CompanySymbol = "TSLA", FormType = "10-Q", FiledAt = new DateTime(2020, 6, 1, 0, 0, 0, DateTimeKind.Utc), AccessionNumber = "far", Url = "https://example.com/f" });
        await db.SaveChangesAsync();
        var sut = Sut(db, sec, av, new FixedNewsProviderFactory(new StubNewsProvider(Array.Empty<NewsArticle>())));

        var snapshot = await sut.GetSnapshot("TSLA", new DateOnly(2020, 1, 15));

        Assert.Single(snapshot.RecentFilings);
        Assert.Equal("hist", snapshot.RecentFilings[0].AccessionNumber);
        Assert.Single(snapshot.OutcomeFilings);
        Assert.Equal("next-day", snapshot.OutcomeFilings[0].AccessionNumber);
    }

    [Fact]
    public void NewsProviderFactory_SelectsConfiguredProviders()
    {
        IConfiguration Config(string? def) =>
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["News:DefaultSource"] = def
            }).Build();

        var gdelt = new GdeltNewsProvider(new HttpClient(), NullLogger<GdeltNewsProvider>.Instance, Config(null));
        var gdeltCloud = new GdeltCloudNewsProvider(new HttpClient(), NullLogger<GdeltCloudNewsProvider>.Instance, Config(null));
        var avNews = new AlphaVantageNewsProvider(new HttpClient(), NullLogger<AlphaVantageNewsProvider>.Instance, Config(null));
        var factory = new NewsProviderFactory(gdelt, gdeltCloud, avNews, Config("alphavantage"));

        Assert.Same(avNews, factory.Get("alphavantage"));
        // No Cloud key in test config: "gdelt" falls back to the Project provider.
        Assert.Same(gdelt, factory.Get("gdelt"));
        Assert.Same(gdelt, factory.Get("bogus"));
        Assert.Equal(NewsSources.AlphaVantage, factory.DefaultSource);

        var cloudConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gdelt:ApiKey"] = "test-key",
            ["News:DefaultSource"] = "gdelt"
        }).Build();
        var cloudFactory = new NewsProviderFactory(gdelt,
            new GdeltCloudNewsProvider(new HttpClient(), NullLogger<GdeltCloudNewsProvider>.Instance, cloudConfig),
            avNews, cloudConfig);
        // Key present: "gdelt" resolves to authenticated Cloud transport.
        Assert.IsType<GdeltCloudNewsProvider>(cloudFactory.Get("gdelt"));
        Assert.IsType<GdeltCloudNewsProvider>(cloudFactory.Default());
    }

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new TaskCanceledException("Simulated provider timeout.");
    }

    [Fact]
    public async Task GdeltNewsProvider_Timeout_DegradesToEmpty()
    {
        var config = new ConfigurationBuilder().Build();
        var provider = new GdeltNewsProvider(
            new HttpClient(new TimeoutHandler()),
            NullLogger<GdeltNewsProvider>.Instance, config);

        var result = await provider.SearchAsync("TSLA", new DateOnly(2020, 1, 15));

        Assert.Empty(result);
    }

    [Fact]
    public async Task GdeltNewsProvider_RequestCancellation_StillPropagates()
    {
        var config = new ConfigurationBuilder().Build();
        var provider = new GdeltNewsProvider(
            new HttpClient(new TimeoutHandler()),
            NullLogger<GdeltNewsProvider>.Instance, config);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.SearchAsync("TSLA", new DateOnly(2020, 1, 15), cts.Token));
    }

    [Fact]
    public async Task GdeltNewsProvider_SkipsBadAndFutureDates()
    {
        var json = """
        {
          "articles": [
            { "title": "Good", "url": "https://example.com/good", "published_date": "2020-01-10 12:00:00", "domain": "example.com" },
            { "title": "Bad date", "url": "https://example.com/bad", "published_date": "not-a-date", "domain": "example.com" },
            { "title": "Future", "url": "https://example.com/future", "published_date": "2020-02-01 12:00:00", "domain": "example.com" }
          ]
        }
        """;
        var config = new ConfigurationBuilder().Build();
        var provider = new GdeltNewsProvider(
            new HttpClient(new StubHttpMessageHandler(json)),
            NullLogger<GdeltNewsProvider>.Instance, config);

        var result = await provider.SearchAsync("TSLA", new DateOnly(2020, 1, 15));

        var single = Assert.Single(result);
        Assert.Equal("Good", single.Title);
        Assert.Equal("TSLA", single.CompanySymbol);
        Assert.False(string.IsNullOrEmpty(single.Id));
    }
}
