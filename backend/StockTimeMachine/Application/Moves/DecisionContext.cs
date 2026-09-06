namespace StockTimeMachine;

// Decision Context Engine (dc-v1): a descriptive, missing-data-aware measure
// of how uncertain and information-constrained a historical decision
// environment was. NOT a prediction, NOT investment advice, NOT a verdict on
// any decision. Higher score = thinner, more conflicting, or more unstable
// evidence. Every input is observed (never expected, never fabricated);
// every absent input is a named state, never a zero.
//
// Layers (coverage): regulatory filings, news, social, market prices.
// Conflict: confidence-weighted variance of FinBERT scores (3+ usable).
// Instability: realized volatility + drawdown severity (window-computed).
// Composite: availability-aware renormalization over MEASURED components
// only, plus a Confidence grade so precision never masquerades as certainty.
public static class DecisionContextCalculator
{
    public const string Version = "dc-v1";
    public const int MinScoredForConflict = 3;
    public const double MinConfidenceUsable = 0.6;
    public const int HighConfidenceScored = 10;

    private const double CoverageWeight = 0.35;
    private const double ConflictWeight = 0.35;
    private const double InstabilityWeight = 0.30;

    public sealed class LayerInput
    {
        public string Name { get; set; } = "";
        // measured | empty | unavailable
        public string Status { get; set; } = "empty";
        // Fraction of window trading days carrying ≥1 item (observed).
        public double Completeness { get; set; }
    }

    public static UncertaintyIndex Calculate(
        MovesWindow window,
        IReadOnlyList<ScoredArticle> scored,
        string modelInfo)
    {
        var tradingDays = Math.Max(window.Summary.TradingDays, 0);
        var layers = BuildLayers(window, tradingDays);
        var measured = layers.Where(l => l.Status != "unavailable").ToList();

        double? coverage = null;
        string coverageDetail;
        if (tradingDays == 0)
        {
            // No window, no observation possible — maximally uncertain, not
            // vacuously certain. Distinct from thin-but-observed windows below.
            coverageDetail = "unmeasurable — no trading window to observe";
        }
        else if (measured.Count == 0)
        {
            coverageDetail = "no layer measurable — providers unavailable";
        }
        else
        {
            // INVERTED vs completeness: thin coverage means HIGH uncertainty.
            // A fully covered calm window scores near 0 here; an empty one
            // scores near 1 before renormalization.
            var completeness = measured.Average(l => l.Completeness);
            coverage = 1 - completeness;
            var missing = layers.Where(l => l.Status == "unavailable").Select(l => l.Name).ToList();
            coverageDetail = $"{completeness:F3} temporal completeness over {measured.Count} of {layers.Count} layers" +
                (missing.Count > 0 ? $" (unavailable: {string.Join(", ", missing)})" : "");
        }

        var usable = scored
            .Where(s => s.Confidence >= MinConfidenceUsable)
            .ToList();
        var excluded = scored.Count - usable.Count;
        double? conflict = null;
        string conflictDetail;
        if (scored.Count == 0)
        {
            conflictDetail = "unavailable — 0 scored articles";
        }
        else if (usable.Count < MinScoredForConflict)
        {
            conflictDetail = $"insufficient — {usable.Count} usable scored article(s), need {MinScoredForConflict}";
        }
        else
        {
            double wSum = usable.Sum(s => s.Confidence);
            double mean = wSum == 0 ? 0 : usable.Sum(s => s.Confidence * s.Score) / wSum;
            double variance = wSum == 0 ? 0 : usable.Sum(s => s.Confidence * (s.Score - mean) * (s.Score - mean)) / wSum;
            conflict = Math.Clamp(variance, 0, 1);
            var agree = window.KeyMoves.Count(m => m.SentimentDirection == "agree");
            var disagree = window.KeyMoves.Count(m => m.SentimentDirection == "disagree");
            conflictDetail = $"{usable.Count} usable scored article(s), weighted variance {conflict:F3}" +
                (excluded > 0 ? $", {excluded} below confidence" : "") +
                $"; moves agree {agree} / disagree {disagree}";
        }

        var vol = Math.Clamp(window.Summary.Volatility / 50, 0, 1);
        var dd = Math.Clamp(Math.Abs((double)window.Summary.MaxDrawdownPct) / 25, 0, 1);
        var instability = Math.Clamp(0.6 * vol + 0.4 * dd, 0, 1);
        var instabilityDetail =
            $"realized vol {window.Summary.Volatility:F2}% + drawdown {window.Summary.MaxDrawdownPct:F2}%";

        var parts = new List<(string Name, double Weight, double Value, string Detail, string Status)>();
        if (coverage.HasValue)
            parts.Add(("evidence-coverage", CoverageWeight, coverage.Value,
                $"{coverage.Value:F3} temporal completeness; {coverageDetail}", "measured"));
        else
            parts.Add(("evidence-coverage", CoverageWeight, 0,
                coverageDetail, "unavailable"));
        // Observed-but-empty layers stay IN the renormalization (thin is
        // measured); only failed layers leave it.
        if (conflict.HasValue)
            parts.Add(("evidence-conflict", ConflictWeight, conflict.Value, conflictDetail, "measured"));
        else
            parts.Add(("evidence-conflict", ConflictWeight, 0,
                conflictDetail, scored.Count == 0 ? "unavailable" : "insufficient"));
        parts.Add(("market-instability", InstabilityWeight, instability, instabilityDetail,
            tradingDays > 0 ? "measured" : "unavailable"));

        // Renormalize over every observed component (measured, empty, or
        // insufficient) — only failed/unavailable ones leave the average.
        // Observed thinness counts; missing data does not pretend to.
        var scoredParts = parts.Where(p => p.Status != "unavailable").ToList();
        double score;
        string confidence;
        if (scoredParts.Count == 0)
        {
            score = 100;
            confidence = "Low";
        }
        else
        {
            var wSum = scoredParts.Sum(p => p.Weight);
            score = wSum == 0 ? 100 : scoredParts.Sum(p => p.Weight * p.Value) / wSum * 100;
            int scoredCount = usable.Count;
            confidence = scoredParts.Count >= 3 && scoredCount >= HighConfidenceScored ? "High"
                : scoredParts.Count >= 2 ? "Medium" : "Low";
        }
        if (double.IsNaN(score) || double.IsInfinity(score))
        {
            score = 100;
            confidence = "Low";
        }

        return new UncertaintyIndex
        {
            Score = Math.Round(score, 1),
            Confidence = confidence,
            Version = Version,
            Model = modelInfo,
            Components = parts.Select(p => new UncertaintyComponent
            {
                Name = p.Name,
                Weight = Math.Round(p.Weight, 4),
                Value = Math.Round(p.Value, 4),
                Detail = p.Detail,
                Status = p.Status,
            }).ToList(),
        };
    }

