using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using StockTimeMachine;
using StockTimeMachine.Web.Controllers;
using StockTimeMachine.Web.Models.Dto;

namespace StockTimeMachine.Tests;

// Thread member inspection (traceability): exact-id resolution against the
// cached rows clustering consumed — no re-clustering, no approximation.
public class ThreadArticlesTests
{
    private static readonly DateOnly AsOf = new(2020, 2, 20);

    private static StockTimeMachineDbContext NewDb() =>
        new(new DbContextOptionsBuilder<StockTimeMachineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static NarrativeService Sut(StockTimeMachineDbContext db) => new(
        new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
        new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
        Mock.Of<IGeminiClient>(),
        Mock.Of<IArticleContentClient>(),
        Mock.Of<ICompanyDirectory>(),
        Mock.Of<IRelevanceService>(),
        NullLogger<NarrativeService>.Instance);

    private static NewsArticle Article(string id, string title, string url, DateTime published) => new()
    {
        Id = id,
        Title = title,
        Description = "body",
        Source = "GDELT",
        PublishedAt = published,
        Url = url,
        CompanySymbol = "TSLA",
    };

    private static async Task SeedAsync(StockTimeMachineDbContext db)
    {
        var day = new DateTime(2020, 2, 10, 0, 0, 0, DateTimeKind.Utc);
        db.NewsArticles.AddRange(
            Article("a1", "Tesla earnings beat", "https://example.com/a1", day),
            Article("a2", "Tesla factory opens", "https://example.com/a2", day),
            Article("a3", "Untitled wire item", "", day));
        db.ArticleRelevances.Add(new ArticleRelevance
        {
            ArticleId = "a1",
            Symbol = "TSLA",
            Model = "m",
            Relevant = true,
            Category = "FINANCIAL",
            Confidence = 0.9,
            Reason = "r",
            Decision = RelevanceDecisions.Relevant,
            DecisionSource = RelevanceSources.Ai,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetThreadArticles_ReturnsExactMembers_InRequestedOrder()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var found = await Sut(db).GetThreadArticles("TSLA", AsOf, NewsSources.Gdelt,
            new[] { "a2", "a1" });

        Assert.Equal(2, found.Count);
        Assert.Equal("a2", found[0].Article.Id);
        Assert.Equal("Tesla factory opens", found[0].Article.Title);
        Assert.Equal("https://example.com/a2", found[0].Article.Url);
        Assert.Equal("a1", found[1].Article.Id);
    }

    [Fact]
    public async Task GetThreadArticles_CarriesStoredVerdictMetadata()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var found = await Sut(db).GetThreadArticles("TSLA", AsOf, NewsSources.Gdelt, new[] { "a1" });

        var single = Assert.Single(found);
        Assert.Equal(RelevanceDecisions.Relevant, single.Relevance.Decision);
        Assert.Equal(RelevanceSources.Ai, single.Relevance.DecisionSource);
        Assert.Equal("FINANCIAL", single.Relevance.Category);
    }

    [Fact]
    public async Task GetThreadArticles_MissingVerdict_ReturnsUntracedDefaults()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var found = await Sut(db).GetThreadArticles("TSLA", AsOf, NewsSources.Gdelt, new[] { "a2" });

        var single = Assert.Single(found);
        Assert.Equal(RelevanceDecisions.Uncertain, single.Relevance.Decision);
    }

    [Fact]
    public async Task GetThreadArticles_MissingId_SkippedNeverInvented()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var found = await Sut(db).GetThreadArticles("TSLA", AsOf, NewsSources.Gdelt,
            new[] { "a1", "ghost" });

        Assert.Single(found);
        Assert.Equal("a1", found[0].Article.Id);
    }

    [Fact]
    public async Task GetThreadArticles_MissingUrl_ReturnedEmpty_NotInvented()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var found = await Sut(db).GetThreadArticles("TSLA", AsOf, NewsSources.Gdelt, new[] { "a3" });

        var single = Assert.Single(found);
        Assert.Equal("", single.Article.Url);
        Assert.Equal("Untitled wire item", single.Article.Title);
    }

    [Fact]
    public async Task GetThreadArticles_DuplicateAndEmptyIds_HandledSafely()
    {
        using var db = NewDb();
        await SeedAsync(db);
        var sut = Sut(db);

        var dupes = await sut.GetThreadArticles("TSLA", AsOf, NewsSources.Gdelt,
            new[] { "a1", "a1", " ", "a1" });
        Assert.Single(dupes);

        Assert.Empty(await sut.GetThreadArticles("TSLA", AsOf, NewsSources.Gdelt,
            Array.Empty<string>()));
    }

    private static MovesController EndpointSut(StockTimeMachineDbContext db) => new(
        Mock.Of<IMoveDetectionService>(),
        Sut(db),
        Mock.Of<IRelevanceService>(),
        Mock.Of<ICompanyDirectory>(),
        Mock.Of<INewsProviderFactory>(),
        Mock.Of<IInvestigationJobStore>(),
        Mock.Of<IInvestigationJobRunner>(),
        NullLogger<MovesController>.Instance);

    [Fact]
    public async Task ThreadArticlesEndpoint_MapsMembers_WithCanonicalUrls()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var result = await EndpointSut(db).ThreadArticles(
            "TSLA", "2020-02-20", "gdelt", "a1,a3", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ThreadArticlesResponse>(ok.Value);
        Assert.Equal("TSLA", response.Symbol);
        Assert.Equal(2, response.RequestedCount);
        Assert.Equal(2, response.Items.Count);
        // Canonical URLs preserved verbatim; missing URL stays empty.
        Assert.Equal("https://example.com/a1", response.Items[0].Article.Url);
        Assert.Equal("", response.Items[1].Article.Url);
        Assert.Equal(RelevanceDecisions.Relevant, response.Items[0].Decision);
    }

    [Theory]
    [InlineData(null, "2020-02-20", "a1")]
    [InlineData("TSLA", "not-a-date", "a1")]
    [InlineData("TSLA", "2020-02-20", null)]
    [InlineData("TSLA", "2020-02-20", "  ")]
    public async Task ThreadArticlesEndpoint_RejectsBadInput(string? symbol, string? date, string? ids)
    {
        using var db = NewDb();

        await Assert.ThrowsAsync<InvalidHistoricalDateException>(() =>
            EndpointSut(db).ThreadArticles(symbol, date, "gdelt", ids, CancellationToken.None));
    }

    [Fact]
    public async Task ThreadArticlesEndpoint_RejectsOverCap()
    {
        using var db = NewDb();
        var ids = string.Join(",", Enumerable.Range(1, 501).Select(i => $"a{i}"));

        await Assert.ThrowsAsync<InvalidHistoricalDateException>(() =>
            EndpointSut(db).ThreadArticles("TSLA", "2020-02-20", "gdelt", ids, CancellationToken.None));
    }

    [Fact]
    public async Task GetThreadArticles_MembershipMatchesClusteringInput()
    {
        // Regression: the inspected membership must be exactly the rows the
        // clustering consumed — same ids in, same rows out, order preserved.
        using var db = NewDb();
        await SeedAsync(db);
        var input = new[] { "a1", "a2", "a3" };

        var found = await Sut(db).GetThreadArticles("TSLA", AsOf, NewsSources.Gdelt, input);

        Assert.Equal(input, found.Select(f => f.Article.Id).ToArray());
        Assert.All(found, f => Assert.Equal("TSLA", f.Article.CompanySymbol));
    }
}
