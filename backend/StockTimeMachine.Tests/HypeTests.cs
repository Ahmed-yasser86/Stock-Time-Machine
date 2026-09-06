using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using StockTimeMachine;

namespace StockTimeMachine.Tests;

// Layer 2 validation: projection validation/completeness, deterministic
// triggers (one positive + one negative per signal), library failure
// honesty, and registry round-trips. Mirrors DecisionContextTests (pure
// fixtures) + RepositoryTests (InMemory store).
public class HypeTests
{
    private static readonly DateOnly Peak = new(2026, 6, 15);

    private static KeyMove Move(params string[] flags) => new()
    {
        Date = Peak,
        Close = 100m,
        DailyReturnPct = 5m,
        ZScore = 2.5,
        VolumeRatio = 3,
        FiveDayMomentumPct = 6m,
        Score = 0.8,
        Flags = flags.ToList(),
        SentimentDirection = SentimentDivergence.Unknown,
    };

    private static int _threadSeq;
    private static TopicCluster Thread(string category, string title, DateTime? start, DateTime? end) => new()
    {
        LabelTerms = title.Split(' ').Take(3).ToList(),
        ArticleIds = new List<string> { "a-" + Interlocked.Increment(ref _threadSeq) + "-" + category },
        RepresentativeTitle = title,
        SpanStart = start,
        SpanEnd = end,
        RelevanceRate = 1.0,
        TopCategory = category,
    };

    private static MovesWindow Window(KeyMove move, Dictionary<string, string>? regimes = null, MoveEvidence? evidence = null)
    {
        var window = new MovesWindow
        {
            CompanySymbol = "NFLX",
            DecisionDate = Peak,
            NewsSource = NewsSources.Gdelt,
            Summary = new WindowSummary { TradingDays = 100, SufficientHistory = true },
            KeyMoves = new List<KeyMove> { move },
            Regimes = regimes ?? new Dictionary<string, string>(),
            Uncertainty = new UncertaintyIndex { Score = 55, Confidence = "Medium" },
        };
        window.EvidenceByDate[Peak.ToString("yyyy-MM-dd")] = evidence ?? new MoveEvidence();
        return window;
    }

    private static NarrativeTopicsResult Topics(params TopicCluster[] threads) => new()
    {
        CompanySymbol = "NFLX",
        AsOfDate = Peak,
        NewsSource = NewsSources.Gdelt,
        ArticlesConsidered = threads.Length,
        Topics = threads.ToList(),
    };

    private static HypeCaseDetail Detail(KeyMove move, NarrativeTopicsResult? topics, MovesWindow? window = null) =>
        HypeCaseLibrary.TryReadDetail(
            HypeCaseProjection.Build(window ?? Window(move), move, topics))!;

    [Fact]
    public void Projection_NullInputs_Throw()
    {
        var window = Window(Move());
        Assert.Throws<ArgumentNullException>(() => HypeCaseProjection.Build(null!, Move(), null));
        Assert.Throws<ArgumentNullException>(() => HypeCaseProjection.Build(window, null!, null));
    }

    [Fact]
    public void Projection_EmptyInputs_MarkedMissingNeverThrows()
    {
        // Present-but-empty evidence reads partial; a fully absent evidence
        // entry reads missing. Neither throws, neither fires signals.
        var partial = HypeCaseProjection.Build(Window(Move()), Move(), null);
        Assert.Equal("NFLX:2026-06-15", partial.Id);
        Assert.Equal(HypeCompleteness.Partial, partial.Completeness);

        var window = Window(Move());
        window.EvidenceByDate.Clear();
        var missing = HypeCaseProjection.Build(window, Move(), null);
        Assert.Equal(HypeCompleteness.Missing, missing.Completeness);

        var detail = HypeCaseLibrary.TryReadDetail(missing);
        Assert.NotNull(detail);
        Assert.Empty(detail!.PrePeakThreads);
        Assert.Empty(HypeSignals.Evaluate(detail));
    }

