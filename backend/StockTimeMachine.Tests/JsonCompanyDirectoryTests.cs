using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using StockTimeMachine;

namespace StockTimeMachine.Tests;

public class JsonCompanyDirectoryTests
{
    [Fact]
    public void TryGet_KnownSymbol_ReturnsCompanyInfo()
    {
        var dir = new JsonCompanyDirectory(NullLogger<JsonCompanyDirectory>.Instance);

        var ok = dir.TryGet("TSLA", out var c);

        Assert.True(ok);
        Assert.NotNull(c);
        Assert.Equal("Tesla, Inc.", c!.Name);
        Assert.Equal("0001318605", c.Cik);
        Assert.Equal("NASDAQ", c.Exchange);
        Assert.Equal("Consumer Discretionary", c.Sector);
    }

    [Fact]
    public void TryGetCik_TSLA_ReturnsCik()
    {
        var dir = new JsonCompanyDirectory(NullLogger<JsonCompanyDirectory>.Instance);

        var ok = dir.TryGetCik("TSLA", out var cik);

        Assert.True(ok);
        Assert.Equal("0001318605", cik);
    }

    [Fact]
    public void TryGetCik_CaseInsensitive()
    {
        var dir = new JsonCompanyDirectory(NullLogger<JsonCompanyDirectory>.Instance);

        var ok = dir.TryGetCik("tsla", out var cik);

        Assert.True(ok);
        Assert.Equal("0001318605", cik);
    }

    [Fact]
    public void TryGet_UnknownSymbol_ReturnsFalse()
    {
        var dir = new JsonCompanyDirectory(NullLogger<JsonCompanyDirectory>.Instance);

        var ok = dir.TryGet("ZZZZ", out var c);

        Assert.False(ok);
        Assert.Null(c);
    }

    [Fact]
    public void Search_ByNamePrefix_ReturnsMatches()
    {
        var dir = new JsonCompanyDirectory(NullLogger<JsonCompanyDirectory>.Instance);

        var results = dir.Search("Apple");

        Assert.Contains(results, c => c.Symbol == "AAPL");
    }

    [Fact]
    public void Search_EmptyQuery_ReturnsEmpty()
    {
        var dir = new JsonCompanyDirectory(NullLogger<JsonCompanyDirectory>.Instance);

        var results = dir.Search("");

        Assert.Empty(results);
    }

    [Fact]
    public void All_HasAtLeast20Companies()
    {
        var dir = new JsonCompanyDirectory(NullLogger<JsonCompanyDirectory>.Instance);

        var all = dir.All();

        Assert.True(all.Count >= 20);
    }
}