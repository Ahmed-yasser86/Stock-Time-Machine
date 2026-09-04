using Xunit;
using StockTimeMachine;

namespace StockTimeMachine.Tests;

public class RegimeClassifierTests
{
    private static List<PricePoint> Series(int flatDays, decimal flatPrice, int swingDays, decimal basePrice, decimal swingPct)
    {
        var prices = new List<PricePoint>();
        var start = new DateOnly(2020, 1, 2);
        for (int i = 0; i < flatDays; i++)
            prices.Add(new PricePoint { CompanySymbol = "T", Date = start.AddDays(i), Open = flatPrice, High = flatPrice, Low = flatPrice, Close = flatPrice, Volume = 1000 });
        for (int i = 0; i < swingDays; i++)
        {
            var close = i % 2 == 0 ? basePrice * (1 + swingPct) : basePrice * (1 - swingPct);
            prices.Add(new PricePoint { CompanySymbol = "T", Date = start.AddDays(flatDays + i), Open = close, High = close, Low = close, Close = close, Volume = 1000 });
        }
        return prices;
    }

    private static string At(Dictionary<string, string> regimes, DateOnly date) =>
        regimes[date.ToString("yyyy-MM-dd")];

    [Fact]
    public void Classify_FlatThenVolatile_LabelsCalmThenTense()
    {
        var prices = Series(30, 100m, 30, 100m, 0.08m);

        var regimes = RegimeClassifier.Classify(prices);

        Assert.Equal(60, regimes.Count);
        Assert.Equal("calm", At(regimes, new DateOnly(2020, 1, 12)));
        Assert.Equal("tense", At(regimes, new DateOnly(2020, 3, 1)));
    }

    [Fact]
    public void Classify_ShortSeries_AllWarming()
    {
        var prices = Series(5, 100m, 0, 100m, 0m);

        var regimes = RegimeClassifier.Classify(prices);

        Assert.Equal(5, regimes.Count);
        Assert.All(regimes.Values, v => Assert.Equal("warming", v));
    }

    [Fact]
    public void Classify_LabelsAreAlwaysValid()
    {
        var prices = Series(30, 100m, 30, 100m, 0.08m);

        var regimes = RegimeClassifier.Classify(prices);

        var valid = new[] { "calm", "normal", "tense", "warming" };
        Assert.All(regimes.Values, v => Assert.Contains(v, valid));
    }

    [Fact]
    public void Classify_IsDeterministic()
    {
        var prices = Series(30, 100m, 30, 100m, 0.08m);

        var first = RegimeClassifier.Classify(prices);
        var second = RegimeClassifier.Classify(prices);

        Assert.Equal(first, second);
    }
}