    [Fact]
    public void Projection_PrePeakFilter_And_Determinism()
    {
        var move = Move(MoveFlags.Spike);
        var inWindow = Thread("FINANCIAL", "Earnings beat again", new DateTime(2026, 6, 10), new DateTime(2026, 6, 12));
        var tooOld = Thread("FINANCIAL", "Old earnings story", new DateTime(2026, 4, 1), new DateTime(2026, 4, 2));
        var topics = Topics(inWindow, tooOld);

        var first = HypeCaseProjection.Build(Window(move), move, topics);
        var second = HypeCaseProjection.Build(Window(move), move, topics);

        Assert.Equal(first.CaseJson, second.CaseJson);
        var detail = HypeCaseLibrary.TryReadDetail(first)!;
        Assert.Single(detail.PrePeakThreads);
        Assert.Equal("Earnings beat again", detail.PrePeakThreads[0].RepresentativeTitle);
    }

    [Fact]
    public void Signal_EarningsChatter_FiresOnTwoFinancialThreads()
    {
        var d = new DateTime(2026, 6, 10);
        var detail = Detail(Move(), Topics(
            Thread("FINANCIAL", "Earnings beat estimates", d, d),
            Thread("FINANCIAL", "Revenue guidance raised", d, d),
            Thread("PRODUCT", "New show launches", d, d)));

        var match = HypeSignals.Evaluate(detail).SingleOrDefault(m => m.SignalId == "earnings-chatter");

        Assert.NotNull(match);
        Assert.Equal(2, match!.TriggerEvidence.Count);
        Assert.Equal(2, match.TriggerThreadIds.Count);
    }

    [Fact]
    public void Signal_EarningsChatter_SilentOnOneThread()
    {
        var d = new DateTime(2026, 6, 10);
        var detail = Detail(Move(), Topics(Thread("FINANCIAL", "Earnings beat estimates", d, d)));

        Assert.DoesNotContain(HypeSignals.Evaluate(detail), m => m.SignalId == "earnings-chatter");
    }

    [Fact]
    public void Signal_RegulatoryOverhang_NeedsThreadPlusTenseRegime()
    {
        var d = new DateTime(2026, 6, 10);
        var regimes = new Dictionary<string, string>
        {
            ["2026-06-10"] = MarketRegimes.Tense,
            ["2026-06-11"] = MarketRegimes.Tense,
            ["2026-06-12"] = MarketRegimes.Tense,
        };
        var detail = Detail(Move(), Topics(Thread("REGULATORY", "FCC proposes streaming rules", d, d)),
            Window(Move(), regimes));

        var match = HypeSignals.Evaluate(detail).SingleOrDefault(m => m.SignalId == "regulatory-overhang");

        Assert.NotNull(match);
        Assert.Contains(match!.TriggerEvidence, e => e.Contains("tense regime on 3 pre-peak days"));
    }

    [Fact]
    public void Signal_RegulatoryOverhang_SilentWithoutTenseDays()
    {
        var d = new DateTime(2026, 6, 10);
        var regimes = new Dictionary<string, string> { ["2026-06-10"] = MarketRegimes.Calm };
        var detail = Detail(Move(), Topics(Thread("LEGAL", "Studio sues streamer", d, d)),
            Window(Move(), regimes));

        Assert.DoesNotContain(HypeSignals.Evaluate(detail), m => m.SignalId == "regulatory-overhang");
    }

    [Fact]
    public void Signal_VolumeFirstDivergence_ThinNarrative()
    {
        var move = Move(MoveFlags.Spike, MoveFlags.HighVolume);
        var evidence = new MoveEvidence
        {
            News = new List<NewsArticle>
            {
                new() { Id = "n1", Title = "Shares jump", PublishedAt = new DateTime(2026, 6, 14), Url = "https://example.com/n1", CompanySymbol = "NFLX" },
            },
        };
        var detail = Detail(move, Topics(), Window(move, evidence: evidence));

        var match = HypeSignals.Evaluate(detail).SingleOrDefault(m => m.SignalId == "volume-first-divergence");

        Assert.NotNull(match);
    }

