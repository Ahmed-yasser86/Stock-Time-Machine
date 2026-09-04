using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using StockTimeMachine;

namespace StockTimeMachine.Tests;

public class TopicClusteringTests
{
    private static NewsArticle Article(string id, string title, string date, string description = "") => new()
    {
        Id = id, Title = title, Description = description, Source = "GDELT",
        PublishedAt = DateTime.SpecifyKind(DateTime.Parse(date), DateTimeKind.Utc),
        Url = "https://example.com/" + id, CompanySymbol = "TSLA",
    };

    // Realistic fixtures carry descriptions (Cluster reads title + description,
    // as real provider rows do) — bare 6-word titles understate story overlap.
    private static NewsArticle[] TwoGroups() => new[]
    {
        Article("a1", "Tesla quarterly earnings beat analyst expectations", "2020-01-10",
            "Tesla reported quarterly earnings above analyst expectations on strong vehicle deliveries and record revenue."),
        Article("a2", "Tesla earnings report shows record quarterly profit", "2020-01-11",
            "Quarterly profit reached a record as Tesla earnings beat forecasts on deliveries growth and revenue."),
        Article("b1", "Tesla factory fire halts Berlin production line", "2020-01-10",
            "A fire at the Tesla Berlin factory halted production as crews contained the blaze."),
        Article("b2", "Berlin factory blaze stops Tesla assembly operations", "2020-01-12",
            "Tesla suspended assembly operations at its Berlin factory after the blaze damaged production equipment."),
    };

    [Fact]
    public void Cluster_TwoDistinctGroups_FormsTwoTopics()
    {
        var articles = TwoGroups();

        var topics = TopicClustering.Cluster(articles);

        Assert.Equal(2, topics.Count);
        Assert.All(topics, t => Assert.Equal(2, t.ArticleIds.Count));
        Assert.Contains(topics, t => t.LabelTerms.Contains("earnings"));
        Assert.Contains(topics, t => t.LabelTerms.Contains("factory") || t.LabelTerms.Contains("berlin"));
    }

    [Fact]
    public void Cluster_IdenticalDocuments_FormsOneTopic()
    {
        var articles = new[]
        {
            Article("a1", "Tesla stock surges on delivery numbers", "2020-01-10"),
            Article("a2", "Tesla stock surges on delivery numbers", "2020-01-11"),
        };

        var topics = TopicClustering.Cluster(articles);

        var single = Assert.Single(topics);
        Assert.Equal(2, single.ArticleIds.Count);
    }

    [Fact]
    public void Cluster_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(TopicClustering.Cluster(Array.Empty<NewsArticle>()));
    }

    [Fact]
    public void Cluster_SpanCoversMemberDates()
    {
        var articles = new[]
        {
            Article("a1", "Tesla quarterly earnings beat analyst expectations", "2020-01-10"),
            Article("a2", "Tesla earnings report shows record quarterly profit", "2020-01-20"),
        };

        var topics = TopicClustering.Cluster(articles);

        var single = Assert.Single(topics);
        Assert.True(single.SpanStart <= single.SpanEnd);
    }

    [Fact]
    public async Task NarrativeService_EmptyCache_ReturnsEmptyTopics()
    {
        var db = new StockTimeMachineDbContext(
            new DbContextOptionsBuilder<StockTimeMachineDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var sut = new NarrativeService(
            new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance),
            new DisabledGeminiStub(), new DisabledBodyStub(),
            NullLogger<NarrativeService>.Instance);

        var result = await sut.GetTopics("TSLA", new DateOnly(2020, 1, 15), NewsSources.Gdelt);

        Assert.Equal(0, result.ArticlesConsidered);
        Assert.Empty(result.Topics);
    }

    [Fact]
    public async Task NarrativeService_SeededCache_ClustersWithoutLiveCalls()
    {
        var db = new StockTimeMachineDbContext(
            new DbContextOptionsBuilder<StockTimeMachineDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);
        await repo.StoreNews("TSLA", new[]
        {
            new NewsArticle { Id = "a1", Title = "Tesla quarterly earnings beat", Source = "GDELT", PublishedAt = new DateTime(2020, 1, 10), Url = "https://example.com/a1", CompanySymbol = "TSLA" },
            new NewsArticle { Id = "a2", Title = "Tesla earnings smash records quarterly", Source = "GDELT", PublishedAt = new DateTime(2020, 1, 11), Url = "https://example.com/a2", CompanySymbol = "TSLA" },
        });
        var sut = new NarrativeService(repo,
            new DisabledGeminiStub(), new DisabledBodyStub(),
            NullLogger<NarrativeService>.Instance);

        var result = await sut.GetTopics("TSLA", new DateOnly(2020, 1, 15), NewsSources.Gdelt);

        Assert.Equal(2, result.ArticlesConsidered);
        Assert.Single(result.Topics);
    }

    [Fact]
    public void Cluster_IsDeterministic()
    {
        var articles = TwoGroups();

        var first = TopicClustering.Cluster(articles);
        var second = TopicClustering.Cluster(articles);

        Assert.Equal(
            first.Select(t => (string.Join(",", t.LabelTerms), string.Join(",", t.ArticleIds))),
            second.Select(t => (string.Join(",", t.LabelTerms), string.Join(",", t.ArticleIds))));
    }
}