    private static List<LayerInput> BuildLayers(MovesWindow window, int tradingDays)
    {
        var layers = new List<LayerInput>();
        bool failed(string layer) => window.EvidenceByDate.Values
            .Any(e => e.UnavailableLayers.Contains(layer));
        static HashSet<DateOnly> Days(IEnumerable<DateTime> stamps) =>
            stamps.Select(d => DateOnly.FromDateTime(DateTime.SpecifyKind(d, DateTimeKind.Utc))).ToHashSet();

        var filingDays = Days(window.EvidenceByDate.Values.SelectMany(e => e.Filings).Select(f => f.FiledAt));
        layers.Add(new LayerInput
        {
            Name = "regulatory",
            Status = failed("regulatory") ? "unavailable" : filingDays.Count > 0 ? "measured" : "empty",
            Completeness = tradingDays > 0 ? (double)filingDays.Count / tradingDays : 0,
        });
        var newsDays = Days(window.EvidenceByDate.Values.SelectMany(e => e.News).Select(n => n.PublishedAt));
        layers.Add(new LayerInput
        {
            Name = "news",
            Status = failed("news") ? "unavailable" : newsDays.Count > 0 ? "measured" : "empty",
            Completeness = tradingDays > 0 ? (double)newsDays.Count / tradingDays : 0,
        });
        var socialDays = Days(window.EvidenceByDate.Values.SelectMany(e => e.Social).Select(s => s.CreatedAt));
        layers.Add(new LayerInput
        {
            Name = "social",
            Status = failed("social") ? "unavailable" : socialDays.Count > 0 ? "measured" : "empty",
            Completeness = tradingDays > 0 ? (double)socialDays.Count / tradingDays : 0,
        });
        layers.Add(new LayerInput
        {
            Name = "market",
            Status = window.WindowPrices.Count > 0 ? "measured" : "unavailable",
            Completeness = window.WindowPrices.Count > 0 ? 1.0 : 0,
        });
        return layers;
    }
}