    [Fact]
    public void Signal_VolumeFirstDivergence_BlockedWhenEvidenceMissing()
    {
        // A missing evidence layer must not read as "thin narrative".
        var move = Move(MoveFlags.Spike, MoveFlags.HighVolume);
        var window = Window(move);
        window.EvidenceByDate.Clear();
        var detail = Detail(move, Topics(), window);

        Assert.DoesNotContain(HypeSignals.Evaluate(detail), m => m.SignalId == "volume-first-divergence");
    }

    [Fact]
    public void Signal_LeadershipTurbulence_FiresOnManagementThread()
    {
        var d = new DateTime(2026, 6, 10);
        var detail = Detail(Move(), Topics(Thread("MANAGEMENT", "CEO steps down abruptly", d, d)));

        Assert.Contains(HypeSignals.Evaluate(detail), m => m.SignalId == "leadership-turbulence");
    }

    [Fact]
    public void Signal_SentimentSplit_FiresOnlyOnDisagree()
    {
        var agree = Move();
        agree.SentimentDirection = SentimentDivergence.Agree;
        Assert.DoesNotContain(HypeSignals.Evaluate(Detail(agree, Topics())), m => m.SignalId == "sentiment-split");

        var disagree = Move();
        disagree.SentimentDirection = SentimentDivergence.Disagree;
        Assert.Contains(HypeSignals.Evaluate(Detail(disagree, Topics())), m => m.SignalId == "sentiment-split");
    }

    [Fact]
    public void Signal_SupplyTremor_NeedsThreadPlusWarmingToTense()
    {
        var d = new DateTime(2026, 6, 10);
        var regimes = new Dictionary<string, string>
        {
            ["2026-06-09"] = MarketRegimes.Warming,
            ["2026-06-10"] = MarketRegimes.Normal,
            ["2026-06-12"] = MarketRegimes.Tense,
        };
        var detail = Detail(Move(), Topics(Thread("SUPPLY_CHAIN", "Chip shortage hits devices", d, d)),
            Window(Move(), regimes));

        Assert.Contains(HypeSignals.Evaluate(detail), m => m.SignalId == "supply-tremor");
    }

    [Fact]
    public void Signal_SupplyTremor_SilentWithoutShift()
    {
        var d = new DateTime(2026, 6, 10);
        var regimes = new Dictionary<string, string> { ["2026-06-10"] = MarketRegimes.Calm };
        var detail = Detail(Move(), Topics(Thread("SUPPLY_CHAIN", "Chip shortage hits devices", d, d)),
            Window(Move(), regimes));

        Assert.DoesNotContain(HypeSignals.Evaluate(detail), m => m.SignalId == "supply-tremor");
    }

    [Fact]
    public void Projection_CapturesExactStageText()
    {
        var move = Move(MoveFlags.Spike);
        var evidence = new MoveEvidence
        {
            News = new List<NewsArticle>
            {
                new() { Id = "n1", Title = "Shares jump", Description = "A long description that gets clipped at projection time. " + new string('x', 600), Source = "GDELT", PublishedAt = new DateTime(2026, 6, 14), Url = "https://example.com/n1", CompanySymbol = "NFLX" },
            },
            Filings = new List<SecFiling>
            {
                new() { AccessionNumber = "0001", FormType = "8-K", FiledAt = new DateTime(2026, 5, 8), Url = "https://sec.gov/1", CompanySymbol = "NFLX" },
            },
            Social = new List<SocialSignal>
            {
                new() { Id = "s1", Provider = "Arctic Shift", Community = "r/wallstreetbets", Title = "DD post", Excerpt = "excerpt", Url = "https://reddit.com/1", CreatedAt = new DateTime(2026, 6, 13), CompanySymbol = "NFLX" },
            },
            Reaction = new List<MarketReaction>
            {
                new() { Date = new DateOnly(2026, 6, 16), Close = 101m },
            },
            Arrival = new List<ArrivalEntry>
            {
                new() { Layer = "news", FirstSeen = new DateTime(2026, 6, 13), State = "observed", LagHours = 5.5, Detail = "2 article(s) published" },
            },
        };
        var detail = Detail(move, Topics(), Window(move, evidence: evidence));

        Assert.Single(detail.Evidence.News);
        Assert.Equal("Shares jump", detail.Evidence.News[0].Title);
        Assert.Equal(500, detail.Evidence.News[0].Description.Length);
        Assert.Single(detail.Evidence.Filings);
        Assert.Equal("8-K", detail.Evidence.Filings[0].FormType);
        Assert.Single(detail.Evidence.Social);
        Assert.Equal("r/wallstreetbets", detail.Evidence.Social[0].Community);
        Assert.Single(detail.Evidence.Arrival);
        Assert.Equal("news", detail.Evidence.Arrival[0].Layer);
    }

