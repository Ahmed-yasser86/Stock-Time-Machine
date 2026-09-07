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
            new List<HypeBriefArticle>
            {
                new() { Title = "Earnings beat estimates", Body = "Record quarter",
                    PublishedAt = new DateOnly(2026, 6, 10) },
            });

        Assert.Contains("2026-06-15", prompt);
        Assert.Contains("CASE FACTS", prompt);
        Assert.Contains("NEVER state or imply", prompt);
        Assert.Contains("NEVER predict", prompt);
        Assert.Contains("[1] (2026-06-10) Earnings beat estimates", prompt);
        Assert.Contains("DISAGREEMENTS AND GAPS", prompt);
        // No filing summaries: no mandatory regulatory bullet, honest empty section.
        Assert.DoesNotContain("Bullet 1 MUST", prompt);
        Assert.Contains("REGULATORY CONTEXT: no filing summaries available", prompt);
    }

    [Fact]
    public void BriefPrompt_RegulatorySectionMandatory()
    {
        // Filings with findings force both the dedicated section and the
        // mandatory first bullet (Phase-2 fix: the model previously crowded
        // filing content out of its output).
        var move = Move(MoveFlags.Spike);
        var detail = Detail(move, Topics());
        var match = new HypeSignalMatch { SignalId = "sentiment-split", Name = "Sentiment split" };
        var filings = new List<HypeFilingSummary>
        {
            new() { FormType = "8-K", FiledAt = new DateTime(2026, 6, 2),
                Findings = "Debt offering announced.", Disclosures = "None stated.",
                ConfidenceNote = "full", PagesProcessed = 1, TotalPages = 1 },
        };
        var prompt = HypeBriefPrompt.Build("MSFT", Peak, match, detail,
            new List<HypeBriefArticle>(), null, filings);

        Assert.Contains("REGULATORY CONTEXT (from filing documents", prompt);
        Assert.Contains("Debt offering announced.", prompt);
        Assert.Contains("Bullet 1 MUST summarize the REGULATORY CONTEXT", prompt);
        Assert.Contains("omitting any section is a failure", prompt);
    }

    [Fact]
    public void BriefPrompt_SplitsAtEventFromPostPeak()
    {
        // Retrospective dating: peak June 5, cutoff June 15. June-5 and
        // earlier items are at-event evidence; June 6+ items are subsequent
        // context only — never event-day knowledge, never causes.
        var peak = new DateOnly(2026, 6, 5);
        var cutoff = new DateOnly(2026, 6, 15);
        var detail = new HypeCaseDetail
        {
            CompanySymbol = "NVDA",
            PeakDate = peak,
            Score = 0.474,
            DailyReturnPct = -6.20m,
            Flags = new List<string> { "plunge", "breakdown" },
            SentimentDirection = SentimentDivergence.Disagree,
            Evidence = new HypeCaseEvidence
            {
                NewsCount = 3,
                News = new List<HypeCaseNewsItem>
                {
                    new() { Title = "June five story", PublishedAt = new DateTime(2026, 6, 5) },
                    new() { Title = "June eight story", PublishedAt = new DateTime(2026, 6, 8) },
                    new() { Title = "June ten story", PublishedAt = new DateTime(2026, 6, 10) },
                },
            },
        };
        var match = new HypeSignalMatch { SignalId = "sentiment-split", Name = "Sentiment split" };
        var articles = new List<HypeBriefArticle>
        {
            new() { Title = "Invitation story", Body = "b1", PublishedAt = new DateOnly(2026, 6, 5) },
            new() { Title = "Lawmakers story", Body = "b2", PublishedAt = new DateOnly(2026, 6, 8) },
            new() { Title = "Taiwan story", Body = "b3", PublishedAt = new DateOnly(2026, 6, 10) },
            new() { Title = "Legacy thread", Body = "b4", SpanLabel = "2026-05-31 → 2026-06-15" },
        };
        var prompt = HypeBriefPrompt.Build("NVDA", cutoff, match, detail, articles);

        // Frame: event date, cutoff, and window stated up front.
        Assert.Contains("Detected event date: 2026-06-05.", prompt);
        Assert.Contains("Investigation cutoff: 2026-06-15", prompt);
        // Groups split exactly at the peak; every dated item carries its date.
        Assert.Contains("AT-EVENT ARTICLES [1]", prompt);
        Assert.Contains("POST-PEAK ARTICLES [2]", prompt);
        Assert.Contains("(2026-06-08) Lawmakers story", prompt);
        Assert.Contains("(2026-06-10) Taiwan story", prompt);
        Assert.Contains("UNDATED ARTICLES", prompt);
        Assert.Contains("(2026-05-31 → 2026-06-15) Legacy thread", prompt);
        // Per-date evidence census: only the June-5 row is at-event.
        Assert.Contains("1 published 2026-06-05 (at-event)", prompt);
        Assert.Contains("1 published 2026-06-08 (post-peak)", prompt);
        Assert.Contains("1 published 2026-06-10 (post-peak)", prompt);
        // Bans: no blanket contemporary label, no post-peak causation,
        // invitation/response dating, spec attribution.
        Assert.DoesNotContain("contemporary coverage behind the trigger", prompt);
        Assert.Contains("never event-day knowledge, never causes", prompt);
        Assert.Contains("date the response by the LATER article", prompt);
        Assert.Contains("never the FirstSeen date for the whole set", prompt);
        Assert.Contains("name its reporting outlet inline", prompt);
        Assert.Contains("report each figure with its source attribution", prompt);
        Assert.Contains("mark pipeline stage facts as (case fact)", prompt);
        // Summary order mandated: event, cutoff, stages, at-event, post-peak.
        Assert.Contains("then at-event coverage, then post-peak developments", prompt);
    }

    private static HypeCaseThread DatedThread(
        string category, string title, string id, DateOnly published) => new()
    {
        LabelTerms = title.Split(' ').Take(3).ToList(),
        ArticleIds = new List<string> { id },
        ArticleDates = new Dictionary<string, DateOnly> { [id] = published },
        RepresentativeTitle = title,
        TopCategory = category,
    };

    private static HypeCaseDetail DatedCase(params HypeCaseThread[] threads) => new()
    {
        CompanySymbol = "NVDA",
        PeakDate = new DateOnly(2026, 6, 5),
        Score = 0.5,
        Flags = new List<string>(),
        SentimentDirection = SentimentDivergence.Unknown,
        PrePeakThreads = threads.ToList(),
        RegimePath = new Dictionary<string, string>(),
        Evidence = new HypeCaseEvidence(),
    };

    [Fact]
    public void QualifiedThreads_LegacyUndatedAlwaysVotes()
    {
        // No dates anywhere (legacy rows): span rule stands, nothing excluded.
        var detail = DatedCase(new HypeCaseThread
        {
            TopCategory = "FINANCIAL",
            RepresentativeTitle = "t",
            ArticleIds = new List<string> { "a" },
        });

        Assert.Single(HypeCaseProjection.QualifiedThreads(detail));
    }

    [Fact]
    public void QualifiedThreads_PostPeakOnlyThreadDoesNotVote()
    {
        // Span-straddling thread whose dated articles are ALL post-peak must
        // not trigger or shape pattern counts as pre-peak evidence.
        var detail = DatedCase(
            DatedThread("FINANCIAL", "June eight story", "a1", new DateOnly(2026, 6, 8)),
            DatedThread("FINANCIAL", "June ten story", "a2", new DateOnly(2026, 6, 10)));

        Assert.Empty(HypeCaseProjection.QualifiedThreads(detail));
        Assert.DoesNotContain(HypeSignals.Evaluate(detail), m => m.SignalId == "earnings-chatter");
        var vec = HypeCaseVector.Build(detail, HypeSignals.Evaluate(detail));
        Assert.Equal(0, vec.Skip(16).Take(34).Sum(x => Math.Abs(x)));
    }

    [Fact]
    public void QualifiedThreads_MixedSetVotesContemporaneousOnly()
    {
        var detail = DatedCase(
            DatedThread("FINANCIAL", "June five story", "a1", new DateOnly(2026, 6, 5)),
            DatedThread("FINANCIAL", "June four story", "a2", new DateOnly(2026, 6, 4)),
            DatedThread("FINANCIAL", "June eight story", "a3", new DateOnly(2026, 6, 8)));

        var qualified = HypeCaseProjection.QualifiedThreads(detail);

        Assert.Equal(2, qualified.Count);
        Assert.Contains(HypeSignals.Evaluate(detail), m => m.SignalId == "earnings-chatter");
    }

    [Fact]
    public void BriefFilter_DropsNoiseKeepsSignal()
    {
        // Social post about a fictional SpaceX IPO: no company mention, no
        // material signal → excluded before any LLM token is spent.
        var noise = new HypeCaseSocialPost
        {
            Title = "My fight to convince ChatGPT that SpaceX had an IPO this month",
            Excerpt = "Elon Musk IPO rumors and market manipulation claims",
            Community = "r/wallstreetbets",
        };
        var signal = new HypeCaseSocialPost
        {
            Title = "NVDA earnings thread: data center revenue doubles",
            Excerpt = "Nvidia data center revenue beat expectations on AI demand",
            Community = "r/wallstreetbets",
        };
        var kept = HypeBriefInputFilter.FilterSocial("NVDA", "Nvidia", new[] { noise, signal });

        Assert.Single(kept);
        Assert.Contains("earnings", kept[0].Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BriefFilter_NewsAndThreads()
    {
        var earnings = new HypeCaseNewsItem { Title = "Nvidia earnings beat expectations", Description = "Data center revenue records" };
        var murder = new HypeCaseNewsItem { Title = "Murder documentary streams", Description = "A Netflix true-crime film about the case" };
        var keptNews = HypeBriefInputFilter.FilterNews("NVDA", "Nvidia", new[] { earnings, murder });

        Assert.Single(keptNews);
        Assert.Equal("Nvidia earnings beat expectations", keptNews[0].Title);

        var threads = new List<HypeCaseThread>
        {
            new() { TopCategory = "FINANCIAL", RepresentativeTitle = "t1" },
            new() { TopCategory = "UNRELATED", RepresentativeTitle = "t2" },
            new() { TopCategory = "REPUTATIONAL", RepresentativeTitle = "t3" },
            new() { TopCategory = "", RepresentativeTitle = "t4" },
        };
        var keptThreads = HypeBriefInputFilter.FilterThreads(threads);

        Assert.Single(keptThreads);
        Assert.Equal("t1", keptThreads[0].RepresentativeTitle);
    }

    [Fact]
    public void CaseVector_LayoutWeightsAndNormalization()
    {
        var detail = new HypeCaseDetail
        {
            CompanySymbol = "NVDA",
            PeakDate = Peak,
            Score = 0.5,
            DailyReturnPct = 6m,
            Flags = new List<string> { MoveFlags.Spike },
            SentimentDirection = SentimentDivergence.Disagree,
            PrePeakThreads = new List<HypeCaseThread>
            {
                new() { TopCategory = "FINANCIAL", RepresentativeTitle = "t1", ArticleIds = new List<string> { "a1" } },
                new() { TopCategory = "FINANCIAL", RepresentativeTitle = "t2", ArticleIds = new List<string> { "a2" } },
            },
            RegimePath = new Dictionary<string, string>
            {
                ["2026-06-10"] = MarketRegimes.Tense,
                ["2026-06-11"] = MarketRegimes.Tense,
            },
            Evidence = new HypeCaseEvidence
            {
                NewsCount = 5,
                Filings = new List<HypeCaseFiling>
                {
                    new() { FormType = "8-K", FiledAt = new DateTime(2026, 6, 1) },
                },
            },
        };
        var matches = new List<HypeSignalMatch>
        {
            new() { SignalId = "earnings-chatter", Name = "Earnings-chatter clustering" },
        };

        var vec = HypeCaseVector.Build(detail, matches);

        Assert.Equal(HypeCaseVector.Dimensions, vec.Length);
        Assert.Equal(96 + 3072, HypeCaseVector.Dimensions);
        // Unit norm after weighting.
        Assert.Equal(1.0, Math.Sqrt(vec.Sum(x => (double)x * x)), 5);
        // Signal bit [0] carries weight 3.0 pre-normalization: it dominates
        // the filing dim [65] (0.5 × 1/3).
        Assert.True(Math.Abs(vec[0]) > Math.Abs(vec[65]));
        // Sentiment disagree one-hot [13] present, others absent.
        Assert.True(vec[13] != 0);
        Assert.Equal(0, vec[12]);
        // Tense-only regime histogram: dim 11 = 2 × dim 8 post-norm.
        Assert.True(vec[8] > 0 && vec[11] > vec[8]);
        // Positive 6% move: dim 68 = 0.6 × 0.5 pre-norm, dim 69 zero.
        Assert.True(vec[68] > 0);
        Assert.Equal(0, vec[69]);
        // Category share+volume [16–49]: 2 FINANCIAL threads → share 1.0,
        // volume 0.2 on dims 16/17; all other categories zero.
        Assert.True(vec[16] > 0 && vec[17] > 0);
        Assert.Equal(0, vec[18]);
        // No content mean passed: content side zero.
        Assert.All(vec.Skip(HypeCaseVector.StructuralDimensions), x => Assert.Equal(0, x));
        // No filing rows: filing dims [89–95] zero.
        Assert.All(vec.Skip(89).Take(7), x => Assert.Equal(0, x));
    }

    [Fact]
    public void CaseVector_PairsBigramsAndSentimentMean()
    {
        var detail = new HypeCaseDetail
        {
            CompanySymbol = "NVDA",
            PeakDate = Peak,
            SentimentDirection = SentimentDivergence.Disagree,
            SentimentMean = -0.8,
            RegimePath = new Dictionary<string, string>
            {
                ["2026-06-09"] = MarketRegimes.Warming,
                ["2026-06-10"] = MarketRegimes.Normal,
                ["2026-06-12"] = MarketRegimes.Tense,
            },
        };
        var matches = new List<HypeSignalMatch>
        {
            new() { SignalId = "sentiment-split", Name = "s" },
            new() { SignalId = "regulatory-overhang", Name = "r" },
        };

        var vec = HypeCaseVector.Build(detail, matches);

        // Pair dims: sentiment-split is signal index 4, regulatory-overhang
        // index 1 → pair (1,4) lives at 50 + 5 + 2 = 57.
        Assert.True(vec[57] > 0);
        // A non-firing pair stays zero (indices 0,2 → 51).
        Assert.Equal(0, vec[51]);
        // Sentiment magnitude [72]: -0.8 × 2.0 pre-norm, negative sign kept.
        Assert.True(vec[72] < 0);
        // Warming→normal→tense bigrams: [warming,normal] then [normal,tense].
        // Label order calm=0, normal=1, tense=2, warming=3:
        // (3,1) → 73+13=86; (1,2) → 73+6=79.
        Assert.True(vec[86] > 0 && vec[79] > 0);
        Assert.Equal(0, vec[73]);
    }

    [Fact]
    public void CaseVector_ContentScalarApplied()
    {
        var detail = new HypeCaseDetail { CompanySymbol = "NVDA", PeakDate = Peak, Score = 0.5 };
        var unit = Enumerable.Repeat(1f / (float)Math.Sqrt(3072), 3072).ToList();

        var vec = HypeCaseVector.Build(detail, new List<HypeSignalMatch>(), unit);

        // Content side norm post-global-normalization is positive and the
        // structural score dim survives alongside it.
        var contentNorm = Math.Sqrt(vec.Skip(89).Sum(x => (double)x * x));
        Assert.True(contentNorm > 0.3 && contentNorm < 0.95);
        Assert.True(vec[70] > 0);
        Assert.Equal(1.0, Math.Sqrt(vec.Sum(x => (double)x * x)), 5);
    }

    [Fact]
    public void CaseVector_FilingDims_FromRecords()
    {
        // Structured filing dims [89–95]: event presence one-hot, max
        // relevance, mean sentiment. Corrupt JSON contributes zeros.
        var good = HypeCaseVector.FromRecord(
            new FilingSummaryRecord { AccessionNumber = "a1" },
            """{"event_type":"acquisition","market_relevance":"high","sentiment":"positive"}""");
        var bad = HypeCaseVector.FromRecord(
            new FilingSummaryRecord { AccessionNumber = "a2" }, "{not json");
        Assert.Equal((FilingEventTypes.Acquisition, 1.0, 1.0), (good.EventType, good.Relevance, good.Sentiment));
        Assert.Equal((FilingEventTypes.Other, 0.0, 0.0), (bad.EventType, bad.Relevance, bad.Sentiment));

        var detail = new HypeCaseDetail { CompanySymbol = "NVDA", PeakDate = Peak };
        var vec = HypeCaseVector.Build(detail, new List<HypeSignalMatch>(),
            null, new List<HypeCaseVector.FilingVectorInput> { good, bad });

        // acquisition is index 1 → dim 90 nonzero; relevance max 1.0 → dim 94;
        // sentiment mean (1+0)/2 = 0.5 → dim 95 nonzero but smaller.
        Assert.True(vec[90] > 0);
        Assert.Equal(0, vec[89]);
        Assert.True(vec[94] > 0);
        Assert.True(vec[95] > 0 && vec[95] < vec[94]);
    }

    [Fact]
    public void CaseVector_NullDetail_IsZero()
    {
        var vec = HypeCaseVector.Build(null!, new List<HypeSignalMatch>());

        Assert.Equal(HypeCaseVector.Dimensions, vec.Length);
        Assert.All(vec, x => Assert.Equal(0, x));
    }

    [Fact]
    public void MeanPool_AveragesAndNormalizes()
    {
        var mean = HypeCaseVector.MeanPool(new List<IReadOnlyList<float>>
        {
            new float[] { 1, 0 },
            new float[] { 0, 1 },
        });

        Assert.NotNull(mean);
        Assert.Equal(2, mean!.Count);
        Assert.Equal(Math.Sqrt(0.5), mean[0], 5);
        Assert.Null(HypeCaseVector.MeanPool(new List<IReadOnlyList<float>>()));
    }

    [Theory]
    [InlineData("https://www.sec.gov/Archives/edgar/data/1045810/000104581026000051/", "0001045810-26-000051")]
    [InlineData("https://www.sec.gov/Archives/edgar/data/789019/000119312526258667", "0001193125-26-258667")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("https://example.com/no-digits-here/", "")]
    public void FilingAccession_DerivesFromDirectoryUrl(string? url, string expected)
    {
        // Legacy rows froze before accession storage: exact SEC 10-2-6
        // reconstruction from the directory URL keeps them joinable.
        Assert.Equal(expected, HypeFilingService.DeriveAccessionNumber(url));
    }

    [Fact]
    public void FilingPicker_SkipsNavPages_SelectsPrimaryDoc()
    {
        // Regression (Phase-1 root cause): {accession}-index-headers.html
        // sorts first and used to pass every filter, so briefs narrated
        // nav-link soup instead of disclosures.
        var indexJson = """
            {"directory": {"item": [
              {"name": "0001045810-26-000051-index-headers.html"},
              {"name": "0001045810-26-000051-index.html"},
              {"name": "FilingSummary.xml"},
              {"name": "nvda-20260520.htm"},
              {"name": "R1.htm"},
              {"name": "nvda-20260520_htm.xml"}
            ]}}
            """;

        Assert.Equal("nvda-20260520.htm", HypeFilingService.PickPrimaryDocument(indexJson));
    }

    [Fact]
    public void FilingPicker_FallsBackToSubmissionText()
    {
        var indexJson = """
            {"directory": {"item": [
              {"name": "0001045810-26-000051-index-headers.html"},
              {"name": "0001045810-26-000051.txt"},
              {"name": "R1.htm"}
            ]}}
            """;

        Assert.Equal("0001045810-26-000051.txt", HypeFilingService.PickPrimaryDocument(indexJson));
    }

    [Fact]
    public void FilingPicker_EmptyOrMalformed_ReturnsNull()
    {
        Assert.Null(HypeFilingService.PickPrimaryDocument("{}"));
        Assert.Null(HypeFilingService.PickPrimaryDocument("{not json"));
        Assert.Null(HypeFilingService.PickPrimaryDocument(
            """{"directory": {"item": [{"name": "R1.htm"}]}}"""));
    }

    [Fact]
    public void StripHtml_RemovesInlineXbrlHeader()
    {
        var html = "<html><body><ix:header><xbrli:context>0000789019 us-gaap:CommonStockMember</xbrli:context></ix:header><p>FORM 8-K CURRENT REPORT</p></body></html>";

        var text = HypeFilingText.StripHtml(html);

        Assert.Contains("FORM 8-K CURRENT REPORT", text);
        Assert.DoesNotContain("us-gaap", text);
        Assert.DoesNotContain("0000789019", text);
    }

    [Fact]
    public void FilingStructured_ParsesEightK()
    {
        var row = HypeFilingService.ParseStructured("0001", "8-K",
            """{"event_type":"acquisition","primary_topic":"Buys chip startup.","market_relevance":"high","sentiment":"positive","findings":"Acquired X for $1B.","disclosures":"None stated."}""",
            "abc123");

        Assert.NotNull(row);
        Assert.Equal("0001", row!.AccessionNumber);
        Assert.Contains("acquisition", row.StructuredJson);
        Assert.Equal("Acquired X for $1B.", row.Findings);
        Assert.Equal("abc123", row.ContentHash);
    }

    [Fact]
    public void FilingStructured_ParsesTenQAndNormalizes()
    {
        // Unknown enum values fall back honestly (stable/bogus → stable +
        // neutral + low); risks capped at 10 × 64 chars.
        var row = HypeFilingService.ParseStructured("0002", "10-Q",
            """{"financial_direction":"bogus","key_risk_flags":["litigation","supply chain"],"market_relevance":"medium","sentiment":"bogus","findings":"Revenue up.","disclosures":"Risk noted."}""",
            "def456");

        Assert.NotNull(row);
        Assert.Contains("stable", row!.StructuredJson);
        Assert.Contains("litigation", row.StructuredJson);
        Assert.Contains("medium", row.StructuredJson);
        Assert.Contains("neutral", row.StructuredJson);
    }

    [Fact]
    public void FilingStructured_RejectsBadInput()
    {
        Assert.Null(HypeFilingService.ParseStructured("a", "8-K", "{not json", "h"));
        Assert.Null(HypeFilingService.ParseStructured("a", "8-K",
            """{"event_type":"acquisition","market_relevance":"high","sentiment":"positive","findings":"","disclosures":"x"}""", "h"));
        // Unknown event types normalize to "other" (kept, never rejected).
        var normalized = HypeFilingService.ParseStructured("a", "8-K",
            """{"event_type":"bogus","primary_topic":"T","market_relevance":"high","sentiment":"positive","findings":"F","disclosures":"D"}""", "h");
        Assert.NotNull(normalized);
        Assert.Contains("other", normalized!.StructuredJson);
    }

    [Fact]
    public async Task FilingStore_RoundTrip()
    {
        var options = new DbContextOptionsBuilder<StockTimeMachineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var db = new StockTimeMachineDbContext(options);
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);

        Assert.Null(await repo.GetFilingSummary("missing"));
        await repo.StoreFilingSummary(new FilingSummaryRecord
        {
            AccessionNumber = "0001", FormType = "8-K",
            StructuredJson = """{"event_type":"acquisition"}""",
            Findings = "F", Disclosures = "D", ConfidenceNote = "full", ContentHash = "h1",
        });
        var fetched = await repo.GetFilingSummary("0001");
        Assert.NotNull(fetched);
        Assert.Equal("8-K", fetched!.FormType);

        // Upsert overwrites (re-extraction replaces stale rows).
        await repo.StoreFilingSummary(new FilingSummaryRecord
        {
            AccessionNumber = "0001", FormType = "8-K",
            StructuredJson = """{"event_type":"regulatory"}""",
            Findings = "F2", Disclosures = "D2", ConfidenceNote = "full", ContentHash = "h2",
        });
        Assert.Contains("regulatory", (await repo.GetFilingSummary("0001"))!.StructuredJson);
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

    private static HypeCase ResemblanceRow(string id, HypeCaseDetail detail, DateOnly? peak = null, string symbol = "NFLX") => new()
    {
        Id = id,
        CompanySymbol = symbol,
        PeakDate = peak ?? Peak,
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

    private sealed class NullFilingService : IHypeFilingService
    {
        public Task<IReadOnlyList<HypeFilingSummary>> SummarizeFilingsAsync(
            string symbol, HypeSignalMatch match, HypeCaseDetail detail,
            DateOnly asOfDate, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<HypeFilingSummary>>(Array.Empty<HypeFilingSummary>());
        public Task<FilingSummaryRecord?> SummarizeStructuredAsync(
            string symbol, string accessionNumber, string formType, DateTime filedAt,
            string documentUrl, DateOnly asOfDate, CancellationToken ct = default) =>
            Task.FromResult<FilingSummaryRecord?>(null);
        public Task<int> EnsureSummariesAsync(
            string symbol, IEnumerable<SecFiling> filings, DateOnly asOfDate,
            CancellationToken ct = default) =>
            Task.FromResult(0);
    }

    private sealed class UnavailableVectorStore : IVectorStore
    {
        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(false);
        public Task<(bool Reachable, ulong Points)> HealthAsync(CancellationToken ct = default) =>
            Task.FromResult((false, 0UL));
        public Task<int> UpsertAsync(string caseId, string symbol, DateOnly peakDate,
            IReadOnlyList<(string ArticleId, float[] Vector)> vectors, CancellationToken ct = default) =>
            Task.FromResult(0);
        public Task<IReadOnlyList<VectorHit>> SearchAsync(float[] query, int limit, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<VectorHit>>(Array.Empty<VectorHit>());
        public Task<int> UpsertCaseAsync(string caseId, string symbol, DateOnly peakDate,
            float[] vector, CancellationToken ct = default) =>
            Task.FromResult(0);
        public Task<IReadOnlyList<VectorHit>> SearchCasesAsync(float[] query, int limit, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<VectorHit>>(Array.Empty<VectorHit>());
        public Task<int> UpsertStructuralAsync(string caseId, string symbol, DateOnly peakDate,
            float[] vector, CancellationToken ct = default) =>
            Task.FromResult(0);
        public Task<IReadOnlyList<VectorHit>> SearchStructuralAsync(float[] query, int limit, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<VectorHit>>(Array.Empty<VectorHit>());
    }

    private static HypeResemblanceService ResemblanceSut(
        Mock<IHistoricalDataRepository> repo, IGeminiClient gemini, IVectorStore? vectors = null) =>
        new(repo.Object, gemini, vectors ?? new UnavailableVectorStore(),
            NullLogger<HypeResemblanceService>.Instance);

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
            ResemblanceRow("NFLX:2026-08-01", ResemblanceDetail(("b", "Library article")),
                peak: new DateOnly(2026, 8, 1)),
        };

        var found = await sut.FindResemblingAsync(current, new[] { "a" }, library);

        var match = Assert.Single(found);
        Assert.Equal("NFLX:2026-08-01", match.CaseId);
        Assert.Equal(0.8, match.Similarity, 3);
    }

    [Fact]
    public void Cosine_IsMathematicallyCorrect()
    {
        Assert.Equal(1.0, EmbeddingClustering.Cosine(new float[] { 1, 0 }, new float[] { 1, 0 }), 9);
        Assert.Equal(0.0, EmbeddingClustering.Cosine(new float[] { 1, 0 }, new float[] { 0, 1 }), 9);
        Assert.Equal(-1.0, EmbeddingClustering.Cosine(new float[] { 1, 0 }, new float[] { -1, 0 }), 9);
        Assert.Equal(0.8, EmbeddingClustering.Cosine(new float[] { 1, 0 }, new float[] { 0.8f, 0.6f }), 6);
    }

    [Fact]
    public async Task Resemblance_ExcludesSameSymbolThirtyDayNeighbors()
    {
        // Date neighbors share most of the pre-peak window: even byte-identical
        // vectors must not join (their 1.00 is window overlap, not resemblance).
        var repo = VectorRepo(new Dictionary<string, float[]>
        {
            ["a"] = new float[] { 1, 0 },
            ["b"] = new float[] { 1, 0 },
        });
        var sut = ResemblanceSut(repo, new FuncGeminiStub(_ => Array.Empty<RelevanceVerdict>()));
        var current = ResemblanceDetail(("a", "Current article"));
        var library = new List<HypeCase>
        {
            ResemblanceRow("NFLX:2026-06-25", ResemblanceDetail(("b", "Neighbor article")),
                peak: new DateOnly(2026, 6, 25)),
        };

        var found = await sut.FindResemblingAsync(current, new[] { "a" }, library);

        Assert.Empty(found);
    }

    [Fact]
    public async Task Resemblance_KeepsDistantSameSymbolAndNearOtherSymbol()
    {
        var repo = VectorRepo(new Dictionary<string, float[]>
        {
            ["a"] = new float[] { 1, 0 },
            ["b"] = new float[] { 0.8f, 0.6f },
            ["c"] = new float[] { 0.9f, 0.4359f },
        });
        var sut = ResemblanceSut(repo, new FuncGeminiStub(_ => Array.Empty<RelevanceVerdict>()));
        var current = ResemblanceDetail(("a", "Current article"));
        var library = new List<HypeCase>
        {
            ResemblanceRow("NFLX:2026-08-01", ResemblanceDetail(("b", "Distant article")),
                peak: new DateOnly(2026, 8, 1)),
            ResemblanceRow("MSFT:2026-06-18", ResemblanceDetail(("c", "Other symbol article")),
                peak: new DateOnly(2026, 6, 18), symbol: "MSFT"),
        };

        var found = await sut.FindResemblingAsync(current, new[] { "a" }, library);

        Assert.Equal(2, found.Count);
        Assert.All(found, f => Assert.True(f.Similarity < 1.0));
    }

    [Fact]
    public async Task Resemblance_VectorStorePath_AppliesSameRules()
    {
        // Qdrant hits go through identical exclusion + threshold math: the
        // neighbor is dropped, the distant case joins.
        var repo = VectorRepo(new Dictionary<string, float[]>
        {
            ["a"] = new float[] { 1, 0 },
        });
        var store = new Mock<IVectorStore>();
        store.Setup(s => s.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        store.Setup(s => s.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorHit>
            {
                new("b", new float[] { 0.8f, 0.6f }, "NFLX:2026-08-01", "NFLX", new DateOnly(2026, 8, 1)),
                new("c", new float[] { 1, 0 }, "NFLX:2026-06-25", "NFLX", new DateOnly(2026, 6, 25)),
            });
        var sut = ResemblanceSut(repo, new FuncGeminiStub(_ => Array.Empty<RelevanceVerdict>()), store.Object);
        var current = ResemblanceDetail(("a", "Current article"));

        var found = await sut.FindResemblingAsync(current, new[] { "a" }, new List<HypeCase>());

        // The vector path ran (current vector cached locally): only the
        // distant case survives exclusion.
        store.Verify(s => s.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        Assert.Single(found);
        Assert.Equal("NFLX:2026-08-01", found[0].CaseId);
    }

    [Fact]
    public async Task Resemblance_VectorStoreFailure_FallsBackToMemory()
    {
        var repo = VectorRepo(new Dictionary<string, float[]>
        {
            ["a"] = new float[] { 1, 0 },
            ["b"] = new float[] { 0.8f, 0.6f },
        });
        var store = new Mock<IVectorStore>();
        store.Setup(s => s.IsAvailableAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("down"));
        var sut = ResemblanceSut(repo, new FuncGeminiStub(_ => Array.Empty<RelevanceVerdict>()), store.Object);
        var current = ResemblanceDetail(("a", "Current article"));
        var library = new List<HypeCase>
        {
            ResemblanceRow("NFLX:2026-08-01", ResemblanceDetail(("b", "Library article")),
                peak: new DateOnly(2026, 8, 1)),
        };

        var found = await sut.FindResemblingAsync(current, new[] { "a" }, library);

        Assert.Single(found);
        Assert.Equal("NFLX:2026-08-01", found[0].CaseId);
    }

    [Fact]
    public async Task Indexer_UpsertsCachedThreadVectors()
    {
        var repo = VectorRepo(new Dictionary<string, float[]>
        {
            ["a"] = new float[] { 1, 0 },
        });
        var store = new Mock<IVectorStore>();
        store.Setup(s => s.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        store.Setup(s => s.UpsertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(),
                It.IsAny<IReadOnlyList<(string ArticleId, float[] Vector)>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        store.Setup(s => s.UpsertCaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(),
                It.IsAny<float[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var indexer = new HypeCaseIndexer(repo.Object, store.Object,
            new FuncGeminiStub(_ => Array.Empty<RelevanceVerdict>()),
            new NullFilingService(),
            NullLogger<HypeCaseIndexer>.Instance);
        var detail = ResemblanceDetail(("a", "Current article"));

        var indexed = await indexer.IndexCaseAsync(detail);

        // 1 article point + 1 case pattern point.
        Assert.Equal(2, indexed);
        store.Verify(s => s.UpsertAsync("NFLX:2026-06-15", "NFLX", new DateOnly(2026, 6, 15),
            It.Is<IReadOnlyList<(string ArticleId, float[] Vector)>>(v => v.Count == 1 && v[0].ArticleId == "a"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Indexer_ThreadlessCase_StillWritesCasePoint()
    {
        // Regression: the case pattern point must not depend on threads
        // existing (38 production cases have none).
        var repo = VectorRepo(new Dictionary<string, float[]>());
        var store = new Mock<IVectorStore>();
        store.Setup(s => s.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        store.Setup(s => s.UpsertCaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(),
                It.IsAny<float[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var indexer = new HypeCaseIndexer(repo.Object, store.Object,
            new FuncGeminiStub(_ => Array.Empty<RelevanceVerdict>()),
            new NullFilingService(),
            NullLogger<HypeCaseIndexer>.Instance);
        var window = Window(Move(MoveFlags.Spike));
        window.EvidenceByDate.Clear();
        var detail = HypeCaseLibrary.TryReadDetail(
            HypeCaseProjection.Build(window, Move(MoveFlags.Spike), null))!;

        Assert.Empty(detail.PrePeakThreads);
        Assert.Equal(1, await indexer.IndexCaseAsync(detail));
        store.Verify(s => s.UpsertCaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(),
            It.Is<float[]>(v => v.Length == HypeCaseVector.Dimensions),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Indexer_UnavailableStore_IndexesNothing()
    {
        var repo = VectorRepo(new Dictionary<string, float[]>
        {
            ["a"] = new float[] { 1, 0 },
        });
        var store = new Mock<IVectorStore>();
        store.Setup(s => s.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var indexer = new HypeCaseIndexer(repo.Object, store.Object,
            new FuncGeminiStub(_ => Array.Empty<RelevanceVerdict>()),
            new NullFilingService(),
            NullLogger<HypeCaseIndexer>.Instance);

        Assert.Equal(0, await indexer.IndexCaseAsync(ResemblanceDetail(("a", "x"))));
    }

    private static HypeCaseDetail StructuralDetail() => new()
    {
        CompanySymbol = "NVDA",
        PeakDate = Peak,
        Score = 0.5,
        Flags = new List<string> { MoveFlags.Spike },
        SentimentDirection = SentimentDivergence.Unknown,
        RegimePath = new Dictionary<string, string>
        {
            ["2026-06-10"] = MarketRegimes.Tense,
            ["2026-06-11"] = MarketRegimes.Tense,
            ["2026-06-12"] = MarketRegimes.Tense,
        },
        PrePeakThreads = new List<HypeCaseThread>
        {
            new() { TopCategory = "REGULATORY", RepresentativeTitle = "Chip curbs", ArticleIds = new List<string> { "s1" } },
        },
        Evidence = new HypeCaseEvidence(),
    };

    private static Mock<IVectorStore> DualStore(
        IReadOnlyList<VectorHit> structural,
        IReadOnlyList<VectorHit> hybrid)
    {
        var store = new Mock<IVectorStore>();
        store.Setup(s => s.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        store.Setup(s => s.SearchStructuralAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(structural);
        store.Setup(s => s.SearchCasesAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hybrid);
        return store;
    }

    private static float[] UnitVector(int dims, int hot)
    {
        var v = new float[dims];
        v[hot % dims] = 1f;
        return v;
    }

    [Fact]
    public async Task Resemblance_DualQuery_LabelsStrongPatternNarrative()
    {
        // Hit vectors mirror the service's own queries (identical → cosine
        // 1.0), so this test pins merge + labels, not arithmetic.
        // Structural + hybrid agree on BOTH → "strong" first; C is
        // structural-only → "pattern"; D is hybrid-only → "narrative".
        var current = StructuralDetail();
        var matches = HypeSignals.Evaluate(current);
        Assert.Contains(matches, m => m.SignalId == "regulatory-overhang");
        var structuralQuery = HypeCaseVector.BuildStructural(current, matches);
        // Service-side hybrid query carries the content mean ([1,0] from the
        // single cached trigger vector): mirror it exactly for cosine 1.0.
        var mean = HypeCaseVector.MeanPool(new List<IReadOnlyList<float>> { new float[] { 1, 0 } });
        var hybridQuery = HypeCaseVector.Build(current, matches, mean);
        var repo = VectorRepo(new Dictionary<string, float[]>
        {
            ["s1"] = new float[] { 1, 0 },
        });
        var store = DualStore(
            new List<VectorHit>
            {
                new("B", structuralQuery, "NVDA:2026-08-01", "NVDA", new DateOnly(2026, 8, 1)),
                new("C", structuralQuery, "NVDA:2026-08-02", "NVDA", new DateOnly(2026, 8, 2)),
            },
            new List<VectorHit>
            {
                new("B", hybridQuery, "NVDA:2026-08-01", "NVDA", new DateOnly(2026, 8, 1)),
                new("D", hybridQuery, "MSFT:2026-08-01", "MSFT", new DateOnly(2026, 8, 1)),
            });
        var sut = ResemblanceSut(repo, new FuncGeminiStub(_ => Array.Empty<RelevanceVerdict>()), store.Object);

        var found = await sut.FindResemblingAsync(current, new[] { "s1" }, new List<HypeCase>());

        Assert.Equal(3, found.Count);
        Assert.Equal(HypeResemblanceKinds.Strong, found[0].Kind);
        Assert.Equal("NVDA:2026-08-01", found[0].CaseId);
        Assert.Contains(found, f => f.CaseId == "NVDA:2026-08-02" && f.Kind == HypeResemblanceKinds.Pattern);
        Assert.Contains(found, f => f.CaseId == "MSFT:2026-08-01" && f.Kind == HypeResemblanceKinds.Narrative);
    }

    [Fact]
    public async Task Resemblance_ContentSignals_SkipStructuralQuery()
    {
        // sentiment-split is content-dominant: no structural search issued.
        // The hybrid hit mirrors the service query (identical → cosine 1.0).
        var current = ResemblanceDetail(("a", "Current article"));
        var matches = HypeSignals.Evaluate(current);
        Assert.DoesNotContain(matches, m => HypeResemblanceKinds.StructuralSignals.Contains(m.SignalId));
        var mean = HypeCaseVector.MeanPool(new List<IReadOnlyList<float>> { new float[] { 1, 0 } });
        var hybridQuery = HypeCaseVector.Build(current, matches, mean);
        var repo = VectorRepo(new Dictionary<string, float[]>
        {
            ["a"] = new float[] { 1, 0 },
        });
        var store = DualStore(
            new List<VectorHit>(),
            new List<VectorHit>
            {
                new("b", hybridQuery, "NFLX:2026-08-01", "NFLX", new DateOnly(2026, 8, 1)),
            });
        var sut = ResemblanceSut(repo, new FuncGeminiStub(_ => Array.Empty<RelevanceVerdict>()), store.Object);

        var found = await sut.FindResemblingAsync(current, new[] { "a" }, new List<HypeCase>());

        store.Verify(s => s.SearchStructuralAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Single(found);
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
