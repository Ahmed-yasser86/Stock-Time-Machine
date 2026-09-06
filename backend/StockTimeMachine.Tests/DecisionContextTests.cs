using Xunit;
using StockTimeMachine;

namespace StockTimeMachine.Tests;

public class DecisionContextTests
{
    private static MovesWindow Window(
        int tradingDays = 100, double volatility = 20, decimal drawdown = -5m,
        int prices = 100) =>
        new()
        {
            CompanySymbol = "TSLA",
            DecisionDate = new DateOnly(2020, 2, 20),
            Summary = new WindowSummary
            {
                TradingDays = tradingDays,
                Volatility = volatility,
                MaxDrawdownPct = drawdown,
                SufficientHistory = tradingDays >= 30,
            },
            WindowPrices = Enumerable.Range(0, prices).Select(i => new PricePoint
            {
                CompanySymbol = "TSLA",
                Date = new DateOnly(2020, 1, 2).AddDays(i),
                Close = 100m,
            }).ToList(),
        };

    private static void AddEvidence(MovesWindow window, DateOnly date,
        int filings = 1, int news = 1, int social = 1, string[]? unavailable = null)
    {
        var evidence = new MoveEvidence();
        for (int i = 0; i < filings; i++)
            evidence.Filings.Add(new SecFiling
            {
                AccessionNumber = $"f-{date:MMdd}-{i}", FormType = "10-K",
                FiledAt = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                Url = "https://example.com", CompanySymbol = "TSLA",
            });
        for (int i = 0; i < news; i++)
            evidence.News.Add(new NewsArticle
            {
                Id = $"n-{date:MMdd}-{i}", Title = "T",
                PublishedAt = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                Url = "https://example.com", CompanySymbol = "TSLA",
            });
        for (int i = 0; i < social; i++)
            evidence.Social.Add(new SocialSignal
            {
                Id = $"s-{date:MMdd}-{i}", Title = "T",
                CreatedAt = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                CompanySymbol = "TSLA",
            });
        if (unavailable is not null)
            evidence.UnavailableLayers.AddRange(unavailable);
        window.EvidenceByDate[date.ToString("yyyy-MM-dd")] = evidence;
    }

    private static ScoredArticle Scored(string id, double score, double confidence = 0.9) =>
        new() { ArticleId = id, Score = score, Confidence = confidence, Model = "test" };

    [Fact]
    public void FullWindow_MeasuredHighConfidence()
    {
        var window = Window();
        for (int i = 0; i < 5; i++)
            AddEvidence(window, new DateOnly(2020, 2, 1).AddDays(i));
        var scored = new[]
        {
            Scored("a1", 0.8), Scored("a2", 0.8), Scored("a3", 0.8),
            Scored("a4", -0.8), Scored("a5", -0.8), Scored("a6", -0.8),
            Scored("a7", 0.1), Scored("a8", -0.1), Scored("a9", 0.2),
            Scored("a10", 0.0),
        };

        var first = DecisionContextCalculator.Calculate(window, scored, "test-model");
        var second = DecisionContextCalculator.Calculate(window, scored, "test-model");

        Assert.Equal("dc-v1", first.Version);
        Assert.Equal("High", first.Confidence);
        Assert.Equal("test-model", first.Model);
        Assert.InRange(first.Score, 0, 100);
        Assert.All(first.Components, c => Assert.Equal("measured", c.Status));
        Assert.Equal(3, first.Components.Count);
        // Determinism: same inputs, identical outputs.
        Assert.Equal(first.Score, second.Score);
        Assert.Equal(
            first.Components.Select(c => (c.Name, c.Value, c.Status)),
            second.Components.Select(c => (c.Name, c.Value, c.Status)));
    }

    [Fact]
    public void Conflict_PolarizedScores_MaxVariance()
    {
        var window = Window();
        AddEvidence(window, new DateOnly(2020, 2, 1));
        var scored = new[] { Scored("a", 1.0, 1.0), Scored("b", -1.0, 1.0), Scored("c", 1.0, 1.0), Scored("d", -1.0, 1.0) };

        var result = DecisionContextCalculator.Calculate(window, scored, "m");

        var conflict = Assert.Single(result.Components, c => c.Name == "evidence-conflict");
        Assert.Equal("measured", conflict.Status);
        Assert.Equal(1.0, conflict.Value, precision: 6);
    }

    [Fact]
    public void Conflict_UniformScores_ZeroVariance()
    {
        var window = Window();
        AddEvidence(window, new DateOnly(2020, 2, 1));
        var scored = new[] { Scored("a", 0.5), Scored("b", 0.5), Scored("c", 0.5) };

        var result = DecisionContextCalculator.Calculate(window, scored, "m");

        Assert.Equal(0, Assert.Single(result.Components, c => c.Name == "evidence-conflict").Value);
    }

