using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using StockTimeMachine;

namespace StockTimeMachine.Tests;

public sealed class DisabledGeminiStub : IGeminiClient
{
    public bool IsEnabled => false;
    public string SummaryModel => "stub";
    public string EmbeddingModel => "stub-embed";
    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
        throw new InvalidOperationException("disabled");
    public Task<ClusterBrief?> SummarizeClusterAsync(string prompt, CancellationToken ct = default) =>
        Task.FromResult<ClusterBrief?>(null);
    public Task<IReadOnlyList<NoteIssue>> ReviewNoteAsync(string prompt, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<NoteIssue>>(Array.Empty<NoteIssue>());
    public Task<IReadOnlyList<RelevanceVerdict>> ClassifyRelevanceAsync(string prompt, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RelevanceVerdict>>(Array.Empty<RelevanceVerdict>());
    // Interface extension (reason: hype filing structured extraction needs
    // raw JSON generation): disabled stub returns null like production.
    public Task<string?> GenerateJsonAsync(string prompt, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);
}

public sealed class DisabledBodyStub : IArticleContentClient
{
    public bool IsEnabled => false;
    public Task<ArticleBody?> FetchBodyAsync(string articleUrl, CancellationToken ct = default) =>
        Task.FromResult<ArticleBody?>(null);
}

public sealed class DisabledRelevanceStub : IRelevanceService
{
    public Task<IReadOnlyDictionary<string, ArticleRelevance>> ClassifyAsync(
        string symbol, DateOnly asOfDate, string? companyName, string? sector,
        IReadOnlyList<NewsArticle> articles, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<string, ArticleRelevance>>(
            new Dictionary<string, ArticleRelevance>());
    public bool PassesGate(ArticleRelevance? row) => true;
    public Task<ExpansionResult> ExpandAsync(
        string symbol, DateOnly asOfDate, string? companyName, CancellationToken ct = default) =>
        Task.FromResult(new ExpansionResult());
    public Task<IReadOnlyList<ArticleRelevance>> CandidatesAsync(
        string symbol, DateOnly asOfDate, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ArticleRelevance>>(Array.Empty<ArticleRelevance>());
    public Task<bool> ApproveAsync(string symbol, string articleId, CancellationToken ct = default) =>
        Task.FromResult(false);
    public Task<bool> RejectAsync(string symbol, string articleId, CancellationToken ct = default) =>
        Task.FromResult(false);
}

public sealed class FixedRelevanceStub : IRelevanceService
{
    public List<string> SeenCompanies { get; } = new();
    public Task<IReadOnlyDictionary<string, ArticleRelevance>> ClassifyAsync(
        string symbol, DateOnly asOfDate, string? companyName, string? sector,
        IReadOnlyList<NewsArticle> articles, CancellationToken ct = default)
    {
        SeenCompanies.Add($"{companyName}|{sector}");
        return Task.FromResult<IReadOnlyDictionary<string, ArticleRelevance>>(
            articles.ToDictionary(
                a => a.Id,
                a =>
                {
                    var relevant = !a.Title.Contains("noise", StringComparison.OrdinalIgnoreCase);
                    return new ArticleRelevance
                    {
                        ArticleId = a.Id, Symbol = symbol, Model = "stub",
                        Relevant = relevant,
                        Decision = relevant ? RelevanceDecisions.Relevant : RelevanceDecisions.Irrelevant,
                        DecisionSource = RelevanceSources.Rule,
                        Category = "FINANCIAL", Confidence = 0.9, Reason = "Stub.",
                    };
                }));
    }
    public bool PassesGate(ArticleRelevance? row) => RelevanceService.PassesGate(row);
    public Task<ExpansionResult> ExpandAsync(
        string symbol, DateOnly asOfDate, string? companyName, CancellationToken ct = default) =>
        Task.FromResult(new ExpansionResult());
    public Task<IReadOnlyList<ArticleRelevance>> CandidatesAsync(
        string symbol, DateOnly asOfDate, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ArticleRelevance>>(Array.Empty<ArticleRelevance>());
    public Task<bool> ApproveAsync(string symbol, string articleId, CancellationToken ct = default) =>
        Task.FromResult(false);
    public Task<bool> RejectAsync(string symbol, string articleId, CancellationToken ct = default) =>
        Task.FromResult(false);
}

public static class TestDirectory
{
    public static ICompanyDirectory Tesla() => new StubCompanyDirectory(
        new CompanyInfo("TSLA", "Tesla, Inc.", "0001318605", "NASDAQ", "Consumer Discretionary", "Automobiles"));
}

public sealed class DisabledSentimentStub : IFinancialSentimentAnalyzer
{
    public bool IsEnabled => false;
    public string ModelId => "ProsusAI/finbert";
    public Task<IReadOnlyList<ScoredArticle>> EnsureScoredAsync(
        IReadOnlyList<NewsArticle> articles, DateOnly cutoff, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ScoredArticle>>(Array.Empty<ScoredArticle>());
}

public sealed class FuncGeminiStub : IGeminiClient
{
    private readonly Func<string, IReadOnlyList<RelevanceVerdict>> _classify;
    private readonly Func<string, string?>? _json;
    public List<string> SeenPrompts { get; } = new();
    public FuncGeminiStub(Func<string, IReadOnlyList<RelevanceVerdict>> classify, Func<string, string?>? json = null)
    {
        _classify = classify;
        _json = json;
    }
    public bool IsEnabled => true;
    public string SummaryModel => "stub-flash";
    public string EmbeddingModel => "stub-embed";
    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
        throw new InvalidOperationException("not used");
    public Task<ClusterBrief?> SummarizeClusterAsync(string prompt, CancellationToken ct = default) =>
        Task.FromResult<ClusterBrief?>(null);
    public Task<IReadOnlyList<NoteIssue>> ReviewNoteAsync(string prompt, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<NoteIssue>>(Array.Empty<NoteIssue>());
    public Task<IReadOnlyList<RelevanceVerdict>> ClassifyRelevanceAsync(string prompt, CancellationToken ct = default)
    {
        SeenPrompts.Add(prompt);
        return Task.FromResult(_classify(prompt));
    }
    public Task<string?> GenerateJsonAsync(string prompt, CancellationToken ct = default) =>
        Task.FromResult(_json?.Invoke(prompt));
}

public class AiNarrativeTests
{
    private sealed class FixedGeminiStub : IGeminiClient
    {
        public bool IsEnabled => true;
        public string SummaryModel => "stub-flash";
        public string EmbeddingModel => "stub-embed";
        public List<string> SeenPrompts { get; } = new();
        public int EmbedCalls { get; private set; }
        // a's ~ [1,0], b's ~ [0,1]: pairs merge within groups only.
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            EmbedCalls++;
            return Task.FromResult<IReadOnlyList<float[]>>(texts.Select((t, i) =>
                (i % 2 == 0 ? new float[] { 1f, 0.05f } : new float[] { 0.05f, 1f })).ToList());
        }
        public Task<ClusterBrief?> SummarizeClusterAsync(string prompt, CancellationToken ct = default)
        {
            SeenPrompts.Add(prompt);
            return Task.FromResult<ClusterBrief?>(new ClusterBrief
            {
                Summary = "Stub summary.",
                KeyPoints = new List<string> { "Stub point [1]." },
                Model = "stub-flash",
            });
        }
        public Task<IReadOnlyList<NoteIssue>> ReviewNoteAsync(string prompt, CancellationToken ct = default)
        {
            SeenPrompts.Add(prompt);
            return Task.FromResult<IReadOnlyList<NoteIssue>>(new[]
            {
                new NoteIssue { Ref = "move 2020-02-01", Verdict = "supported", Detail = "Stub check." },
            });
        }
        public Task<IReadOnlyList<RelevanceVerdict>> ClassifyRelevanceAsync(string prompt, CancellationToken ct = default)
        {
            SeenPrompts.Add(prompt);
            // Relevant unless the model-side input carried a noise marker.
            var relevant = !prompt.Contains("noise-marker");
            return Task.FromResult<IReadOnlyList<RelevanceVerdict>>(new[]
            {
                new RelevanceVerdict { Id = "r1", Relevant = relevant, Category = "FINANCIAL", Confidence = 0.9, Reason = "Stub." },
            });
        }
        public Task<string?> GenerateJsonAsync(string prompt, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class ThrowingGeminiStub : IGeminiClient
    {
        public bool IsEnabled => true;
        public string SummaryModel => "stub";
        public string EmbeddingModel => "stub-embed";
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            throw new HttpRequestException("Gemini down");
        public Task<ClusterBrief?> SummarizeClusterAsync(string prompt, CancellationToken ct = default) =>
            Task.FromResult<ClusterBrief?>(null);
        public Task<IReadOnlyList<NoteIssue>> ReviewNoteAsync(string prompt, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<NoteIssue>>(Array.Empty<NoteIssue>());
        public Task<IReadOnlyList<RelevanceVerdict>> ClassifyRelevanceAsync(string prompt, CancellationToken ct = default) =>
            throw new HttpRequestException("Gemini down");
        public Task<string?> GenerateJsonAsync(string prompt, CancellationToken ct = default) =>
            throw new HttpRequestException("Gemini down");
    }

    private static StockTimeMachineDbContext NewDb() => new(
        new DbContextOptionsBuilder<StockTimeMachineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task SeedPair(StockTimeMachineDbContext db)
    {
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);
        await repo.StoreNews("TSLA", new[]
        {
            new NewsArticle { Id = "a1", Title = "Tesla quarterly earnings beat", Description = "Record quarter", Source = "GDELT", PublishedAt = new DateTime(2020, 1, 10), Url = "https://example.com/a1", CompanySymbol = "TSLA" },
            new NewsArticle { Id = "a2", Title = "Tesla earnings smash records quarterly", Description = "Profit record", Source = "GDELT", PublishedAt = new DateTime(2020, 1, 11), Url = "https://example.com/a2", CompanySymbol = "TSLA" },
            new NewsArticle { Id = "b1", Title = "Tesla factory fire halts Berlin line", Description = "Blaze contained", Source = "GDELT", PublishedAt = new DateTime(2020, 1, 10), Url = "https://example.com/b1", CompanySymbol = "TSLA" },
            new NewsArticle { Id = "b2", Title = "Berlin blaze stops Tesla assembly", Description = "Production halted", Source = "GDELT", PublishedAt = new DateTime(2020, 1, 12), Url = "https://example.com/b2", CompanySymbol = "TSLA" },
        });
    }

    [Fact]
    public async Task NarrativeService_AiPath_ClustersByEmbeddingAndBriefs()
    {
        var db = NewDb();
        await SeedPair(db);
        var gemini = new FixedGeminiStub();
        var sut = new NarrativeService(
            new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
            new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
            gemini, new DisabledBodyStub(), TestDirectory.Tesla(), new DisabledRelevanceStub(), NullLogger<NarrativeService>.Instance);

        var result = await sut.GetTopics("TSLA", new DateOnly(2020, 1, 15), NewsSources.Gdelt);

        Assert.Equal("gemini-embeddings", result.ClusteringMethod);
        Assert.Equal(2, result.Topics.Count);
        Assert.All(result.Topics, t =>
        {
            Assert.Equal(2, t.ArticleIds.Count);
            Assert.NotNull(t.Brief);
            Assert.Equal("stub-flash", t.Brief!.Model);
        });
        // Every brief prompt carries the cutoff and the containment rules.
        Assert.All(gemini.SeenPrompts, p =>
        {
            Assert.Contains("2020-01-15", p);
            Assert.Contains("NEVER state or imply causation", p);
            Assert.Contains("NEVER predict", p);
        });
    }

    [Fact]
    public async Task NarrativeService_GeminiFailure_FallsBackToTfIdf()
    {
        var db = NewDb();
        await SeedPair(db);
        var sut = new NarrativeService(
            new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
            new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
            new ThrowingGeminiStub(), new DisabledBodyStub(), TestDirectory.Tesla(), new DisabledRelevanceStub(), NullLogger<NarrativeService>.Instance);

        var result = await sut.GetTopics("TSLA", new DateOnly(2020, 1, 15), NewsSources.Gdelt);

        Assert.Equal("tf-idf-fallback", result.ClusteringMethod);
        Assert.NotEmpty(result.Topics);
        Assert.All(result.Topics, t => Assert.Null(t.Brief));
    }

    [Fact]
    public async Task NarrativeService_GeminiDisabled_UsesTfIdfDirectly()
    {
        var db = NewDb();
        await SeedPair(db);
        var sut = new NarrativeService(
            new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
            new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
            new DisabledGeminiStub(), new DisabledBodyStub(), TestDirectory.Tesla(), new DisabledRelevanceStub(), NullLogger<NarrativeService>.Instance);

        var result = await sut.GetTopics("TSLA", new DateOnly(2020, 1, 15), NewsSources.Gdelt);

        Assert.Equal("tf-idf-fallback", result.ClusteringMethod);
        Assert.NotEmpty(result.Topics);
    }

    [Fact]
    public async Task NarrativeService_ClassifiesEntireInput_NoGateCap()
    {
        // 180 cached articles (> the removed 150 cap): every one must be
        // evaluated, and the census must sum to the evaluated total.
        var db = NewDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);
        await repo.StoreNews("TSLA", Enumerable.Range(1, 180).Select(i =>
            new NewsArticle
            {
                Id = $"g{i}", Title = $"Tesla story number {i} earnings quarter",
                Description = "Cached body", Source = "GDELT",
                PublishedAt = new DateTime(2020, 1, 10), Url = $"https://example.com/g{i}",
                CompanySymbol = "TSLA",
            }));
        var sut = new NarrativeService(repo, repo,
            new DisabledGeminiStub(), new DisabledBodyStub(), TestDirectory.Tesla(),
            new FixedRelevanceStub(), NullLogger<NarrativeService>.Instance);

        var result = await sut.GetTopics("TSLA", new DateOnly(2020, 1, 15), NewsSources.Gdelt);

        Assert.Equal(180, result.ArticlesConsidered);
        Assert.Equal(180, result.ArticlesEvaluated);
        Assert.Equal(180, result.RelevantCount + result.IrrelevantCount + result.UncertainCount);
        Assert.Equal(180, result.RelevantCount);
        Assert.Equal(180, result.ArticlesClustered);
    }

    [Fact]
    public async Task NarrativeService_EmbeddingCeilingIs400()
    {
        // 410 relevant articles: classification covers all 410, while the
        // embedding-cost ceiling caps clustering at 400 (disclosed, not silent).
        var db = NewDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);
        await repo.StoreNews("TSLA", Enumerable.Range(1, 410).Select(i =>
            new NewsArticle
            {
                Id = $"h{i}", Title = $"Tesla story number {i} earnings quarter",
                Description = "Cached body", Source = "GDELT",
                PublishedAt = new DateTime(2020, 1, 10), Url = $"https://example.com/h{i}",
                CompanySymbol = "TSLA",
            }));
        var sut = new NarrativeService(repo, repo,
            new DisabledGeminiStub(), new DisabledBodyStub(), TestDirectory.Tesla(),
            new FixedRelevanceStub(), NullLogger<NarrativeService>.Instance);

        var result = await sut.GetTopics("TSLA", new DateOnly(2020, 1, 15), NewsSources.Gdelt);

        Assert.Equal(410, result.ArticlesConsidered);
        Assert.Equal(410, result.ArticlesEvaluated);
        Assert.Equal(410, result.RelevantCount);
        Assert.Equal(400, result.ArticlesClustered);
        Assert.True(result.RelevantCount > 400);
    }

    [Fact]
    public async Task BriefSharedThread_MatchesAcrossSymbols()
    {
        var db = NewDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);
        await repo.StoreNews("AAA", new[]
        {
            new NewsArticle { Id = "a1", Title = "Data center water approvals contested", Description = "Regulators pause", Source = "GDELT", PublishedAt = new DateTime(2020, 1, 10), Url = "https://example.com/a1", CompanySymbol = "AAA" },
        });
        await repo.StoreNews("BBB", new[]
        {
            new NewsArticle { Id = "b1", Title = "Approvals sought for new data center", Description = "Water review", Source = "GDELT", PublishedAt = new DateTime(2020, 1, 11), Url = "https://example.com/b1", CompanySymbol = "BBB" },
        });
        var gemini = new FixedGeminiStub();
        var sut = new NarrativeService(repo, repo, gemini,
            new DisabledBodyStub(), TestDirectory.Tesla(), new DisabledRelevanceStub(), NullLogger<NarrativeService>.Instance);

        var brief = await sut.BriefSharedThread(
            new[] { "AAA", "BBB" }, new DateOnly(2020, 1, 15), NewsSources.Gdelt,
            new[] { "data", "center", "approvals" });

        Assert.NotNull(brief);
        Assert.Equal("stub-flash", brief!.Model);
        Assert.Single(gemini.SeenPrompts);
        Assert.Contains("NEVER pool", gemini.SeenPrompts[0]);
    }

    [Fact]
    public async Task BriefSharedThread_NoMatch_ReturnsNull()
    {
        var db = NewDb();
        await SeedPair(db);
        var sut = new NarrativeService(
            new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
            new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
            new FixedGeminiStub(), new DisabledBodyStub(), TestDirectory.Tesla(), new DisabledRelevanceStub(), NullLogger<NarrativeService>.Instance);

        // "zzzqqq" appears nowhere: no match, no Gemini call.
        var brief = await sut.BriefSharedThread(
            new[] { "TSLA" }, new DateOnly(2020, 1, 15), NewsSources.Gdelt,
            new[] { "zzzqqq" });

        Assert.Null(brief);
    }

    private sealed class StubMoves : IMoveDetectionService
    {
        public Task<MovesWindow> GetMoves(string symbol, DateOnly asOfDate, string? newsSource = null, CancellationToken ct = default, IProgress<SnapshotProgress>? progress = null, int? topMoves = null) =>
            Task.FromResult(new MovesWindow
            {
                CompanySymbol = symbol,
                DecisionDate = asOfDate,
                Uncertainty = new UncertaintyIndex
                {
                    Score = 55.0,
                    Components = new List<UncertaintyComponent>
                    {
                        new() { Name = "evidence-sparsity", Weight = 0.4, Value = 0.5, Detail = "half covered" },
                    },
                },
                KeyMoves = new List<KeyMove>
                {
                    new() { Date = new DateOnly(2020, 2, 1), DailyReturnPct = 5.0m, Flags = new List<string> { "spike" }, SentimentDirection = "unknown" },
                },
            });
    }

    private static CopilotService Copilot(
        StockTimeMachineDbContext db, IGeminiClient gemini) =>
        new(new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
            new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
            new StubMoves(), gemini, new DisabledBodyStub(),
            new Mock<IHypeFilingService>().Object,
            NullLogger<CopilotService>.Instance);

    [Fact]
    public async Task Copilot_Contrast_NeedsTwoArticles()
    {
        var db = NewDb();
        var sut = Copilot(db, new FixedGeminiStub());

        // Zero cached articles: null, not an error.
        Assert.Null(await sut.ContrastArticles("TSLA", new DateOnly(2020, 1, 15), NewsSources.Gdelt, new[] { "x", "y" }));
    }

    [Fact]
    public async Task Copilot_Actions_UseContainmentPrompt()
    {
        var db = NewDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);
        await repo.StoreNews("TSLA", new[]
        {
            new NewsArticle { Id = "a1", Title = "Alpha story one", Description = "Body one", Source = "GDELT", PublishedAt = new DateTime(2020, 1, 10), Url = "https://example.com/a1", CompanySymbol = "TSLA" },
            new NewsArticle { Id = "a2", Title = "Alpha story two", Description = "Body two", Source = "GDELT", PublishedAt = new DateTime(2020, 1, 11), Url = "https://example.com/a2", CompanySymbol = "TSLA" },
        });
        await db.SecFilings.AddAsync(new SecFiling
        {
            CompanySymbol = "TSLA", FormType = "10-K",
            FiledAt = new DateTime(2020, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            AccessionNumber = "acc1", Url = "https://example.com/f1", Summary = "Annual report",
        });
        await db.SaveChangesAsync();
        var gemini = new FixedGeminiStub();
        var sut = Copilot(db, gemini);

        var contrast = await sut.ContrastArticles("TSLA", new DateOnly(2020, 1, 15), NewsSources.Gdelt, new[] { "a1", "a2" });
        var filings = await sut.SummarizeFilings("TSLA", new DateOnly(2020, 1, 15));
        var explain = await sut.ExplainUncertainty("TSLA", new DateOnly(2020, 1, 15), NewsSources.Gdelt);
        var gist = await sut.GistThread("TSLA", new DateOnly(2020, 1, 15), NewsSources.Gdelt, new[] { "a1" });

        Assert.NotNull(contrast);
        Assert.NotNull(filings);
        Assert.NotNull(explain);
        Assert.NotNull(gist);
        Assert.All(gemini.SeenPrompts, p =>
        {
            Assert.Contains("2020-01-15", p);
            Assert.Contains("NEVER predict", p);
        });
        Assert.Contains(gemini.SeenPrompts, p => p.Contains("DISAGREE"));
        Assert.Contains(gemini.SeenPrompts, p => p.Contains("plain words"));
    }

    [Fact]
    public async Task Copilot_FilingsSummary_UsesStoredContent()
    {
        // Blocker fix: the drawer fed the always-empty Summary metadata
        // column. With a stored summary row present, the prompt must carry
        // real findings instead.
        var db = NewDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);
        await db.SecFilings.AddAsync(new SecFiling
        {
            CompanySymbol = "TSLA", FormType = "8-K",
            FiledAt = new DateTime(2020, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            AccessionNumber = "acc9", Url = "https://example.com/f9", Summary = "",
        });
        await repo.StoreFilingSummary(new FilingSummaryRecord
        {
            AccessionNumber = "acc9", FormType = "8-K",
            StructuredJson = """{"event_type":"management_change"}""",
            Findings = "CEO resigned effective immediately.",
            Disclosures = "None stated.", ConfidenceNote = "full", ContentHash = "h",
        });
        await db.SaveChangesAsync();
        var gemini = new FixedGeminiStub();
        var sut = Copilot(db, gemini);

        var filings = await sut.SummarizeFilings("TSLA", new DateOnly(2020, 1, 15));

        Assert.NotNull(filings);
        Assert.Contains(gemini.SeenPrompts, p => p.Contains("CEO resigned effective immediately."));
    }

    [Fact]
    public async Task Copilot_Review_ReturnsVerdicts()
    {
        var db = NewDb();
        var sut = Copilot(db, new FixedGeminiStub());

        var issues = await sut.ReviewNote("TSLA", new DateOnly(2020, 1, 15), NewsSources.Gdelt,
            "Move [move 2020-02-01] was large.");

        var single = Assert.Single(issues);
        Assert.Equal("supported", single.Verdict);
    }

    [Fact]
    public async Task Copilot_DisabledAi_ReturnsNull()
    {
        var db = NewDb();
        var sut = Copilot(db, new DisabledGeminiStub());

        Assert.Null(await sut.SummarizeFilings("TSLA", new DateOnly(2020, 1, 15)));
        Assert.Empty(await sut.ReviewNote("TSLA", new DateOnly(2020, 1, 15), NewsSources.Gdelt, "Note."));
    }

    // Scripted embeddings: dequeues one vector per requested text, in order.
    // Full geometric control per test (the shared FixedGeminiStub alternates
    // two fixed vectors, which cannot express distinct thread shapes).
    private sealed class ScriptedGeminiStub : IGeminiClient
    {
        private readonly Queue<float[]> _vectors;
        public ScriptedGeminiStub(IEnumerable<float[]> vectors) => _vectors = new Queue<float[]>(vectors);
        public bool IsEnabled => true;
        public string SummaryModel => "stub-flash";
        public string EmbeddingModel => "stub-embed";
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => _vectors.Dequeue()).ToList());
        public Task<ClusterBrief?> SummarizeClusterAsync(string prompt, CancellationToken ct = default) =>
            Task.FromResult<ClusterBrief?>(null);
        public Task<IReadOnlyList<NoteIssue>> ReviewNoteAsync(string prompt, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<NoteIssue>>(Array.Empty<NoteIssue>());
        public Task<IReadOnlyList<RelevanceVerdict>> ClassifyRelevanceAsync(string prompt, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RelevanceVerdict>>(Array.Empty<RelevanceVerdict>());
        public Task<string?> GenerateJsonAsync(string prompt, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
    }

    private static NarrativeService CrossSut(
        StockTimeMachineDbContext db, IGeminiClient gemini) => new(
        new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
        new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
        gemini, new DisabledBodyStub(), TestDirectory.Tesla(),
        new DisabledRelevanceStub(), NullLogger<NarrativeService>.Instance);

    private static NewsArticle Doc(string id, string symbol, string title, string day, string? url = null) => new()
    {
        Id = id,
        Title = title,
        Description = "d",
        Source = "GDELT",
        PublishedAt = DateTime.Parse(day, null,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal),
        Url = url ?? $"https://example.com/{id}",
        CompanySymbol = symbol,
    };

    [Fact]
    public async Task CrossThreadSimilarity_ThreadToThreadShape()
    {
        // Geometry: AAA {a1=(1,0), a2=(0.9,0.4359)} one cluster (cos 0.9);
        // BBB {b1=(0.95,0.3122), b2=(0.7,0.7141)} one cluster (cos 0.888).
        // Cross max a2-b1 = 0.991 (discovery score); cross mean = 0.896.
        var db = NewDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);
        await repo.StoreNews("AAA", new[]
        {
            Doc("a1", "AAA", "Alpha one", "2020-01-10"),
            Doc("a2", "AAA", "Alpha two", "2020-01-11"),
        });
        await repo.StoreNews("BBB", new[]
        {
            Doc("b1", "BBB", "Beta one", "2020-01-10"),
            Doc("b2", "BBB", "Beta two", "2020-01-12"),
        });
        var sut = CrossSut(db, new ScriptedGeminiStub(new[]
        {
            new float[] { 1f, 0f }, new float[] { 0.9f, 0.4359f },
            new float[] { 0.95f, 0.3122f }, new float[] { 0.7f, 0.7141f },
        }));

        var result = await sut.CrossThreadSimilarity(
            new[] { "AAA", "BBB" }, new DateOnly(2020, 1, 15), NewsSources.Gdelt);

        var pair = Assert.Single(result.Pairs);
        Assert.Equal(0, result.DuplicatePairsSkipped);
        // Discovery score (max distinct-id pair), thread-level mean, and
        // per-thread cohesion are separate dimensions, not one blended score.
        Assert.Equal(0.991, pair.Similarity, 3);
        Assert.Equal(0.896, pair.MeanSimilarity, 3);
        Assert.Equal(0.9, pair.CohesionA!.Value, 3);
        Assert.Equal(0.888, pair.CohesionB!.Value, 3);
        // Vocabulary overlap is reported alongside, never as the ranking.
        Assert.Equal(new[] { "one", "two" }, pair.SharedTerms);
        // Full membership preserved with canonical URLs verbatim (member
        // order follows merge order, not doc order — compare as sets).
        Assert.Equal(new[] { "a1", "a2" }, pair.AMembers.Select(m => m.Id).OrderBy(id => id).ToArray());
        Assert.Equal(new[] { "b1", "b2" }, pair.BMembers.Select(m => m.Id).OrderBy(id => id).ToArray());
        Assert.All(pair.AMembers.Concat(pair.BMembers),
            m => Assert.Equal($"https://example.com/{m.Id}", m.Url));
    }

    [Fact]
    public async Task CrossThreadSimilarity_MatchIgnoresRepresentativeTitles()
    {
        // The longest titles (representatives) are a cold pair (cos 0.6);
        // the match must still surface via the hot pair (0.991) while the
        // displayed titles stay the representatives. Representative-only
        // matching would miss this pair entirely.
        var db = NewDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);
        await repo.StoreNews("AAA", new[]
        {
            Doc("a1", "AAA", "Zebra alpha yakking quantum vectors", "2020-01-10"),
            Doc("a2", "AAA", "Chip pact", "2020-01-11"),
        });
        await repo.StoreNews("BBB", new[]
        {
            Doc("b1", "BBB", "Brief note on semiconductors broadly defined here", "2020-01-10"),
            Doc("b2", "BBB", "Chip deal", "2020-01-12"),
        });
        var sut = CrossSut(db, new ScriptedGeminiStub(new[]
        {
            new float[] { 1f, 0f }, new float[] { 0.9f, 0.4359f },
            new float[] { 0.6f, 0.8f }, new float[] { 0.95f, 0.3122f },
        }));

        var result = await sut.CrossThreadSimilarity(
            new[] { "AAA", "BBB" }, new DateOnly(2020, 1, 15), NewsSources.Gdelt);

        var pair = Assert.Single(result.Pairs);
        Assert.Equal("Zebra alpha yakking quantum vectors", pair.ATitle);
        Assert.Equal("Brief note on semiconductors broadly defined here", pair.BTitle);
        Assert.True(pair.MeanSimilarity < pair.Similarity);
    }

    [Fact]
    public async Task CrossThreadSimilarity_IdenticalContentSkippedAndCounted()
    {
        // The live 1.000 NVDA/AAPL case: identical title+description embed
        // identically (cosine exactly 1.0) under different ids/URLs. Same
        // wire story twice is not a cross-company relationship.
        var db = NewDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);
        await repo.StoreNews("AAA", new[]
        {
            Doc("a1", "AAA", "Apple rolls out new, AI-powered Siri", "2020-01-10", "https://outlet-a.example/siri"),
        });
        await repo.StoreNews("BBB", new[]
        {
            Doc("b1", "BBB", "Apple rolls out new, AI-powered Siri", "2020-01-10", "https://outlet-b.example/siri"),
        });
        var same = new float[] { 1f, 0f };
        var sut = CrossSut(db, new ScriptedGeminiStub(new[] { same, same }));

        var result = await sut.CrossThreadSimilarity(
            new[] { "AAA", "BBB" }, new DateOnly(2020, 1, 15), NewsSources.Gdelt);

        Assert.Empty(result.Pairs);
        Assert.Equal(1, result.DuplicatePairsSkipped);
    }

    [Fact]
    public async Task CrossThreadSimilarity_SameCachedRowSkippedSilently()
    {
        // One cached row visible under both symbols joins at 1.0 by
        // construction and proves nothing (same rule as the hype join).
        var db = NewDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);
        await repo.StoreNews("AAA", new[] { Doc("shared", "AAA", "Shared wire story", "2020-01-10") });
        await repo.StoreNews("BBB", new[] { Doc("shared", "BBB", "Shared wire story", "2020-01-10") });
        var same = new float[] { 1f, 0f };
        var sut = CrossSut(db, new ScriptedGeminiStub(new[] { same, same }));

        var result = await sut.CrossThreadSimilarity(
            new[] { "AAA", "BBB" }, new DateOnly(2020, 1, 15), NewsSources.Gdelt);

        Assert.Empty(result.Pairs);
        Assert.Equal(0, result.DuplicatePairsSkipped);
    }

    [Fact]
    public async Task CrossThreadSimilarity_PostCutoffRowsNeverEnter()
    {
        var db = NewDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);
        await repo.StoreNews("AAA", new[]
        {
            Doc("a1", "AAA", "Alpha one", "2020-01-10"),
            Doc("a2future", "AAA", "Alpha future", "2020-02-01"),
        });
        await repo.StoreNews("BBB", new[]
        {
            Doc("b1", "BBB", "Beta one", "2020-01-10"),
        });
        var sut = CrossSut(db, new ScriptedGeminiStub(new[]
        {
            new float[] { 1f, 0f }, new float[] { 0.9f, 0.4359f }, new float[] { 0.95f, 0.3122f },
        }));

        var result = await sut.CrossThreadSimilarity(
            new[] { "AAA", "BBB" }, new DateOnly(2020, 1, 15), NewsSources.Gdelt);

        var pair = Assert.Single(result.Pairs);
        Assert.DoesNotContain("a2future", pair.AMembers.Select(m => m.Id));
    }

    [Fact]
    public async Task CrossThreadSimilarity_DisabledAi_ReturnsEmpty()
    {
        var db = NewDb();
        var sut = new NarrativeService(
            new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
            new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
            new DisabledGeminiStub(), new DisabledBodyStub(), TestDirectory.Tesla(), new DisabledRelevanceStub(), NullLogger<NarrativeService>.Instance);

        var result = await sut.CrossThreadSimilarity(
            new[] { "AAA", "BBB" }, new DateOnly(2020, 1, 15), NewsSources.Gdelt);
        Assert.Empty(result.Pairs);
        Assert.Equal(0, result.DuplicatePairsSkipped);
    }

    [Fact]
    public async Task Copilot_Suggest_PhrasesSuppliedGaps()
    {
        var db = NewDb();
        var gemini = new FixedGeminiStub();
        var sut = Copilot(db, gemini);

        var brief = await sut.SuggestNextSteps("TSLA", new DateOnly(2020, 1, 15), NewsSources.Gdelt,
            new[] { "No news for 2 moves [Retry with MarketAux]" });

        Assert.NotNull(brief);
        Assert.Contains(gemini.SeenPrompts, p => p.Contains("No news for 2 moves") && p.Contains("Never invent"));
    }

    [Fact]
    public async Task Copilot_Suggest_EmptyGaps_ReturnsNull()
    {
        var db = NewDb();
        var sut = Copilot(db, new FixedGeminiStub());

        Assert.Null(await sut.SuggestNextSteps("TSLA", new DateOnly(2020, 1, 15), NewsSources.Gdelt,
            Array.Empty<string>()));
    }

    [Fact]
    public void MethodologyRetrieve_RanksRelevantSection()
    {
        var hits = MethodologyContent.Retrieve("why is there no news before my cutoff date");

        Assert.NotEmpty(hits);
        Assert.Contains(hits, h => h.Heading == "News Sources" || h.Heading == "Temporal Boundary");
    }

    [Fact]
    public void MethodologyRetrieve_EmptyQuestion_ReturnsEmpty()
    {
        Assert.Empty(MethodologyContent.Retrieve("!!!"));
    }

    [Fact]
    public async Task Copilot_Explain_GroundedInRetrievedSections()
    {
        var db = NewDb();
        var gemini = new FixedGeminiStub();
        var sut = Copilot(db, gemini);

        var answer = await sut.ExplainMethodology("why is my news empty?", "symbol=MSFT date=2026-07-03");

        Assert.NotNull(answer);
        Assert.NotEmpty(answer!.CitedSections);
        Assert.Contains(gemini.SeenPrompts, p => p.Contains("Answer ONLY from the SECTIONS"));
    }

    [Fact]
    public async Task Copilot_Explain_NoRetrieval_ReturnsRefusal()
    {
        var db = NewDb();
        var gemini = new FixedGeminiStub();
        var sut = Copilot(db, gemini);

        // Gibberish retrieves nothing: refusal without a model call.
        var answer = await sut.ExplainMethodology("zzzqqq xxxwww", null);

        Assert.NotNull(answer);
        Assert.Equal("The methodology does not cover that.", answer!.Answer);
        Assert.Empty(answer.CitedSections);
        Assert.DoesNotContain(gemini.SeenPrompts, p => p.Contains("QUESTION: zzzqqq"));
    }

    [Fact]
    public async Task Copilot_Explain_DisabledAi_ReturnsNull()
    {
        var db = NewDb();
        var sut = Copilot(db, new DisabledGeminiStub());

        Assert.Null(await sut.ExplainMethodology("why empty?", null));
    }

    [Fact]
    public async Task NarrativeService_EmbeddingCache_SecondCallSkipsProvider()
    {
        var db = NewDb();
        await SeedPair(db);
        var gemini = new FixedGeminiStub();
        var sut = new NarrativeService(
            new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
            new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
            gemini, new DisabledBodyStub(), TestDirectory.Tesla(), new DisabledRelevanceStub(), NullLogger<NarrativeService>.Instance);

        var first = await sut.GetTopics("TSLA", new DateOnly(2020, 1, 15), NewsSources.Gdelt);
        var callsAfterFirst = gemini.EmbedCalls;
        Assert.True(callsAfterFirst > 0);
        Assert.Equal("gemini-embeddings", first.ClusteringMethod);

        var second = await sut.GetTopics("TSLA", new DateOnly(2020, 1, 15), NewsSources.Gdelt);

        Assert.Equal(callsAfterFirst, gemini.EmbedCalls);
        Assert.Equal("gemini-embeddings", second.ClusteringMethod);
        Assert.Equal(
            first.Topics.Select(t => string.Join(",", t.ArticleIds)),
            second.Topics.Select(t => string.Join(",", t.ArticleIds)));
    }

    [Fact]
    public async Task EmbeddingRepository_Roundtrips()
    {
        var db = NewDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);

        Assert.Null(await repo.GetEmbedding("x", "m"));
        await repo.StoreEmbedding(new ArticleEmbedding
        {
            ArticleId = "x", Model = "m", VectorJson = "[0.1,0.2]", CachedAt = DateTime.UtcNow,
        });

        var row = await repo.GetEmbedding("x", "m");
        Assert.NotNull(row);
        Assert.Equal("[0.1,0.2]", row!.VectorJson);
        Assert.Null(await repo.GetEmbedding("x", "other-model"));
    }

    [Fact]
    public async Task NarrativeService_Progress_ReportsEmbeddingAndBriefing()
    {
        var db = NewDb();
        await SeedPair(db);
        var gemini = new FixedGeminiStub();
        var sut = new NarrativeService(
            new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
            new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
            gemini, new DisabledBodyStub(), TestDirectory.Tesla(), new DisabledRelevanceStub(), NullLogger<NarrativeService>.Instance);
        var stages = new List<SnapshotProgress>();
        var progress = new Progress<SnapshotProgress>(s => stages.Add(s));

        var result = await sut.GetTopics("TSLA", new DateOnly(2020, 1, 15), NewsSources.Gdelt, progress: progress);

        Assert.Equal("gemini-embeddings", result.ClusteringMethod);
        // Progress<T> posts callbacks asynchronously: poll briefly.
        for (int i = 0; i < 50 && !stages.Any(s => s.Stage == "briefing" && s.State == "complete"); i++)
            await Task.Delay(100);
        Assert.Contains(stages, s => s.Stage == "clustering" && s.State == "started");
        Assert.Contains(stages, s => s.Stage == "embedding" && s.State == "complete");
        Assert.Contains(stages, s => s.Stage == "clustering" && s.State == "complete");
        Assert.Contains(stages, s => s.Stage == "briefing" && s.State == "started");
        Assert.Contains(stages, s => s.Stage == "briefing" && s.State == "complete");
    }

    [Fact]
    public async Task NarrativeService_Progress_EmptyCache_ReportsComplete()
    {
        var db = NewDb();
        var sut = new NarrativeService(
            new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
            new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
            new DisabledGeminiStub(), new DisabledBodyStub(), TestDirectory.Tesla(), new DisabledRelevanceStub(), NullLogger<NarrativeService>.Instance);
        var stages = new List<SnapshotProgress>();
        var progress = new Progress<SnapshotProgress>(s => stages.Add(s));

        var result = await sut.GetTopics("TSLA", new DateOnly(2020, 1, 15), NewsSources.Gdelt, progress: progress);

        Assert.Empty(result.Topics);
        for (int i = 0; i < 50 && !stages.Any(s => s.Stage == "clustering"); i++)
            await Task.Delay(100);
        Assert.Contains(stages, s => s.Stage == "clustering" && s.State == "complete");
    }

    [Fact]
    public void RelevancePrompt_CarriesContextAndRules()
    {
        var prompt = ClusterBriefPromptBuilderCheck();
        Assert.Contains("Tesla, Inc.", prompt);
        Assert.Contains("TSLA", prompt);
        Assert.Contains("2020-01-15", prompt);
        Assert.Contains("Consumer Discretionary", prompt);
        Assert.Contains("[a1] Tesla earnings beat", prompt);
        Assert.Contains("SEMANTIC classification, not keyword matching", prompt);
        Assert.Contains("Never predict, advise, or recommend anything", prompt);
    }

    private static string ClusterBriefPromptBuilderCheck() =>
        RelevancePrompt.Build("Tesla, Inc.", "TSLA", new DateOnly(2020, 1, 15),
            "Consumer Discretionary", new[] { ("a1", "Tesla earnings beat") });

    private static RelevanceService RelevanceSut(
        StockTimeMachineDbContext db, IGeminiClient gemini) =>
        new(new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
            new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
            gemini,
            new GdeltNewsProvider(new HttpClient(),
                NullLogger<GdeltNewsProvider>.Instance,
                new ConfigurationBuilder().Build()),
            NullLogger<RelevanceService>.Instance);

    private static NewsArticle RelArticle(string id, string title) => new()
    {
        Id = id, Title = title, Source = "GDELT",
        PublishedAt = new DateTime(2020, 1, 10), Url = "https://example.com/" + id,
        CompanySymbol = "TSLA",
    };

    [Fact]
    public async Task RelevanceService_MapsVerdictsAndCaches()
    {
        var db = NewDb();
        var gemini = new FuncGeminiStub(_ => new[]
        {
            new RelevanceVerdict { Id = "a1", Relevant = true, Category = "financial", Confidence = 1.5, Reason = "R1" },
            new RelevanceVerdict { Id = "zzz", Relevant = true, Category = "BOGUS", Confidence = 0.9, Reason = "Rz" },
        });
        var sut = RelevanceSut(db, gemini);
        var docs = new[] { RelArticle("a1", "T1"), RelArticle("a2", "T2") };

        var first = await sut.ClassifyAsync("TSLA", new DateOnly(2020, 1, 15),
            "Tesla, Inc.", "Automobiles", docs);

        // a1 mapped (category uppercased, confidence clamped); unknown id
        // ignored; a2 unanswered by the model → RULE fallback (never absent,
        // never guessed relevant: "T2" carries no mention or signal).
        Assert.True(first["a1"].Relevant);
        Assert.Equal("FINANCIAL", first["a1"].Category);
        Assert.Equal(1.0, first["a1"].Confidence);
        Assert.Equal(RelevanceSources.Ai, first["a1"].DecisionSource);
        Assert.True(first.ContainsKey("a2"));
        Assert.Equal(RelevanceSources.Rule, first["a2"].DecisionSource);
        Assert.Equal(RelevanceDecisions.Irrelevant, first["a2"].Decision);
        Assert.Single(gemini.SeenPrompts);

        // Second call served from cache: no new model call.
        var second = await sut.ClassifyAsync("TSLA", new DateOnly(2020, 1, 15),
            "Tesla, Inc.", "Automobiles", docs.Take(1).ToList());
        Assert.Single(gemini.SeenPrompts);
        Assert.True(second["a1"].Relevant);
    }

    [Fact]
    public async Task RelevanceService_DisabledAi_ReturnsRuleCoverage()
    {
        // AI off no longer means "unknown": the deterministic materiality
        // fallback judges every candidate ("T1" — no mention, no signal).
        var db = NewDb();
        var sut = RelevanceSut(db, new DisabledGeminiStub());

        var result = await sut.ClassifyAsync("TSLA", new DateOnly(2020, 1, 15),
            null, null, new[] { RelArticle("a1", "T1") });

        Assert.Single(result);
        Assert.Equal(RelevanceSources.Rule, result["a1"].DecisionSource);
        Assert.Equal(RelevanceDecisions.Irrelevant, result["a1"].Decision);
    }

    [Fact]
    public void RelevanceGate_AdmitsRelevantAndApprovedOnly()
    {
        // The single policy every consumer shares: RELEVANT + USER_APPROVED
        // pass; everything else — including a legacy Relevant flag without a
        // decision, and unknown — stays out.
        Assert.True(RelevanceService.PassesGate(
            new ArticleRelevance { Decision = RelevanceDecisions.Relevant }));
        Assert.True(RelevanceService.PassesGate(
            new ArticleRelevance { Decision = RelevanceDecisions.UserApproved }));
        Assert.False(RelevanceService.PassesGate(
            new ArticleRelevance { Decision = RelevanceDecisions.Irrelevant }));
        Assert.False(RelevanceService.PassesGate(
            new ArticleRelevance { Decision = RelevanceDecisions.Uncertain }));
        Assert.False(RelevanceService.PassesGate(null));
        Assert.False(RelevanceService.PassesGate(new ArticleRelevance { Relevant = true }));
    }

    [Fact]
    public async Task RelevanceService_ApprovalWorkflow()
    {
        var db = NewDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);
        await repo.StoreNews("TSLA", new[] { RelArticle("u1", "T1"), RelArticle("u2", "T2") });
        await repo.StoreRelevances(new[]
        {
            new ArticleRelevance { ArticleId = "u1", Symbol = "TSLA", Model = "t", Decision = RelevanceDecisions.Uncertain, DecisionSource = RelevanceSources.Ai, Category = "FINANCIAL", Confidence = 0.4, Reason = "R1" },
            new ArticleRelevance { ArticleId = "u2", Symbol = "TSLA", Model = "t", Decision = RelevanceDecisions.Uncertain, DecisionSource = RelevanceSources.Ai, Category = "FINANCIAL", Confidence = 0.5, Reason = "R2" },
        });
        var sut = RelevanceSut(db, new DisabledGeminiStub());

        // Candidates surface highest confidence first.
        var cands = await sut.CandidatesAsync("TSLA", new DateOnly(2020, 1, 15));
        Assert.Equal(2, cands.Count);
        Assert.Equal("u2", cands[0].ArticleId);

        // Approval admits to the pipeline with USER provenance...
        Assert.True(await sut.ApproveAsync("TSLA", "u1"));
        var approved = await repo.GetRelevance("u1", "TSLA");
        Assert.Equal(RelevanceDecisions.UserApproved, approved!.Decision);
        Assert.Equal(RelevanceSources.User, approved.DecisionSource);
        Assert.True(RelevanceService.PassesGate(approved));

        // ...rejection excludes, and unknown ids fail honestly.
        Assert.True(await sut.RejectAsync("TSLA", "u2"));
        Assert.False(RelevanceService.PassesGate(await repo.GetRelevance("u2", "TSLA")));
        Assert.False(await sut.ApproveAsync("TSLA", "missing"));
    }

