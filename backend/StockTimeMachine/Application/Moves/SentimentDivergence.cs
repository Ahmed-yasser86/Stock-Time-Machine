namespace StockTimeMachine;

// Narrative-vs-market agreement for one movement: does the scored news lean
// the same way the price moved? Pure and deterministic.
// - Fewer than 2 scored articles -> "unknown" (single scores are noise).
// - |mean| < 0.1 -> "neutral" (no readable lean).
// - Otherwise "agree" (same sign) or "disagree" (opposite signs).
// A disagreement is a contrarian lens, never a prediction or a cause.
public static class SentimentDivergence
{
    public const string Agree = "agree";
    public const string Disagree = "disagree";
    public const string Neutral = "neutral";
    public const string Unknown = "unknown";

    private const decimal NeutralBand = 0.1m;

    public static string Classify(IEnumerable<decimal?> scores, decimal dailyReturnPct)
    {
        var scored = scores.Where(s => s.HasValue).Select(s => s!.Value).ToList();
        if (scored.Count < 2)
            return Unknown;

        var mean = scored.Average();
        if (Math.Abs(mean) < NeutralBand || dailyReturnPct == 0)
            return Neutral;

        var narrativeUp = mean > 0;
        var marketUp = dailyReturnPct > 0;
        return narrativeUp == marketUp ? Agree : Disagree;
    }
}