    [Fact]
    public void BriefPrompt_ContainsStagesBansAndCitations()
    {
        var move = Move(MoveFlags.Spike, MoveFlags.HighVolume);
        var detail = Detail(move, Topics(
            Thread("FINANCIAL", "Earnings beat estimates", new DateTime(2026, 6, 10), new DateTime(2026, 6, 10)),
            Thread("FINANCIAL", "Revenue guidance raised", new DateTime(2026, 6, 10), new DateTime(2026, 6, 10))));
        var match = HypeSignals.Evaluate(detail).First(m => m.SignalId == "earnings-chatter");
        var prompt = HypeBriefPrompt.Build("NFLX", Peak, match, detail,
            new List<(string Title, string Body)> { ("Earnings beat estimates", "Record quarter") });

        Assert.Contains("2026-06-15", prompt);
        Assert.Contains("CASE FACTS", prompt);
        Assert.Contains("NEVER state or imply", prompt);
        Assert.Contains("NEVER predict", prompt);
        Assert.Contains("[1] Earnings beat estimates", prompt);
        Assert.Contains("DISAGREEMENTS AND GAPS", prompt);
    }

    [Fact]
    public void Library_CorruptOrEmptyJson_ReturnsNull()
    {
        Assert.Null(HypeCaseLibrary.TryReadDetail(null!));
        Assert.Null(HypeCaseLibrary.TryReadDetail(new HypeCase { CaseJson = "" }));
        Assert.Null(HypeCaseLibrary.TryReadDetail(new HypeCase { CaseJson = "{not json" }));
    }

    [Fact]
    public async Task Store_SaveGetList_RoundTrip()
    {
        var options = new DbContextOptionsBuilder<StockTimeMachineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var db = new StockTimeMachineDbContext(options);
        var store = new HypeCaseStore(db, NullLogger<HypeCaseStore>.Instance);

        var saved = await store.SaveAsync(HypeCaseProjection.Build(Window(Move(MoveFlags.Spike)), Move(MoveFlags.Spike), null));

        Assert.Equal("NFLX:2026-06-15", saved.Id);
        var fetched = await store.GetAsync("nflx", Peak);
        Assert.NotNull(fetched);
        Assert.Equal(saved.Id, fetched!.Id);
        var listed = await store.ListBySymbolAsync("NFLX");
        Assert.Single(listed);
        Assert.Equal(1, await store.CountAsync());

        // Reinvestigation upserts instead of duplicating.
        await store.SaveAsync(HypeCaseProjection.Build(Window(Move(MoveFlags.Plunge)), Move(MoveFlags.Plunge), null));
        Assert.Equal(1, await store.CountAsync());
        Assert.Contains("plunge", (await store.GetAsync("NFLX", Peak))!.FlagsCsv);
    }

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static HypeCaseDetail ResemblanceDetail(params (string Id, string Title)[] articles) => new()
    {
        CompanySymbol = "NFLX",
        PeakDate = Peak,
        PrePeakThreads = new List<HypeCaseThread>
        {
            new()
            {
                LabelTerms = new List<string> { "streaming", "rules" },
                RepresentativeTitle = articles[0].Title,
                ArticleIds = articles.Select(a => a.Id).ToList(),
            },
        },
    };