    [Fact]
    public async Task NarrativeService_GateExcludesIrrelevant()
    {
        var db = NewDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);
        await repo.StoreNews("TSLA", new[]
        {
            RelArticle("n1", "Tesla quarterly earnings beat"),
            RelArticle("n2", "Tesla earnings smash records quarterly"),
            RelArticle("noise1", "Market noise daily roundup chatter"),
        });
        var sut = new NarrativeService(repo, repo, new DisabledGeminiStub(), new DisabledBodyStub(),
            TestDirectory.Tesla(), new FixedRelevanceStub(), NullLogger<NarrativeService>.Instance);

        var result = await sut.GetTopics("TSLA", new DateOnly(2020, 1, 15), NewsSources.Gdelt);

        Assert.Equal(3, result.ArticlesConsidered);
        Assert.Equal(2, result.RelevantCount);
        Assert.NotEmpty(result.Topics);
        Assert.DoesNotContain(result.Topics.SelectMany(t => t.ArticleIds), id => id == "noise1");
        Assert.All(result.Topics, t => Assert.NotNull(t.RelevanceRate));
    }

    [Fact]
    public async Task NarrativeService_AttachesThreadRelevance()
    {
        var db = NewDb();
        await SeedPair(db);
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);
        var relevance = new RelevanceService(repo, repo, new FuncGeminiStub(prompt =>
        {
            // Every requested id relevant except titles carrying "fire".
            var ids = new List<string>();
            foreach (var line in prompt.Split('\n'))
            {
                var m = System.Text.RegularExpressions.Regex.Match(line, @"^\[(.+?)\] (.*)$");
                if (m.Success)
                    ids.Add(m.Groups[1].Value + "|" + m.Groups[2].Value);
            }
            return ids.Select(x =>
            {
                var parts = x.Split('|', 2);
                return new RelevanceVerdict
                {
                    Id = parts[0],
                    Relevant = !parts[1].Contains("fire", StringComparison.OrdinalIgnoreCase),
                    Category = "OPERATIONS",
                    Confidence = 0.8,
                    Reason = "Stub.",
                };
            }).ToList();
        }),
            new GdeltNewsProvider(new HttpClient(),
                NullLogger<GdeltNewsProvider>.Instance,
                new ConfigurationBuilder().Build()),
            NullLogger<RelevanceService>.Instance);
        var sut = new NarrativeService(repo, repo, new DisabledGeminiStub(), new DisabledBodyStub(),
            TestDirectory.Tesla(), relevance, NullLogger<NarrativeService>.Instance);

        var result = await sut.GetTopics("TSLA", new DateOnly(2020, 1, 15), NewsSources.Gdelt);

        Assert.NotEmpty(result.Topics);
        Assert.All(result.Topics, t => Assert.NotNull(t.RelevanceRate));
        var withCategory = result.Topics.Where(t => t.RelevanceRate > 0).ToList();
        Assert.All(withCategory, t => Assert.Equal("OPERATIONS", t.TopCategory));
    }

    private static FinBertSentimentAnalyzer NlpAnalyzer(
        StockTimeMachineDbContext db, HttpMessageHandler handler, bool enabled = true)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Nlp:Endpoint"] = "http://127.0.0.1:5252",
            ["Nlp:Enabled"] = enabled ? "true" : "false",
        }).Build();
        return new FinBertSentimentAnalyzer(
            new HttpClient(handler),
            new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
            NullLogger<FinBertSentimentAnalyzer>.Instance, config);
    }

    private const string NlpBatch = """
        {"results": [{"pos": 0.8, "neu": 0.1, "neg": 0.1, "score": 0.7, "confidence": 0.8}], "model": "ProsusAI/finbert", "revision": "r1"}
        """;

    private static NewsArticle NlpArticle(string id, string date) => new()
    {
        Id = id, Title = "T " + id, Description = "Body",
        PublishedAt = DateTime.SpecifyKind(DateTime.Parse(date), DateTimeKind.Utc),
        Url = "https://example.com/" + id, CompanySymbol = "TSLA", Source = "GDELT",
    };

    [Fact]
    public async Task Nlp_ScoresAndCachesByTextHash()
    {
        var db = NewDb();
        var handler = new RoutedHttpMessageHandler()
            .When(_ => true, NlpBatch);
        var sut = NlpAnalyzer(db, handler);

        var first = await sut.EnsureScoredAsync(
            new[] { NlpArticle("a1", "2020-01-10") }, new DateOnly(2020, 1, 15));
        var second = await sut.EnsureScoredAsync(
            new[] { NlpArticle("a1", "2020-01-10") }, new DateOnly(2020, 1, 15));

        var one = Assert.Single(first);
        Assert.Equal(0.7, one.Score);
        Assert.Equal(0.8, one.Confidence);
        Assert.False(one.FromCache);
        Assert.True(Assert.Single(second).FromCache);
        Assert.Equal(1, handler.Calls); // second call served from cache
    }

    [Fact]
    public async Task Nlp_FutureArticles_ExcludedNeverScored()
    {
        var db = NewDb();
        var handler = new RoutedHttpMessageHandler()
            .When(_ => true, NlpBatch);
        var sut = NlpAnalyzer(db, handler);

        var result = await sut.EnsureScoredAsync(
            new[] { NlpArticle("a1", "2020-02-01") }, new DateOnly(2020, 1, 15));

        Assert.Empty(result);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Nlp_SidecarDown_ReturnsCachedOnly()
    {
        var db = NewDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);
        await repo.StoreSentiment(new ArticleSentiment
        {
            ArticleId = "a1", Model = "ProsusAI/finbert", TextHash = FinBertSentimentAnalyzer.TextHash(NlpArticle("a1", "2020-01-10")),
            Pos = 0.8, Neu = 0.1, Neg = 0.1, Score = 0.7, Confidence = 0.8,
        });
        var sut = NlpAnalyzer(db, new RoutedHttpMessageHandler()
            .When(_ => true, "boom", System.Net.HttpStatusCode.InternalServerError));

        var result = await sut.EnsureScoredAsync(
            new[] { NlpArticle("a1", "2020-01-10"), NlpArticle("a2", "2020-01-10") },
            new DateOnly(2020, 1, 15));

        var single = Assert.Single(result); // a1 cached; a2 failed softly, not fabricated
        Assert.Equal("a1", single.ArticleId);
        Assert.True(single.FromCache);
    }

    [Fact]
    public async Task Nlp_Disabled_ReturnsEmptyWithoutHttp()
    {
        var db = NewDb();
        var handler = new RoutedHttpMessageHandler()
            .When(_ => true, NlpBatch);
        var sut = NlpAnalyzer(db, handler, enabled: false);

        Assert.True(!sut.IsEnabled);
        Assert.Empty(await sut.EnsureScoredAsync(
            new[] { NlpArticle("a1", "2020-01-10") }, new DateOnly(2020, 1, 15)));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void EmbeddingClustering_IdenticalVectorsMerge_OrthogonalDoNot()
    {
        var topics = EmbeddingClustering.Cluster(new[]
        {
            new float[] { 1f, 0f },
            new float[] { 1f, 0f },
            new float[] { 0f, 1f },
        });

        Assert.Equal(2, topics.Count);
        Assert.Contains(topics, t => t.Count == 2);
    }

    [Fact]
    public void EmbeddingClustering_ThresholdIs075()
    {
        // Operating point chosen by offline A/B evaluation (322 cached
        // vectors, two investigations): average linkage at 0.75 separates
        // narratives without fragmenting them. Changing this constant
        // re-tunes every thread in the product — do it with measurements.
        Assert.Equal(0.75, EmbeddingClustering.SimilarityThreshold);
    }

    [Fact]
    public void EmbeddingClustering_BridgePairDoesNotChainClusters()
    {
        // Chaining regression (Issue: 166-article mega-thread): two tight
        // pairs (cos 0.94 within) joined by ONE bridge pair at cos 0.82.
        // Single/max linkage would fuse all four via the bridge; average
        // linkage keeps the pairs apart (cross-mean 0.56 < 0.75).
        // Angles: p1@0°, p2@20°, q1@55°, q2@75°.
        var topics = EmbeddingClustering.Cluster(new[]
        {
            new float[] { 1f, 0f },
            new float[] { 0.93969f, 0.34202f },
            new float[] { 0.57358f, 0.81915f },
            new float[] { 0.25882f, 0.96593f },
        });

        Assert.Equal(2, topics.Count);
        Assert.All(topics, t => Assert.Equal(2, t.Count));
    }

    [Fact]
    public void BriefBatcher_SplitsOverBudget()
    {
        var inputs = new[]
        {
            ("T1", new string('x', 100)),
            ("T2", new string('y', 100)),
            ("T3", new string('z', 100)),
        };

        var single = BriefBatcher.Batch(inputs, maxBatchChars: 1000);
        var split = BriefBatcher.Batch(inputs, maxBatchChars: 150);

        Assert.Single(single);
        Assert.Equal(3, split.Count);
        Assert.Equal(inputs.Length, split.SelectMany(b => b).Count());
    }

    [Fact]
    public void ClusterBriefPrompt_ReduceMode_PreservesGlobalNumbering()
    {
        var prompt = ClusterBriefPrompt.Build("TSLA", new DateOnly(2020, 1, 15), new[]
        {
            ("Title three", "Body three"),
        }, startIndex: 3, isReduce: true);

        Assert.Contains("[3] Title three", prompt);
        Assert.Contains("BATCH SUMMARIES", prompt);
        Assert.Contains("preserve them exactly", prompt);
    }

    [Fact]
    public void ClusterBriefPrompt_ContainsCutoffAndCitations()
    {
        var prompt = ClusterBriefPrompt.Build("TSLA", new DateOnly(2020, 1, 15), new[]
        {
            ("Title one", "Body one"),
            ("Title two", "Body two"),
        });

        Assert.Contains("2020-01-15", prompt);
        Assert.Contains("[1] Title one", prompt);
        Assert.Contains("[2] Title two", prompt);
        Assert.Contains("cite each claim like [1], [2]", prompt);
        Assert.DoesNotContain("price move caused", prompt);
    }
}

