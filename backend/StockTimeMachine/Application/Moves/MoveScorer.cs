
namespace StockTimeMachine;

// Deterministic Key Moves scoring over trailing price windows.
// Methodology (also published on /methodology):
// score = 0.5 * min(|z|/3, 1) + 0.3 * min(max(volRatio-1,0)/4, 1) + 0.2 * rangeBreak,
// where z is the daily-return z-score vs trailing 20 days, volRatio the volume
// vs trailing-20d median, rangeBreak the fractional close beyond the trailing-20d
// high/low scaled x20 (5% break = full). Rank: score desc, date desc, |ret| desc.
// Statistics use double internally for ranking only; all money stays decimal and
// every displayed price is a real close. Same rows in → same moves out.
public static class MoveScorer
{
    private const int Rolling = 20;

    // take is harvest-aware (reason: Step 7 of hype-intelligence-plan):
    // product default TopMoves, mining override clamped by the caller.
    public static List<KeyMove> ScoreDays(List<PricePoint> asc, List<PricePoint> slice, int take)
    {
        var indexOf = new Dictionary<DateOnly, int>();
        for (int i = 0; i < asc.Count; i++) indexOf[asc[i].Date] = i;

        var scored = new List<(KeyMove Move, double Score, double AbsRet)>();
        foreach (var p in slice)
        {
            var i = indexOf[p.Date];
            // Baselines use the 20 days strictly before i (closes [i-21, i-1]
            // for returns, rows [i-20, i-1] for volume/range), hence i >= 21.
            if (i < Rolling + 1)
                continue;

            // The evaluated observation: day i's own return (never part of its baseline).
            var prevClose = (double)asc[i - 1].Close;
            var ret = prevClose == 0 ? 0 : ((double)p.Close - prevClose) / prevClose;

            var rets = new double[Rolling];
            for (int k = 0; k < Rolling; k++)
            {
                int j = i - Rolling + k;
                var pc = (double)asc[j - 1].Close;
                rets[k] = pc == 0 ? 0 : ((double)asc[j].Close - pc) / pc;
            }
            var mean = RollingStats.Average(rets);
            // Volatility floor: after perfectly flat windows any move is extreme.
            // Documented constant; deterministic given the same rows.
            var std = Math.Max(RollingStats.SampleStdDev(rets, mean), 0.0005);
            var z = (ret - mean) / std;

            var vols = new double[Rolling];
            for (int k = 0; k < Rolling; k++) vols[k] = asc[i - Rolling + k].Volume;
            var med = RollingStats.Median(vols);
            var volRatio = med <= 0 ? 1 : (double)p.Volume / med;

            double mom5 = 0;
            if (i >= 5)
            {
                var baseClose = (double)asc[i - 5].Close;
                mom5 = baseClose == 0 ? 0 : ((double)p.Close - baseClose) / baseClose;
            }

            double hi = double.MinValue, lo = double.MaxValue;
            for (int k = 0; k < Rolling; k++)
            {
                hi = Math.Max(hi, (double)asc[i - Rolling + k].High);
                lo = Math.Min(lo, (double)asc[i - Rolling + k].Low);
            }
            var close = (double)p.Close;
            double rangeBreak = close > hi && hi > 0 ? (close - hi) / hi
                : close < lo && lo > 0 ? (lo - close) / lo : 0;

            var score = 0.5 * Math.Min(Math.Abs(z) / 3, 1)
                + 0.3 * Math.Min(Math.Max(volRatio - 1, 0) / 4, 1)
                + 0.2 * Math.Min(rangeBreak * 20, 1);
            if (score <= 0)
                continue;

            var flags = new List<string>();
            if (z > 2) flags.Add(MoveFlags.Spike);
            if (z < -2) flags.Add(MoveFlags.Plunge);
            if (volRatio > 2.5) flags.Add(MoveFlags.HighVolume);
            if (close > hi) flags.Add(MoveFlags.Breakout);
            if (close < lo) flags.Add(MoveFlags.Breakdown);

            scored.Add((new KeyMove
            {
                Date = p.Date,
                Close = p.Close,
                DailyReturnPct = Math.Round((decimal)ret * 100, 2),
                ZScore = Math.Round(z, 2),
                VolumeRatio = Math.Round(volRatio, 2),
                FiveDayMomentumPct = Math.Round((decimal)mom5 * 100, 2),
                Score = Math.Round(score, 4),
                Flags = flags,
            }, score, Math.Abs(ret)));
        }

        return scored
            .OrderByDescending(s => s.Score)
            .ThenByDescending(s => s.Move.Date)
            .ThenByDescending(s => s.AbsRet)
            .Take(take)
            .Select(s => s.Move)
            .ToList();
    }
}
