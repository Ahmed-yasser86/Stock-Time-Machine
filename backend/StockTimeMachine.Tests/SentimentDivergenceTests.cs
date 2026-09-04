using Xunit;
using StockTimeMachine;

namespace StockTimeMachine.Tests;

public class SentimentDivergenceTests
{
    [Fact]
    public void Classify_SameSignPositive_Agrees()
    {
        Assert.Equal("agree", SentimentDivergence.Classify(new decimal?[] { 0.5m, 0.3m }, 4.2m));
    }

    [Fact]
    public void Classify_SameSignNegative_Agrees()
    {
        Assert.Equal("agree", SentimentDivergence.Classify(new decimal?[] { -0.6m, -0.2m }, -3.1m));
    }

    [Fact]
    public void Classify_OppositeSigns_Disagrees()
    {
        Assert.Equal("disagree", SentimentDivergence.Classify(new decimal?[] { 0.7m, 0.4m }, -2.5m));
        Assert.Equal("disagree", SentimentDivergence.Classify(new decimal?[] { -0.5m, -0.3m }, 1.8m));
    }

    [Fact]
    public void Classify_WeakMean_IsNeutral()
    {
        Assert.Equal("neutral", SentimentDivergence.Classify(new decimal?[] { 0.05m, -0.04m }, 5.0m));
        Assert.Equal("neutral", SentimentDivergence.Classify(new decimal?[] { 0.8m, 0.2m }, 0m));
    }

    [Fact]
    public void Classify_FewerThanTwoScores_IsUnknown()
    {
        Assert.Equal("unknown", SentimentDivergence.Classify(new decimal?[] { 0.9m }, 5.0m));
        Assert.Equal("unknown", SentimentDivergence.Classify(Array.Empty<decimal?>(), -5.0m));
        Assert.Equal("unknown", SentimentDivergence.Classify(new decimal?[] { null, null }, 5.0m));
    }

    [Fact]
    public void Classify_NullsAreIgnored()
    {
        Assert.Equal("agree", SentimentDivergence.Classify(new decimal?[] { null, 0.4m, 0.6m, null }, 2.0m));
    }
}
