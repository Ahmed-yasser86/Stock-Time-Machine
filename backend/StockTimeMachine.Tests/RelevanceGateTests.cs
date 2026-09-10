using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using StockTimeMachine;

namespace StockTimeMachine.Tests;

// The shared relevance gate: provider association (entity match, ticker tag)
// is retrieval, never relevance. These tests pin the materiality policy with
// the NFLX negative cases from production plus positive edge cases, then pin
// the service wiring (RULE fallback, store-failure resilience, verdict
// precedence USER > AI > RULE).
public class RelevanceGateTests
{
    private static StockTimeMachineDbContext NewDb() => new(
        new DbContextOptionsBuilder<StockTimeMachineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ICompanyDirectory NetflixDirectory() => new StubCompanyDirectory(
        new CompanyInfo("NFLX", "Netflix, Inc.", "0001065280", "NASDAQ",
            "Communication Services", "Entertainment"));

    private static NewsArticle Doc(string id, string title, string desc = "") => new()
    {
        Id = id, Title = title, Description = desc, Source = "GDELT",
        PublishedAt = new DateTime(2026, 6, 10), Url = "https://example.com/" + id,
        CompanySymbol = "NFLX",
    };

    // Incidental Netflix mentions must NOT pass: murder/prison/crime coverage
    // distributed or documented by Netflix is entertainment, not material.
    [Theory]
    [InlineData("Mackenzie Shirilla lands prison job after murder convictions",
        "The case was featured in a Netflix documentary series")]
    [InlineData("Rachel Nickell murder: new podcast explores the case",
        "Netflix true-crime series covers the investigation")]
    [InlineData("California man killed estranged wife, police say",
        "A documentary about the case streams on Netflix")]
    [InlineData("Serial con artist Samantha Cookes jailed again",
        "Her story was told in a Netflix film")]
    [InlineData("Review: the greatest war movies streaming now",
        "Including several titles on Netflix this month")]
    [InlineData("Michael J. Fox to voice lead in animated Dragoons movie",
        "The film lands on Netflix next year")]
    public void Rule_IncidentalMention_NeverRelevant(string title, string desc)
    {
        var verdict = MaterialityRules.Judge("NFLX", "Netflix, Inc.", title, desc);

        Assert.NotEqual(RelevanceDecisions.Relevant, verdict.Decision);
        Assert.False(RelevanceService.PassesGate(new ArticleRelevance { Decision = verdict.Decision }));
    }

    // Provider association without any mention or signal is not relevance.
    [Theory]
    [InlineData("Elon Musk says SpaceX IPO could come next year", "")]
    [InlineData("Cocaine dealers sentenced in Frankfurt drug ring", "")]
    [InlineData("Oregon prison housing unit faces overcrowding suit", "")]
    [InlineData("Kennedy Center announces summer gala lineup", "")]
    [InlineData("Antibiotics overuse flagged in new health study", "")]
    [InlineData("Germany finance minister unveils budget plan", "")]
    [InlineData("Trump pardon in insider trading case sparks debate", "")]
    [InlineData("Korean schools expand AI surveillance pilot", "")]
    [InlineData("Accenture acquires Whalar Group", "Senior executives quoted")]
    public void Rule_UnrelatedProviderNoise_NotRelevant(string title, string desc)
    {
        var verdict = MaterialityRules.Judge("NFLX", "Netflix, Inc.", title, desc);

        Assert.NotEqual(RelevanceDecisions.Relevant, verdict.Decision);
        Assert.False(RelevanceService.PassesGate(new ArticleRelevance { Decision = verdict.Decision }));
    }

    // A bare ticker tag is retrieval metadata, not a materiality judgment.
    [Fact]
    public void Rule_BareTickerTag_NotRelevant()
    {
        var verdict = MaterialityRules.Judge(
            "NFLX", "Netflix, Inc.", "Stocks to watch: NFLX, AAPL rally into the close", "");

        Assert.NotEqual(RelevanceDecisions.Relevant, verdict.Decision);
    }

    // Genuinely material company coverage passes, with a real category.
    [Theory]
    [InlineData("Netflix earnings beat expectations on subscriber growth", "", "FINANCIAL")]
    [InlineData("Netflix adds 8 million subscribers in fourth quarter", "", "FINANCIAL")]
    [InlineData("Tyra Banks sues Netflix for defamation over documentary", "", "LEGAL")]
    [InlineData("Director who defrauded Netflix of $11 million seeks leniency", "", "LEGAL")]
    [InlineData("Netflix announces layoffs affecting 300 employees", "", "OPERATIONS")]
    [InlineData("FCC proposes new streaming regulation affecting Netflix, Disney", "", "REGULATORY")]
    [InlineData("Netflix launches ad-supported tier in twelve markets", "", "PRODUCT")]
    [InlineData("Netflix CEO unveils new content strategy at investor day", "", "MANAGEMENT")]
    [InlineData("Paramount and Warner Bros merger would reshape streaming", "Netflix faces tougher competition", "STRATEGY")]
    public void Rule_MaterialCoverage_Relevant(string title, string desc, string category)
    {
        var verdict = MaterialityRules.Judge("NFLX", "Netflix, Inc.", title, desc);

        Assert.Equal(RelevanceDecisions.Relevant, verdict.Decision);
        Assert.Equal(RelevanceSources.Rule, "RULE");
        Assert.Equal(category, verdict.Category);
        Assert.True(RelevanceService.PassesGate(new ArticleRelevance { Decision = verdict.Decision }));
    }

    // Industry-relevant without a company mention stays UNCERTAIN: excluded
    // from admitted evidence until semantic (AI) or human review — never
    // auto-admitted, never silently dropped (candidates surface it).
    [Fact]
    public void Rule_IndustryWithoutMention_UncertainAndExcluded()
    {
        var verdict = MaterialityRules.Judge(
            "NFLX", "Netflix, Inc.", "Paramount-Warner Bros merger talks resume", "");

        Assert.Equal(RelevanceDecisions.Uncertain, verdict.Decision);
        Assert.False(RelevanceService.PassesGate(new ArticleRelevance { Decision = verdict.Decision }));
    }
    private static RelevanceService RuleOnlySut(StockTimeMachineDbContext db) =>
        new(new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
            new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
            new DisabledGeminiStub(),
            new GdeltNewsProvider(new HttpClient(),
                NullLogger<GdeltNewsProvider>.Instance,
                new ConfigurationBuilder().Build()),
            NullLogger<RelevanceService>.Instance);

    // AI off: every candidate gets a real RULE verdict — full coverage, no
    // unknowns, only material articles admitted.
    [Fact]
    public async Task ClassifyAsync_AiOff_FullRuleCoverage()
    {
        var db = NewDb();
        var sut = RuleOnlySut(db);
        var docs = new[]
        {
            Doc("r1", "Netflix earnings beat expectations"),
            Doc("u1", "Mackenzie Shirilla lands prison job", "Case featured in a Netflix documentary"),
            Doc("i1", "Cocaine dealers sentenced in Frankfurt drug ring"),
        };

        var map = await sut.ClassifyAsync("NFLX", new DateOnly(2026, 6, 15),
            "Netflix, Inc.", "Communication Services", docs);

        Assert.Equal(3, map.Count);
        Assert.All(map.Values, v => Assert.Equal(RelevanceSources.Rule, v.DecisionSource));
        Assert.True(RelevanceService.PassesGate(map["r1"]));
        Assert.False(RelevanceService.PassesGate(map["u1"]));
        Assert.False(RelevanceService.PassesGate(map["i1"]));
    }

    // A persistence outage must not open the gate: fresh verdicts apply to
    // the current request even when the cache store throws.
    [Fact]
    public async Task ClassifyAsync_StoreFailure_VerdictsStillApply()
    {
        var repo = new Mock<IHistoricalDataRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetRelevance(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArticleRelevance?)null);
        repo.Setup(r => r.StoreRelevances(It.IsAny<IEnumerable<ArticleRelevance>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));
        var sut = new RelevanceService(repo.Object, repo.Object,
            new FuncGeminiStub(_ => new[]
            {
                new RelevanceVerdict { Id = "a1", Relevant = true, Category = "FINANCIAL", Confidence = 0.9, Reason = "R" },
            }),
            new GdeltNewsProvider(new HttpClient(),
                NullLogger<GdeltNewsProvider>.Instance,
                new ConfigurationBuilder().Build()),
            NullLogger<RelevanceService>.Instance);

        var map = await sut.ClassifyAsync("NFLX", new DateOnly(2026, 6, 15),
            "Netflix, Inc.", null, new[] { Doc("a1", "Netflix earnings beat") });

        Assert.True(map.TryGetValue("a1", out var row));
        Assert.Equal(RelevanceDecisions.Relevant, row.Decision);
    }

