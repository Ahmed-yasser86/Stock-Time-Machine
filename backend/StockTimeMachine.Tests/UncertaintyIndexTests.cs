using Xunit;
using StockTimeMachine;

namespace StockTimeMachine.Tests;

public class UncertaintyIndexTests
{
    private static MovesWindow Window(
        int filings, int news, int scored, double[] sentiments, int social, double volatility)
    {
        var window = new MovesWindow
        {
            CompanySymbol = "TSLA",
            DecisionDate = new DateOnly(2020, 2, 20),
            Summary = new WindowSummary { TradingDays = 100, Volatility = volatility, SufficientHistory = true },
        };
        var evidence = new MoveEvidence();
        for (int i = 0; i < filings; i++)
            evidence.Filings.Add(new SecFiling { AccessionNumber = $"f{i}", FormType = "10-K", FiledAt = new DateTime(2020, 1, 10), Url = "https://example.com", CompanySymbol = "TSLA" });
        for (int i = 0; i < news; i++)
            evidence.News.Add(new NewsArticle
            {
                Id = $"n{i}", Title = "t", Source = "MarketAux", PublishedAt = new DateTime(2020, 1, 10),
                Url = "https://example.com", CompanySymbol = "TSLA",
                SentimentScore = i < scored ? (decimal)sentiments[i] : null,
            });
        for (int i = 0; i < social; i++)
            evidence.Social.Add(new SocialSignal { Id = $"s{i}", Provider = "P", Community = "r/x", Title = "t", CreatedAt = new DateTime(2020, 1, 10), CompanySymbol = "TSLA" });
        window.EvidenceByDate["2020-02-01"] = evidence;
        return window;
    }

    [Fact]
    public void Calculate_FixedInputs_ExactScore()
    {
        // 5 items -> sparsity 1 - 5/65; scores [+0.5, -0.5] -> std 0.5 -> dispersion 1; vol 20 -> 0.4.
        // 100 * (0.4*0.9230769 + 0.3*1 + 0.3*0.4) = 78.9 (rounded).
        var result = UncertaintyIndexCalculator.Calculate(Window(2, 2, 2, new[] { 0.5, -0.5 }, 1, 20));

        Assert.Equal(78.9, result.Score);
        Assert.Equal(3, result.Components.Count);
        Assert.Equal(new[] { 0.4, 0.3, 0.3 }, result.Components.Select(c => c.Weight));
    }

    [Fact]
    public void Calculate_NoScores_TreatedAsUnknownNotConsensus()
    {
        var result = UncertaintyIndexCalculator.Calculate(Window(1, 1, 0, Array.Empty<double>(), 0, 10));

        var dispersion = Assert.Single(result.Components, c => c.Name == "sentiment-dispersion");
        Assert.Equal(0.5, dispersion.Value);
        Assert.Contains("unknown", dispersion.Detail);
    }

    [Fact]
    public void Calculate_EmptyWindow_DeterministicValue()
    {
        // sparsity 1, dispersion 0.5 (unknown), vol 0 -> 100 * (0.4 + 0.15) = 55.0.
        var result = UncertaintyIndexCalculator.Calculate(new MovesWindow());

        Assert.Equal(55.0, result.Score);
    }

    [Fact]
    public void Calculate_ClampsExtremeVolatility()
    {
        var result = UncertaintyIndexCalculator.Calculate(Window(0, 0, 0, Array.Empty<double>(), 0, 500));

        var vol = Assert.Single(result.Components, c => c.Name == "volatility-level");
        Assert.Equal(1.0, vol.Value);
    }

    [Fact]
    public void Calculate_IsDeterministic()
    {
        var window = Window(3, 4, 2, new[] { 0.2, -0.4 }, 2, 33.33);
        var first = UncertaintyIndexCalculator.Calculate(window);
        var second = UncertaintyIndexCalculator.Calculate(window);

        Assert.Equal(first.Score, second.Score);
        Assert.Equal(
            first.Components.Select(c => (c.Name, c.Value)),
            second.Components.Select(c => (c.Name, c.Value)));
    }
}
