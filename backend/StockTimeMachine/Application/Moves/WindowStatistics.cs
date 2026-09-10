
namespace StockTimeMachine;

public static class WindowStatistics
{
    public static WindowSummary Summarize(List<PricePoint> slice)
    {
        var rets = Returns(slice);
        var first = slice.First().Close;
        var last = slice.Last().Close;
        decimal peak = first, maxDd = 0;
        foreach (var p in slice)
        {
            if (p.Close > peak) peak = p.Close;
            var dd = peak == 0 ? 0 : (p.Close - peak) / peak * 100;
            if (dd < maxDd) maxDd = dd;
        }
        var best = rets.Count > 0 ? rets.MaxBy(r => r.Ret) : null;
        var worst = rets.Count > 0 ? rets.MinBy(r => r.Ret) : null;
        var mean = rets.Count > 0 ? rets.Average(r => r.Ret) : 0;
        var variance = rets.Count > 1 ? rets.Sum(r => (r.Ret - mean) * (r.Ret - mean)) / (rets.Count - 1) : 0;

        return new WindowSummary
        {
            TradingDays = slice.Count,
            CumulativeReturnPct = first == 0 ? 0 : Math.Round((last - first) / first * 100, 2),
            Volatility = Math.Round(Math.Sqrt(variance) * Math.Sqrt(252) * 100, 2),
            MaxDrawdownPct = Math.Round(maxDd, 2),
            BestDay = best?.Date,
            BestDayReturnPct = best is null ? 0 : Math.Round((decimal)best.Ret * 100, 2),
            WorstDay = worst is null ? null : worst.Date,
            WorstDayReturnPct = worst is null ? 0 : Math.Round((decimal)worst.Ret * 100, 2),
            SufficientHistory = true,
        };
    }

    private sealed record DayRet(DateOnly Date, double Ret);

    private static List<DayRet> Returns(List<PricePoint> asc)
    {
        var list = new List<DayRet>(asc.Count);
        for (int i = 1; i < asc.Count; i++)
        {
            var prev = (double)asc[i - 1].Close;
            list.Add(new DayRet(asc[i].Date, prev == 0 ? 0 : ((double)asc[i].Close - prev) / prev));
        }
        return list;
    }
}