    private static HypeCase ResemblanceRow(string id, HypeCaseDetail detail) => new()
    {
        Id = id,
        CompanySymbol = "NFLX",
        PeakDate = Peak,
        CaseJson = JsonSerializer.Serialize(detail, WebJson),
    };

    private static Mock<IHistoricalDataRepository> VectorRepo(Dictionary<string, float[]> vectors)
    {
        var repo = new Mock<IHistoricalDataRepository>();
        repo.Setup(r => r.GetEmbedding(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string articleId, string model, CancellationToken _) =>
                vectors.TryGetValue(articleId, out var v)
                    ? new ArticleEmbedding
                    {
                        ArticleId = articleId,
                        Model = model,
                        VectorJson = JsonSerializer.Serialize(v),
                        CachedAt = DateTime.UtcNow,
                    }
                    : null);
        return repo;
    }

    private static HypeResemblanceService ResemblanceSut(Mock<IHistoricalDataRepository> repo, IGeminiClient gemini) =>
        new(repo.Object, gemini, NullLogger<HypeResemblanceService>.Instance);

    [Fact]
    public async Task Resemblance_SkipsIdenticalArticles()
    {
        // Overlapping windows share cached rows: same-id pairs join at 1.0
        // by construction and must not count as resemblance.
        var repo = VectorRepo(new Dictionary<string, float[]>
        {
            ["shared"] = new float[] { 1, 0 },
        });
        var sut = ResemblanceSut(repo, new FuncGeminiStub(_ => Array.Empty<RelevanceVerdict>()));
        var current = ResemblanceDetail(("shared", "Shared article"));
        var library = new List<HypeCase>
        {
            ResemblanceRow("NFLX:2026-06-16", ResemblanceDetail(("shared", "Shared article"))),
        };

        var found = await sut.FindResemblingAsync(current, new[] { "shared" }, library);

        Assert.Empty(found);
    }

    [Fact]
    public async Task Resemblance_MatchesDistinctSimilarVectors()
    {
        var repo = VectorRepo(new Dictionary<string, float[]>
        {
            ["a"] = new float[] { 1, 0 },
            ["b"] = new float[] { 0.8f, 0.6f },
        });
        var sut = ResemblanceSut(repo, new FuncGeminiStub(_ => Array.Empty<RelevanceVerdict>()));
        var current = ResemblanceDetail(("a", "Current article"));
        var library = new List<HypeCase>
        {
            ResemblanceRow("NFLX:2026-06-16", ResemblanceDetail(("b", "Library article"))),
        };

        var found = await sut.FindResemblingAsync(current, new[] { "a" }, library);

        var match = Assert.Single(found);
        Assert.Equal("NFLX:2026-06-16", match.CaseId);
        Assert.Equal(0.8, match.Similarity, 3);
    }

    [Fact]
    public async Task Resemblance_EmptyWhenAiOffOrUncached()
    {
        var repo = VectorRepo(new Dictionary<string, float[]>
        {
            ["a"] = new float[] { 1, 0 },
        });
        var current = ResemblanceDetail(("a", "Current article"));
        var library = new List<HypeCase>
        {
            ResemblanceRow("NFLX:2026-06-16", ResemblanceDetail(("a", "Current article"))),
        };

        var off = ResemblanceSut(repo, new DisabledGeminiStub());
        Assert.Empty(await off.FindResemblingAsync(current, new[] { "a" }, library));

        var emptyRepo = VectorRepo(new Dictionary<string, float[]>());
        var uncached = ResemblanceSut(emptyRepo, new FuncGeminiStub(_ => Array.Empty<RelevanceVerdict>()));
        Assert.Empty(await uncached.FindResemblingAsync(current, new[] { "a" }, library));
    }
}