    [Fact]
    public void MissingData_ZeroScores_Unavailable()
    {
        var window = Window();
        AddEvidence(window, new DateOnly(2020, 2, 1));

        var result = DecisionContextCalculator.Calculate(window, Array.Empty<ScoredArticle>(), "m");

        var conflict = Assert.Single(result.Components, c => c.Name == "evidence-conflict");
        Assert.Equal("unavailable", conflict.Status);
        Assert.Contains("0 scored", conflict.Detail);
    }

    [Fact]
    public void MissingData_OneOrTwoScores_InsufficientNotZero()
    {
        var window = Window();
        AddEvidence(window, new DateOnly(2020, 2, 1));

        foreach (var n in new[] { 1, 2 })
        {
            var scored = Enumerable.Range(0, n).Select(i => Scored($"a{i}", 0.9)).ToList();
            var result = DecisionContextCalculator.Calculate(window, scored, "m");
            var conflict = Assert.Single(result.Components, c => c.Name == "evidence-conflict");
            Assert.Equal("insufficient", conflict.Status);
            Assert.Equal(0, conflict.Value);
        }
    }

    [Fact]
    public void MissingData_LowConfidence_Excluded()
    {
        var window = Window();
        AddEvidence(window, new DateOnly(2020, 2, 1));
        var scored = new[] { Scored("a", 0.9, 0.5), Scored("b", -0.9, 0.5), Scored("c", 0.1, 0.9) };

        var result = DecisionContextCalculator.Calculate(window, scored, "m");

        // Only 1 usable (< 3): insufficient, and the detail says why.
        var conflict = Assert.Single(result.Components, c => c.Name == "evidence-conflict");
        Assert.Equal("insufficient", conflict.Status);
    }

    [Fact]
    public void MissingData_FailedLayers_ExcludedFromAverage()
    {
        var withNews = Window();
        AddEvidence(withNews, new DateOnly(2020, 2, 1));
        var noNews = Window();
        AddEvidence(noNews, new DateOnly(2020, 2, 1), news: 0,
            unavailable: new[] { "news" });

        var a = DecisionContextCalculator.Calculate(withNews, Array.Empty<ScoredArticle>(), "m");
        var b = DecisionContextCalculator.Calculate(noNews, Array.Empty<ScoredArticle>(), "m");

        // Coverage renormalizes without the failed layer; detail names it.
        var coverage = Assert.Single(b.Components, c => c.Name == "evidence-coverage");
        Assert.Contains("unavailable: news", coverage.Detail);
        Assert.NotEqual(a.Score, b.Score);
    }

    [Fact]
    public void EmptyWindow_MaxScoreLowConfidenceNoCrash()
    {
        var window = Window(tradingDays: 0, volatility: 0, drawdown: 0m, prices: 0);

        var result = DecisionContextCalculator.Calculate(window, Array.Empty<ScoredArticle>(), "m");

        Assert.Equal(100, result.Score);
        Assert.Equal("Low", result.Confidence);
        Assert.All(result.Components, c => Assert.DoesNotContain("NaN", c.Detail));
    }

    [Fact]
    public void Renormalization_ExactMath()
    {
        // No evidence rows, calm market, no scores: completeness (0+0+0+1)/4
        // = 0.25, so coverage contributes 1-0.25 = 0.75 (thin = uncertain);
        // conflict unavailable; instability 0.
        // Score = (.35*.75 + .3*0)/(.35+.30)*100 = 40.4 exactly.
        var window = Window(tradingDays: 30, volatility: 0, drawdown: 0m, prices: 30);

        var result = DecisionContextCalculator.Calculate(window, Array.Empty<ScoredArticle>(), "m");

        Assert.Equal(40.4, result.Score);
        Assert.Contains(result.Components, c => c.Name == "market-instability" && c.Status == "measured");
        Assert.Contains(result.Components, c => c.Name == "evidence-conflict" && c.Status == "unavailable");
    }

    [Fact]
    public void Bounds_HoldAcrossShapes()
    {
        var rng = new Random(42);
        for (int i = 0; i < 25; i++)
        {
            var window = Window(
                tradingDays: rng.Next(0, 120),
                volatility: rng.NextDouble() * 200,
                drawdown: (decimal)(-rng.NextDouble() * 60));
            var scored = Enumerable.Range(0, rng.Next(0, 15)).Select(k =>
                Scored($"s{k}", rng.NextDouble() * 2 - 1, rng.NextDouble())).ToList();
            var result = DecisionContextCalculator.Calculate(window, scored, "m");
            Assert.InRange(result.Score, 0, 100);
            Assert.False(double.IsNaN(result.Score));
            Assert.Contains(result.Confidence, new[] { "High", "Medium", "Low" });
        }
    }

    [Fact]
    public void ChangingOneInput_ChangesScorePredictably()
    {
        var window = Window(volatility: 10);
        AddEvidence(window, new DateOnly(2020, 2, 1), filings: 0, news: 0, social: 0);
        var calm = DecisionContextCalculator.Calculate(window, Array.Empty<ScoredArticle>(), "m");
        window.Summary.Volatility = 80;
        var stormy = DecisionContextCalculator.Calculate(window, Array.Empty<ScoredArticle>(), "m");

        Assert.True(stormy.Score > calm.Score);
    }
}
