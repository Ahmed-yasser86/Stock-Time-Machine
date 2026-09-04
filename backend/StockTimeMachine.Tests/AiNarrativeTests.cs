using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using StockTimeMachine;

namespace StockTimeMachine.Tests;

public sealed class DisabledGeminiStub : IGeminiClient
{
    public bool IsEnabled => false;
    public string SummaryModel => "stub";
    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
        throw new InvalidOperationException("disabled");
    public Task<ClusterBrief?> SummarizeClusterAsync(string prompt, CancellationToken ct = default) =>
        Task.FromResult<ClusterBrief?>(null);
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
        public List<string> SeenPrompts { get; } = new();
        // a's ~ [1,0], b's ~ [0,1]: pairs merge within groups only.
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select((t, i) =>
                (i % 2 == 0 ? new float[] { 1f, 0.05f } : new float[] { 0.05f, 1f })).ToList());
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
    }

    private sealed class ThrowingGeminiStub : IGeminiClient
    {
        public bool IsEnabled => true;
        public string SummaryModel => "stub";
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            throw new HttpRequestException("Gemini down");
        public Task<ClusterBrief?> SummarizeClusterAsync(string prompt, CancellationToken ct = default) =>
            Task.FromResult<ClusterBrief?>(null);
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
