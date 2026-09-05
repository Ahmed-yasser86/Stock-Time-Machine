using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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
}

public sealed class DisabledBodyStub : IArticleContentClient
{
    public bool IsEnabled => false;
    public Task<ArticleBody?> FetchBodyAsync(string articleUrl, CancellationToken ct = default) =>
        Task.FromResult<ArticleBody?>(null);
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
            gemini, new DisabledBodyStub(), NullLogger<NarrativeService>.Instance);

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
            new ThrowingGeminiStub(), new DisabledBodyStub(), NullLogger<NarrativeService>.Instance);

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
            new DisabledGeminiStub(), new DisabledBodyStub(), NullLogger<NarrativeService>.Instance);

        var result = await sut.GetTopics("TSLA", new DateOnly(2020, 1, 15), NewsSources.Gdelt);

        Assert.Equal("tf-idf-fallback", result.ClusteringMethod);
        Assert.NotEmpty(result.Topics);
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
        var sut = new NarrativeService(repo, gemini,
            new DisabledBodyStub(), NullLogger<NarrativeService>.Instance);

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
            new FixedGeminiStub(), new DisabledBodyStub(), NullLogger<NarrativeService>.Instance);

        // "zzzqqq" appears nowhere: no match, no Gemini call.
        var brief = await sut.BriefSharedThread(
            new[] { "TSLA" }, new DateOnly(2020, 1, 15), NewsSources.Gdelt,
            new[] { "zzzqqq" });

        Assert.Null(brief);
    }

    private sealed class StubMoves : IMoveDetectionService
    {
        public Task<MovesWindow> GetMoves(string symbol, DateOnly asOfDate, string? newsSource = null, CancellationToken ct = default, IProgress<SnapshotProgress>? progress = null) =>
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
            new StubMoves(), gemini, new DisabledBodyStub(),
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

    [Fact]
    public async Task CrossThreadSimilarity_PairsRankedByCosine()
    {
        var db = NewDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);
        await repo.StoreNews("AAA", new[]
        {
            new NewsArticle { Id = "a1", Title = "Alpha one", Description = "d", Source = "GDELT", PublishedAt = new DateTime(2020, 1, 10), Url = "https://example.com/a1", CompanySymbol = "AAA" },
            new NewsArticle { Id = "a2", Title = "Alpha two", Description = "d", Source = "GDELT", PublishedAt = new DateTime(2020, 1, 11), Url = "https://example.com/a2", CompanySymbol = "AAA" },
        });
        await repo.StoreNews("BBB", new[]
        {
            new NewsArticle { Id = "b1", Title = "Beta one", Description = "d", Source = "GDELT", PublishedAt = new DateTime(2020, 1, 10), Url = "https://example.com/b1", CompanySymbol = "BBB" },
            new NewsArticle { Id = "b2", Title = "Beta two", Description = "d", Source = "GDELT", PublishedAt = new DateTime(2020, 1, 12), Url = "https://example.com/b2", CompanySymbol = "BBB" },
        });
        var sut = new NarrativeService(repo, new FixedGeminiStub(),
            new DisabledBodyStub(), NullLogger<NarrativeService>.Instance);

        var pairs = await sut.CrossThreadSimilarity(
            new[] { "AAA", "BBB" }, new DateOnly(2020, 1, 15), NewsSources.Gdelt);

        // Alternating stub vectors pair index-to-index at cosine 1.0.
        Assert.Equal(2, pairs.Count);
        Assert.All(pairs, p => Assert.Equal(1.0, p.Similarity));
        Assert.Contains(pairs, p => p.ASymbol == "AAA" && p.BSymbol == "BBB");
    }

    [Fact]
    public async Task CrossThreadSimilarity_DisabledAi_ReturnsEmpty()
    {
        var db = NewDb();
        var sut = new NarrativeService(
            new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
            new DisabledGeminiStub(), new DisabledBodyStub(), NullLogger<NarrativeService>.Instance);

        Assert.Empty(await sut.CrossThreadSimilarity(
            new[] { "AAA", "BBB" }, new DateOnly(2020, 1, 15), NewsSources.Gdelt));
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
            gemini, new DisabledBodyStub(), NullLogger<NarrativeService>.Instance);

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
            gemini, new DisabledBodyStub(), NullLogger<NarrativeService>.Instance);
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
            new DisabledGeminiStub(), new DisabledBodyStub(), NullLogger<NarrativeService>.Instance);
        var stages = new List<SnapshotProgress>();
        var progress = new Progress<SnapshotProgress>(s => stages.Add(s));

        var result = await sut.GetTopics("TSLA", new DateOnly(2020, 1, 15), NewsSources.Gdelt, progress: progress);

        Assert.Empty(result.Topics);
        for (int i = 0; i < 50 && !stages.Any(s => s.Stage == "clustering"); i++)
            await Task.Delay(100);
        Assert.Contains(stages, s => s.Stage == "clustering" && s.State == "complete");
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
