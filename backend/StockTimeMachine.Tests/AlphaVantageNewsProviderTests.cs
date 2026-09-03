using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using StockTimeMachine;

namespace StockTimeMachine.Tests;

public class AlphaVantageNewsProviderTests
{
    private static IConfiguration ConfigWithKey() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AlphaVantage:ApiKey"] = "test"
        }).Build();

    [Fact]
    public async Task SearchAsync_NoApiKey_ReturnsEmpty()
    {
        var provider = new AlphaVantageNewsProvider(
            new HttpClient(),
            NullLogger<AlphaVantageNewsProvider>.Instance,
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        var result = await provider.SearchAsync("TSLA", new DateOnly(2020, 1, 15));

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchAsync_FiltersFutureItems_AndSetsIdentity()
    {
        var json = """
        {
          "feed": [
            { "title": "Past article", "summary": "s", "url": "https://example.com/past", "source": "Example", "time_published": "20200110T120000" },
            { "title": "Future article", "summary": "s", "url": "https://example.com/future", "source": "Example", "time_published": "20200120T120000" },
            { "title": "", "summary": "s", "url": "https://example.com/empty", "source": "Example", "time_published": "20200110T120000" }
          ]
        }
        """;
        var provider = new AlphaVantageNewsProvider(
            new HttpClient(new StubHttpMessageHandler(json)),
            NullLogger<AlphaVantageNewsProvider>.Instance,
            ConfigWithKey());

        var result = await provider.SearchAsync("tsla", new DateOnly(2020, 1, 15));

        var single = Assert.Single(result);
        Assert.Equal("Past article", single.Title);
        Assert.Equal("TSLA", single.CompanySymbol);
        Assert.False(string.IsNullOrEmpty(single.Id));
        Assert.True(single.PublishedAt <= TemporalBoundary.GetCutoffUtc(new DateOnly(2020, 1, 15)));
    }

    [Fact]
    public void DeterministicIds_AreStable()
    {
        Assert.Equal(
            AlphaVantageNewsProvider.DeterministicId("https://example.com/X"),
            AlphaVantageNewsProvider.DeterministicId("https://example.com/X"));
    }

    [Fact]
    public async Task SearchAsync_ServerError_ReturnsEmpty()
    {
        var provider = new AlphaVantageNewsProvider(
            new HttpClient(new StubHttpMessageHandler("boom", System.Net.HttpStatusCode.InternalServerError)),
            NullLogger<AlphaVantageNewsProvider>.Instance,
            ConfigWithKey());

        var result = await provider.SearchAsync("TSLA", new DateOnly(2020, 1, 15));

        Assert.Empty(result);
    }
}
