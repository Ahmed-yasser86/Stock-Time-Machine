namespace StockTimeMachine;

// Shared rolling-window statistics. Extracted from MoveDetectionService without
// behavior change: identical formulas, now reusable (regime classification).
// All double precision, ranking/statistics only — never money.
public static class RollingStats
{
    public static double Average(double[] xs)
    {
        double s = 0;
        foreach (var x in xs) s += x;
        return s / xs.Length;
    }

    public static double SampleStdDev(double[] xs, double mean)
    {
        double s = 0;
        foreach (var x in xs) s += (x - mean) * (x - mean);
        return xs.Length > 1 ? Math.Sqrt(s / (xs.Length - 1)) : 0;
    }

    public static double Median(double[] xs)
    {
        var sorted = xs.OrderBy(x => x).ToArray();
        int n = sorted.Length;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2;
    }

    public static double DailyReturn(double close, double prevClose) =>
        prevClose == 0 ? 0 : (close - prevClose) / prevClose;
}
