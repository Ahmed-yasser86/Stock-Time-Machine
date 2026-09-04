namespace StockTimeMachine;

// Decision Uncertainty Index: a transparent 0–100 gauge of how thin or
// conflicting the knowable evidence is around the decision window.
// Higher = more uncertain. Every term is visible in Components; there are no
// hidden inputs and no thresholds that advise action.
//
// Terms (weights sum to 1):
// - evidence-sparsity (0.4): 1 minus evidence density vs a full window
//   (5 moves × 13 items: 5 filings + 5 news + 3 social).
// - sentiment-dispersion (0.3): std of available per-entity sentiment scores,
//   scaled by 0.5 (half the [-1,1] range). Fewer than 2 scores contributes 0.5
//   flagged as unknown — missing sentiment never reads as consensus.
// - volatility-level (0.3): annualized window volatility scaled by 50%.
// Pure and deterministic: same window in → same index out.
public class UncertaintyComponent
{
    public string Name { get; set; } = "";
    public double Weight { get; set; }
    public double Value { get; set; }
    public string Detail { get; set; } = "";
}

public class UncertaintyIndex
{
    public double Score { get; set; }
    public List<UncertaintyComponent> Components { get; set; } = new();
}

public static class UncertaintyIndexCalculator
{
    private const int ExpectedMoves = 5;
    private const int ItemsPerMove = 13;
    private const double SparsityWeight = 0.4;
    private const double DispersionWeight = 0.3;
    private const double VolatilityWeight = 0.3;

    public static UncertaintyIndex Calculate(MovesWindow window)
    {
        var totalItems = window.EvidenceByDate.Values.Sum(e =>
            e.Filings.Count + e.News.Count + e.Social.Count);
        var density = Math.Min((double)totalItems / (ExpectedMoves * ItemsPerMove), 1);
        var sparsity = 1 - density;

        var scores = window.EvidenceByDate.Values
            .SelectMany(e => e.News)
            .Where(n => n.SentimentScore.HasValue)
            .Select(n => (double)n.SentimentScore!.Value)
            .ToList();
        double dispersion;
        string dispersionDetail;
        if (scores.Count < 2)
        {
            dispersion = 0.5;
            dispersionDetail = $"only {scores.Count} scored article(s) — treated as unknown, not consensus";
        }
        else
        {
            var mean = scores.Average();
            var variance = scores.Sum(s => (s - mean) * (s - mean)) / scores.Count;
            dispersion = Math.Min(Math.Sqrt(variance) / 0.5, 1);
            dispersionDetail = $"{scores.Count} scored article(s), std {Math.Sqrt(variance):F3}";
        }

        var volLevel = Math.Min(window.Summary.Volatility / 50, 1);
        var volDetail = $"annualized window volatility {window.Summary.Volatility:F2}%";

        var score = SparsityWeight * sparsity + DispersionWeight * dispersion + VolatilityWeight * volLevel;

        return new UncertaintyIndex
        {
            Score = Math.Round(score * 100, 1),
            Components = new List<UncertaintyComponent>
            {
                new() { Name = "evidence-sparsity", Weight = SparsityWeight, Value = Math.Round(sparsity, 4), Detail = $"{totalItems} evidence items vs {ExpectedMoves * ItemsPerMove} for a full window" },
                new() { Name = "sentiment-dispersion", Weight = DispersionWeight, Value = Math.Round(dispersion, 4), Detail = dispersionDetail },
                new() { Name = "volatility-level", Weight = VolatilityWeight, Value = Math.Round(volLevel, 4), Detail = volDetail },
            },
        };
    }
}
