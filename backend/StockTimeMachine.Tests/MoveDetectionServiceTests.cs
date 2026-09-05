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
    public async Task GetMoves_AttachesValidRegimes()
    {
        var (db, av, directory) = BuildDb();
        await SeedSpike(db);
        var sut = Sut(db, av, directory, new NullNewsProvider(NullLogger<NullNewsProvider>.Instance));

        var window = await sut.GetMoves("TSLA", new DateOnly(2020, 2, 20));

        var valid = new[] { "calm", "normal", "tense", "warming" };
        Assert.NotEmpty(window.Regimes);
        Assert.All(window.Regimes.Values, v => Assert.Contains(v, valid));
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

    private sealed class CapturingSocialProvider : ISocialSignalProvider
    {
        public string ProviderName => "Capturing";
        public DateOnly SeenFrom { get; private set; }
        public Task<IReadOnlyList<SocialSignal>> GetSignals(string symbol, string? companyName, DateOnly from, DateOnly to, CancellationToken ct = default)
        {
            SeenFrom = from;
            return Task.FromResult<IReadOnlyList<SocialSignal>>(new[]
            {
                new SocialSignal { Id = "s5", Title = "Five days before", CreatedAt = new DateTime(2020, 1, 27, 12, 0, 0, DateTimeKind.Utc), Score = 10, Url = "https://example.com/s5", CompanySymbol = "TSLA" },
                new SocialSignal { Id = "s10", Title = "Ten days before", CreatedAt = new DateTime(2020, 1, 22, 12, 0, 0, DateTimeKind.Utc), Score = 99, Url = "https://example.com/s10", CompanySymbol = "TSLA" },
            });
        }
    }

    [Fact]
    public async Task GetMoves_SocialWindow_CoversSevenDays()
    {
        var (db, av, directory) = BuildDb();
        await SeedSpike(db);
        var social = new CapturingSocialProvider();
        var sut = Sut(db, av, directory,
            new NullNewsProvider(NullLogger<NullNewsProvider>.Instance),
            new[] { social });

        var window = await sut.GetMoves("TSLA", new DateOnly(2020, 2, 20));

        // Move is Feb-1: fetch starts 7 days back; the 5-day-old post attaches
        // despite outscoring nothing, the 10-day-old post stays out even with
        // the highest score.
        Assert.Equal(new DateOnly(2020, 1, 25), social.SeenFrom);
        var evidence = window.EvidenceByDate.Values.First();
        Assert.Contains(evidence.Social, s => s.Id == "s5");
        Assert.DoesNotContain(evidence.Social, s => s.Id == "s10");
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
    public async Task GetMoves_StaleNewsCache_RefreshesOncePerInvestigation()
    {
        var (db, av, directory) = BuildDb();
        await SeedSpike(db);
        // Cached row Jan-20 vs Feb-1 move: older than 7 days → stale.
        await db.NewsArticles.AddAsync(new NewsArticle
        {
            Id = "old", Title = "Old news", Source = "GDELT",
            PublishedAt = new DateTime(2020, 1, 20, 0, 0, 0, DateTimeKind.Utc),
            Url = "https://example.com/old", CompanySymbol = "TSLA",
        });
        await db.SaveChangesAsync();
        var news = new CountingNewsProvider(new[]
        {
            new NewsArticle { Id = "fresh", Title = "Fresh news", Source = "GDELT", PublishedAt = new DateTime(2020, 1, 30, 0, 0, 0, DateTimeKind.Utc), Url = "https://example.com/fresh", CompanySymbol = "TSLA" },
        });
        var sut = Sut(db, av, directory, news);

        var window = await sut.GetMoves("TSLA", new DateOnly(2020, 2, 20));

        // One refresh for the whole investigation despite several moves.
        Assert.Equal(1, news.Calls);
        var evidence = window.EvidenceByDate.Values.First();
        Assert.Contains(evidence.News, n => n.Id == "fresh");
    }

    [Fact]
    public async Task GetMoves_FreshNewsCache_SkipsRefresh()
    {
        var (db, av, directory) = BuildDb();
        await SeedSpike(db);
        // Detected moves fall in [Jan-22, Feb-10]. Jan-20 covers every move's
        // cutoff so the per-move empty fallback never fires; the Feb rows keep
        // the visible-newest within 7 days of any latest move, so the
        // staleness refresh stays quiet too.
        await db.NewsArticles.AddRangeAsync(
            new NewsArticle
            {
                Id = "early", Title = "Early news", Source = "GDELT",
                PublishedAt = new DateTime(2020, 1, 20, 0, 0, 0, DateTimeKind.Utc),
                Url = "https://example.com/early", CompanySymbol = "TSLA",
            },
            new NewsArticle
            {
                Id = "mid", Title = "Mid news", Source = "GDELT",
                PublishedAt = new DateTime(2020, 2, 2, 0, 0, 0, DateTimeKind.Utc),
                Url = "https://example.com/mid", CompanySymbol = "TSLA",
            },
            new NewsArticle
            {
                Id = "recent", Title = "Recent news", Source = "GDELT",
                PublishedAt = new DateTime(2020, 2, 8, 0, 0, 0, DateTimeKind.Utc),
                Url = "https://example.com/recent", CompanySymbol = "TSLA",
            });
        await db.SaveChangesAsync();
        var news = new CountingNewsProvider(Array.Empty<NewsArticle>());
        var sut = Sut(db, av, directory, news);

        await sut.GetMoves("TSLA", new DateOnly(2020, 2, 20));

        Assert.Equal(0, news.Calls);
    }

    private sealed class ThrowingAfterRepository : IHistoricalDataRepository
    {
        private readonly IHistoricalDataRepository _inner;
        public ThrowingAfterRepository(IHistoricalDataRepository inner) => _inner = inner;
        public Task<IReadOnlyList<NewsArticle>> GetNewsAsOf(string s, DateOnly d, CancellationToken ct = default) => _inner.GetNewsAsOf(s, d, ct);
        public Task<IReadOnlyList<NewsArticle>> GetNewsAsOf(string s, DateOnly d, string? source, CancellationToken ct = default) => _inner.GetNewsAsOf(s, d, source, ct);
        public Task<IReadOnlyList<SecFiling>> GetFilingsAsOf(string s, DateOnly d, CancellationToken ct = default) => _inner.GetFilingsAsOf(s, d, ct);
        public Task<IReadOnlyList<PricePoint>> GetPricesAsOf(string s, DateOnly d, int days = 30, CancellationToken ct = default) => _inner.GetPricesAsOf(s, d, days, ct);
        public Task<IReadOnlyList<PricePoint>> GetPriceRange(string s, DateOnly f, DateOnly t, CancellationToken ct = default) => _inner.GetPriceRange(s, f, t, ct);
        public Task<IReadOnlyList<PricePoint>> GetPricesAfter(string s, DateOnly d, int days = 30, CancellationToken ct = default) =>
            throw new InvalidOperationException("prices-after down");
        public Task<IReadOnlyList<SecFiling>> GetFilingsAfter(string s, DateOnly d, int days = 30, CancellationToken ct = default) => _inner.GetFilingsAfter(s, d, days, ct);
        public Task<ArticleEmbedding?> GetEmbedding(string id, string model, CancellationToken ct = default) => _inner.GetEmbedding(id, model, ct);
        public Task StoreEmbedding(ArticleEmbedding e, CancellationToken ct = default) => _inner.StoreEmbedding(e, ct);
        public Task StorePrices(string s, IEnumerable<PricePoint> p, CancellationToken ct = default) => _inner.StorePrices(s, p, ct);
        public Task StoreNews(string s, IEnumerable<NewsArticle> n, CancellationToken ct = default) => _inner.StoreNews(s, n, ct);
        public Task StoreFilings(string s, IEnumerable<SecFiling> f, CancellationToken ct = default) => _inner.StoreFilings(s, f, ct);
    }

    [Fact]
    public async Task GetMoves_ReactionFailure_MarksLayerUnavailable()
    {
        var (db, av, directory) = BuildDb();
        await SeedSpike(db);
        var throwing = new ThrowingAfterRepository(
            new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance));
        var sut = new MoveDetectionService(
            new CompanyRepository(db, NullLogger<CompanyRepository>.Instance),
            throwing, av.Object, directory,
            new FixedNewsProviderFactory(new NullNewsProvider(NullLogger<NullNewsProvider>.Instance)),
            Array.Empty<ISocialSignalProvider>(),
            NullLogger<MoveDetectionService>.Instance);

        var window = await sut.GetMoves("TSLA", new DateOnly(2020, 2, 20));

        Assert.NotEmpty(window.KeyMoves);
        var evidence = window.EvidenceByDate.Values.First();
        Assert.Contains("reaction", evidence.UnavailableLayers);
        Assert.Empty(evidence.Reaction);
    }

    private sealed class ThrottlingNewsProvider : INewsProvider
    {
        public int Calls { get; private set; }
        public Task<IReadOnlyList<NewsArticle>> SearchAsync(string symbol, DateOnly cutoffDate, CancellationToken ct = default) =>
            throw new RateLimitExceededException("throttled");
        public Task<IReadOnlyList<NewsArticle>> SearchAsync(string symbol, string? companyName, DateOnly cutoffDate, CancellationToken ct = default)
        {
            Calls++;
            throw new RateLimitExceededException("throttled");
        }
    }

    [Fact]
    public async Task GetMoves_Progress_ReportsDetectionAndEvidence()
    {
        var (db, av, directory) = BuildDb();
        await SeedSpike(db);
        var sut = Sut(db, av, directory,
            new NullNewsProvider(NullLogger<NullNewsProvider>.Instance));
        var stages = new List<SnapshotProgress>();
        var progress = new Progress<SnapshotProgress>(s => { lock (stages) stages.Add(s); });

        var window = await sut.GetMoves("TSLA", new DateOnly(2020, 2, 20), progress: progress);

        Assert.NotEmpty(window.KeyMoves);
        for (int i = 0; i < 50; i++)
        {
            lock (stages)
            {
                if (stages.Any(s => s.Stage == "evidence" && s.State == "complete"))
                    break;
            }
            await Task.Delay(100);
        }
        Assert.Contains(stages, s => s.Stage == "detecting" && s.State == "started");
        Assert.Contains(stages, s => s.Stage == "detecting" && s.State == "complete");
        Assert.Equal(window.KeyMoves.Count,
            stages.Count(s => s.Stage == "evidence" && s.State == "started"));
        Assert.Contains(stages, s => s.Stage == "evidence" && s.State == "complete");
    }

    [Fact]
    public async Task GetMoves_ProviderFailure_FetchesOncePerInvestigation()
    {
        var (db, av, directory) = BuildDb();
        await SeedSpike(db);
        var news = new ThrottlingNewsProvider();
        var sut = Sut(db, av, directory, news);

        var window = await sut.GetMoves("TSLA", new DateOnly(2020, 2, 20));

        // One failed fetch per investigation — later moves must not re-hammer
        // a throttled provider seconds later.
        Assert.Equal(1, news.Calls);
        Assert.NotEmpty(window.KeyMoves);
        Assert.All(window.EvidenceByDate.Values,
            e => Assert.Contains("news", e.UnavailableLayers));
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
