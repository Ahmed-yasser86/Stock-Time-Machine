namespace StockTimeMachine;

// Market regime labels from trailing volatility, tertiled WITHIN the analyzed
// window (never against absolute thresholds — a 2020 calm is not a 2026 calm).
// Bottom third calm, middle normal, top tense; days with fewer than 10 prior
// closes read "warming" (insufficient trailing data).
// Descriptive only: regimes describe realized volatility, predict nothing.
public static class MarketRegimes
{
    public const string Calm = "calm";
    public const string Normal = "normal";
    public const string Tense = "tense";
    public const string Warming = "warming";
}

public static class RegimeClassifier
{
    private const int Trailing = 20;
    private const int MinPrior = 10;

    public static Dictionary<string, string> Classify(IReadOnlyList<PricePoint> ascending)
    {
        var vols = new List<(DateOnly Date, double Vol)>();
        for (int i = 0; i < ascending.Count; i++)
        {
            if (i < MinPrior)
                continue;
            int start = Math.Max(1, i - Trailing + 1);
            var rets = new List<double>();
            for (int j = start; j <= i; j++)
                rets.Add(RollingStats.DailyReturn((double)ascending[j].Close, (double)ascending[j - 1].Close));
            var arr = rets.ToArray();
            var vol = RollingStats.SampleStdDev(arr, RollingStats.Average(arr)) * Math.Sqrt(252) * 100;
            vols.Add((ascending[i].Date, vol));
        }

        var result = new Dictionary<string, string>();
        for (int i = 0; i < ascending.Count && i < MinPrior; i++)
            result[ascending[i].Date.ToString("yyyy-MM-dd")] = MarketRegimes.Warming;
        if (vols.Count == 0)
            return result;

        var sorted = vols.Select(v => v.Vol).OrderBy(v => v).ToArray();
        double cut(int third) => sorted[Math.Min(sorted.Length - 1, third * sorted.Length / 3)];
        var t1 = cut(1);
        var t2 = cut(2);

        foreach (var (date, vol) in vols)
        {
            // Inclusive cuts: tied values (e.g. long flat stretches at vol 0)
            // settle into the calmer bucket, deterministically.
            var regime = vol <= t1 ? MarketRegimes.Calm
                : vol <= t2 ? MarketRegimes.Normal
                : MarketRegimes.Tense;
            result[date.ToString("yyyy-MM-dd")] = regime;
        }

        return result;
    }
}