    // USER verdicts are final: AI re-judgment neither returns nor stores over
    // them.
    [Fact]
    public async Task ClassifyAsync_UserVerdict_NeverOverwritten()
    {
        var db = NewDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);
        await repo.StoreRelevances(new[]
        {
            new ArticleRelevance
            {
                ArticleId = "a1", Symbol = "NFLX", Model = "user", Relevant = true,
                Category = "FINANCIAL", Confidence = 1.0, Reason = "Approved by reviewer.",
                Decision = RelevanceDecisions.UserApproved, DecisionSource = RelevanceSources.User,
            },
        });
        var sut = new RelevanceService(repo, repo,
            new FuncGeminiStub(_ => new[]
            {
                new RelevanceVerdict { Id = "a1", Relevant = false, Category = "UNRELATED", Confidence = 0.9, Reason = "AI disagrees." },
            }),
            new GdeltNewsProvider(new HttpClient(),
                NullLogger<GdeltNewsProvider>.Instance,
                new ConfigurationBuilder().Build()),
            NullLogger<RelevanceService>.Instance);

        var map = await sut.ClassifyAsync("NFLX", new DateOnly(2026, 6, 15),
            "Netflix, Inc.", null, new[] { Doc("a1", "Netflix earnings beat") });

        Assert.Equal(RelevanceDecisions.UserApproved, map["a1"].Decision);
        Assert.Equal(RelevanceSources.User, map["a1"].DecisionSource);
        var stored = await repo.GetRelevance("a1", "NFLX");
        Assert.Equal(RelevanceDecisions.UserApproved, stored!.Decision);
    }

    // RULE placeholders yield to AI once it is back: the verdict upgrades in
    // both the returned map and the store.
    [Fact]
    public async Task ClassifyAsync_RuleUpgradedByAi()
    {
        var db = NewDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);
        await repo.StoreRelevances(new[]
        {
            new ArticleRelevance
            {
                ArticleId = "a1", Symbol = "NFLX", Model = "rule-v1", Relevant = null,
                Category = "UNRELATED", Confidence = 0.4, Reason = "Rule fallback.",
                Decision = RelevanceDecisions.Uncertain, DecisionSource = RelevanceSources.Rule,
            },
        });
        var sut = new RelevanceService(repo, repo,
            new FuncGeminiStub(_ => new[]
            {
                new RelevanceVerdict { Id = "a1", Relevant = true, Category = "FINANCIAL", Confidence = 0.9, Reason = "AI judges relevant." },
            }),
            new GdeltNewsProvider(new HttpClient(),
                NullLogger<GdeltNewsProvider>.Instance,
                new ConfigurationBuilder().Build()),
            NullLogger<RelevanceService>.Instance);

        var map = await sut.ClassifyAsync("NFLX", new DateOnly(2026, 6, 15),
            "Netflix, Inc.", null, new[] { Doc("a1", "Netflix earnings beat") });

        Assert.Equal(RelevanceDecisions.Relevant, map["a1"].Decision);
        Assert.Equal(RelevanceSources.Ai, map["a1"].DecisionSource);
        var stored = await repo.GetRelevance("a1", "NFLX");
        Assert.Equal(RelevanceDecisions.Relevant, stored!.Decision);
    }

    // End to end without AI: garbage never reaches threads, and the census
    // adds up to the evaluated candidates.
    [Fact]
    public async Task Narratives_RuleFallback_ExcludesGarbage()
    {
        var db = NewDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);
        await repo.StoreNews("NFLX", new[]
        {
            Doc("r1", "Netflix earnings beat expectations on subscriber growth"),
            Doc("r2", "Netflix adds 8 million subscribers in fourth quarter"),
            Doc("g1", "Mackenzie Shirilla lands prison job", "Case featured in a Netflix documentary"),
            Doc("g2", "SpaceX IPO could value Elon Musk company high", ""),
            Doc("g3", "Cocaine dealers sentenced in Frankfurt drug ring", ""),
            Doc("g4", "Review: the greatest war movies streaming now", "Including titles on Netflix"),
        });
        var sut = new NarrativeService(repo, repo, new DisabledGeminiStub(), new DisabledBodyStub(),
            NetflixDirectory(), RuleOnlySut(db), NullLogger<NarrativeService>.Instance);

        var result = await sut.GetTopics("NFLX", new DateOnly(2026, 6, 15), NewsSources.Gdelt);

        Assert.Equal(6, result.ArticlesConsidered);
        Assert.Equal(2, result.RelevantCount);
        Assert.Equal(6, result.RelevantCount + result.IrrelevantCount + result.UncertainCount);
        var clustered = result.Topics.SelectMany(t => t.ArticleIds).ToList();
        Assert.DoesNotContain(clustered, id => id == "g1");
        Assert.DoesNotContain(clustered, id => id == "g2");
        Assert.DoesNotContain(clustered, id => id == "g3");
        Assert.DoesNotContain(clustered, id => id == "g4");
    }
}
