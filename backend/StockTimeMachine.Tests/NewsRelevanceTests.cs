using Xunit;
using StockTimeMachine;

namespace StockTimeMachine.Tests;

public class NewsRelevanceTests
{
    private static NewsArticle Article(string id, string title) => new()
    {
        Id = id, Title = title, Source = "GDELT",
        PublishedAt = new DateTime(2020, 1, 10), Url = "https://example.com/" + id,
        CompanySymbol = "AAPL",
    };

    [Fact]
    public void NamesCompany_BySymbolOrName()
    {
        Assert.True(NewsRelevance.NamesCompany(Article("a", "AAPL hits record"), "AAPL", "Apple Inc."));
        Assert.True(NewsRelevance.NamesCompany(Article("b", "Apple faces probe"), "AAPL", "Apple Inc."));
        Assert.False(NewsRelevance.NamesCompany(Article("c", "Market rallies broadly"), "AAPL", "Apple Inc."));
    }

    [Fact]
    public void OrderByMention_TitleMatchesFirstThenNewest()
    {
        var tagged = Article("t", "Old tagged story");
        tagged.PublishedAt = new DateTime(2020, 1, 1);
        var namedOld = Article("n", "Apple sued over fees");
        namedOld.PublishedAt = new DateTime(2020, 1, 1);
        var namedNew = Article("m", "Apple opens store");
        namedNew.PublishedAt = new DateTime(2020, 1, 20);

        var ordered = NewsRelevance.OrderByMention(
            new[] { tagged, namedOld, namedNew }, "AAPL", "Apple Inc.");

        // Same rows, better arranged: namers first (newest among them), then the rest.
        Assert.Equal(new[] { "m", "n", "t" }, ordered.Select(n => n.Id));
    }
}
