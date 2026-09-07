namespace StockTimeMachine;

// Case-level hybrid pattern vector: [89-d structure | 3072-d content].
// Structure encodes which signals fired (+ their 15 pairwise
// co-occurrences), regime path (histogram + ordered bigrams), sentiment
// (direction + magnitude), thread category mix (share + volume per
// category), filing mix, move magnitude, detection score, evidence density.
// Content is the mean-pooled admitted-thread embedding: WHAT the news was
// actually about (fixes category-only blindness: earnings-miss vs
// acquisition both read FINANCIAL structurally but differ in content).
// Deterministic pure math (zero quota). Weights applied first, then the
// FULL vector is L2-normalized so cosine works directly.
public static class HypeCaseVector
{
    // Structural side: 89 base dims + 7 filing dims (Phase 4). NOTE on the
    // Phase-4 spec's "72-d": 72 was the pre-bigram count; the structural side
    // is 96-d (bigrams, sentiment magnitude, and filing dims are structural
    // too). The structural collection mirrors this full side, never content.
    public const int StructuralDimensions = 96;
    public const int ContentDimensions = 3072;
    public const int Dimensions = StructuralDimensions + ContentDimensions;

    // Dimension weights (applied before normalization).
    public const double SignalWeight = 3.0;      // dims 0–5
    public const double RegimeTransitionWeight = 2.0; // dims 10–11
    public const double SentimentWeight = 2.0;   // dims 12–15
    public const double CategoryWeight = 1.0;    // dims 16–49
    public const double PairWeight = 2.0;        // dims 50–64
    public const double FilingWeight = 0.5;      // dims 65–67
    public const double MagnitudeWeight = 0.5;   // dims 68–69
    public const double ScoreWeight = 1.0;       // dims 70–71
    public const double SentimentMagnitudeWeight = 2.0; // dim 72 (sentiment family)
    public const double BigramWeight = 1.0;      // dims 73–88
    public const double FilingSignalWeight = 2.0; // dims 89–95 (filing family)
    // Content scalar: balances the ~unit-norm content mean against the
    // weighted structural side (norm ≈ 5–7). Tunable during validation.
    public const double ContentWeight = 4.0;

    // Canonical category order (RelevancePrompt.Categories is unordered).
    public static readonly IReadOnlyList<string> CategoryOrder = new List<string>
    {
        "FINANCIAL", "MARKET", "PRODUCT", "OPERATIONS", "SUPPLY_CHAIN",
        "MANAGEMENT", "STRATEGY", "LEGAL", "REGULATORY", "TECHNOLOGY",
        "CUSTOMER_DEMAND", "COMPETITIVE", "MACROECONOMIC", "GEOPOLITICAL",
        "REPUTATIONAL", "LABOR", "OTHER_MATERIAL",
    }.AsReadOnly();

    private static readonly IReadOnlyList<string> SignalOrder = HypeSignalCatalog.All
        .Select(s => s.Id).ToList().AsReadOnly();

    private static readonly IReadOnlyList<string> RegimeLabels = new List<string>
    {
        MarketRegimes.Calm, MarketRegimes.Normal, MarketRegimes.Tense, MarketRegimes.Warming,
    }.AsReadOnly();

    // Mean-pool cached article vectors into the content side input.
    // Unit-normalized here; Build applies ContentWeight then global norm.
    // Null when nothing measurable (caller stores structural-only zeros).
    public static IReadOnlyList<float>? MeanPool(IReadOnlyList<IReadOnlyList<float>> vectors)
    {
        var list = vectors.Where(v => v is { Count: > 0 }).ToList();
        if (list.Count == 0)
            return null;
        int dims = list.Max(v => v.Count);
        var mean = new double[dims];
        foreach (var v in list)
            for (int i = 0; i < v.Count; i++)
                mean[i] += v[i] / list.Count;
        double norm = Math.Sqrt(mean.Sum(x => x * x));
        if (norm <= 0)
            return null;
        return mean.Select(x => (float)(x / norm)).ToList();
    }

    // Per-filing structured values feeding dims 89–95. Callers derive these
    // from FilingSummaryRecord rows (absent rows simply contribute nothing).
    public sealed record FilingVectorInput(string EventType, double Relevance, double Sentiment);

    public static FilingVectorInput FromRecord(FilingSummaryRecord row, string structuredJson)
    {
        string evt = FilingEventTypes.Other;
        double relevance = 0, sentiment = 0;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(structuredJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("event_type", out var e) &&
                FilingEventTypes.All.Contains((e.GetString() ?? "").Trim().ToLowerInvariant()))
                evt = e.GetString()!.Trim().ToLowerInvariant();
            if (root.TryGetProperty("market_relevance", out var r))
                relevance = FilingRelevance.ToScore(r.GetString());
            if (root.TryGetProperty("sentiment", out var s))
                sentiment = FilingSentiment.ToScore(s.GetString());
        }
        catch (System.Text.Json.JsonException)
        {
            // Corrupt JSON contributes zeros (never throws the indexer).
        }
        return new FilingVectorInput(evt, relevance, sentiment);
    }

    public static float[] Build(
        HypeCaseDetail detail,
        IReadOnlyList<HypeSignalMatch> matches,
        IReadOnlyList<float>? contentMean = null,
        IReadOnlyList<FilingVectorInput>? filingInputs = null)
    {
        var v = new double[Dimensions];
        if (detail is not null)
            FillStructural(v, detail, matches, filingInputs);
        if (contentMean is not null)
        {
            double norm = 0;
            foreach (var x in contentMean)
                norm += (double)x * x;
            norm = Math.Sqrt(norm);
            if (norm > 0)
            {
                for (int i = 0; i < ContentDimensions && i < contentMean.Count; i++)
                    v[StructuralDimensions + i] = contentMean[i] / norm * ContentWeight;
            }
        }
        var total = Math.Sqrt(v.Sum(x => x * x));
        if (total <= 0)
            return new float[Dimensions];
        return v.Select(x => (float)(x / total)).ToArray();
    }

    // Structural-only vector (96-d, no content): the structural collection's
    // unit. Same math as the hybrid's structural side, L2-normalized alone.
    public static float[] BuildStructural(
        HypeCaseDetail detail,
        IReadOnlyList<HypeSignalMatch>? matches,
        IReadOnlyList<FilingVectorInput>? filingInputs = null)
    {
        var v = new double[StructuralDimensions];
        if (detail is not null)
            FillStructural(v, detail, matches, filingInputs);
        var norm = Math.Sqrt(v.Sum(x => x * x));
        if (norm <= 0)
            return new float[StructuralDimensions];
        return v.Select(x => (float)(x / norm)).ToArray();
    }

    private static void FillStructural(
        double[] v, HypeCaseDetail detail, IReadOnlyList<HypeSignalMatch>? matches,
        IReadOnlyList<FilingVectorInput>? filingInputs = null)
    {
        var fired = new HashSet<string>(
            (matches ?? Enumerable.Empty<HypeSignalMatch>()).Select(m => m.SignalId),
            StringComparer.Ordinal);
        for (int i = 0; i < SignalOrder.Count && i < 6; i++)
            v[i] = fired.Contains(SignalOrder[i]) ? SignalWeight : 0;

        var regimes = detail.RegimePath.Values.ToList();
        if (regimes.Count > 0)
        {
            for (int i = 0; i < RegimeLabels.Count; i++)
                v[6 + i] = regimes.Count(r => r == RegimeLabels[i]) / (double)regimes.Count;
            var ordered = detail.RegimePath.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
            var warmingIdx = ordered.FindIndex(r => r == MarketRegimes.Warming);
            v[10] = warmingIdx >= 0 && ordered.Skip(warmingIdx + 1).Any(r => r == MarketRegimes.Tense)
                ? RegimeTransitionWeight : 0;
            v[11] = regimes.Count(r => r == MarketRegimes.Tense) / (double)regimes.Count * RegimeTransitionWeight;
            // Ordered bigrams [73–88]: index = from × 4 + to. Direction
            // matters (warming→tense ≠ tense→warming).
            var bigrams = new int[16];
            for (int i = 1; i < ordered.Count; i++)
            {
                var from = RegimeLabels.ToList().IndexOf(ordered[i - 1]);
                var to = RegimeLabels.ToList().IndexOf(ordered[i]);
                if (from >= 0 && to >= 0)
                    bigrams[from * 4 + to]++;
            }
            var pairs = Math.Max(ordered.Count - 1, 1);
            for (int i = 0; i < 16; i++)
                v[73 + i] = bigrams[i] / (double)pairs * BigramWeight;
        }

        string[] sentiments = { SentimentDivergence.Agree, SentimentDivergence.Disagree, SentimentDivergence.Neutral, SentimentDivergence.Unknown };
        for (int i = 0; i < sentiments.Length; i++)
            v[12 + i] = string.Equals(detail.SentimentDirection, sentiments[i], StringComparison.Ordinal)
                ? SentimentWeight : 0;
        // Sentiment magnitude [72]: signed mean (-1..1), same family weight.
        // A strong disagree (-0.8) out-pulls a weak one (-0.2).
        if (detail.SentimentMean.HasValue)
            v[72] = Math.Clamp(detail.SentimentMean.Value, -1, 1) * SentimentMagnitudeWeight;

        // Category share + volume [16–49]: share answers "what mix",
        // volume answers "how much" (10 FINANCIAL ≠ 2 FINANCIAL). Counts
        // only temporally qualified threads (reason: same retrospective
        // dating rule as the triggers — post-peak-only threads must not
        // shape the pattern vector).
        var threads = HypeCaseProjection.QualifiedThreads(detail);
        if (threads.Count > 0)
        {
            for (int i = 0; i < CategoryOrder.Count; i++)
            {
                var count = threads.Count(t => string.Equals(t.TopCategory, CategoryOrder[i], StringComparison.Ordinal));
                v[16 + i * 2] = count / (double)threads.Count * CategoryWeight;
                v[16 + i * 2 + 1] = Math.Min(count / 10.0, 1.0) * CategoryWeight;
            }
        }

        // Signal co-occurrence [50–64]: all 15 unordered pairs. Combinations
        // matter more than singles — hence pair weight above single weight.
        int pair = 0;
        for (int i = 0; i < SignalOrder.Count; i++)
            for (int j = i + 1; j < SignalOrder.Count; j++, pair++)
                v[50 + pair] = fired.Contains(SignalOrder[i]) && fired.Contains(SignalOrder[j])
                    ? PairWeight : 0;

        var filings = detail.Evidence.Filings;
        bool Is(string prefix, string? form) => (form ?? "").StartsWith(prefix, StringComparison.Ordinal);
        var eightK = filings.Count(f => Is("8-K", f.FormType));
        var ten = filings.Count(f => Is("10-Q", f.FormType) || Is("10-K", f.FormType));
        v[65] = Math.Min(eightK / 3.0, 1.0) * FilingWeight;
        v[66] = Math.Min(ten / 3.0, 1.0) * FilingWeight;
        v[67] = Math.Min(Math.Max(filings.Count - eightK - ten, 0) / 3.0, 1.0) * FilingWeight;

        var ret = (double)detail.DailyReturnPct;
        v[68] = Math.Min(Math.Max(ret, 0), 10) / 10.0 * MagnitudeWeight;
        v[69] = Math.Min(Math.Max(-ret, 0), 10) / 10.0 * MagnitudeWeight;

        v[70] = Math.Clamp(detail.Score, 0, 1) * ScoreWeight;
        v[71] = Math.Min(detail.Evidence.NewsCount / 5.0, 1.0) * ScoreWeight;

        // Filing structured dims [89–95] (Phase 4): event one-hot (presence
        // across the case's filings), max relevance, mean sentiment. Absent
        // filings contribute zeros — never guessed.
        var structured = (filingInputs ?? Enumerable.Empty<FilingVectorInput>()).ToList();
        string[] events = { FilingEventTypes.ManagementChange, FilingEventTypes.Acquisition, FilingEventTypes.EarningsWarning, FilingEventTypes.Regulatory, FilingEventTypes.Other };
        for (int i = 0; i < events.Length; i++)
            v[89 + i] = structured.Any(f => string.Equals(f.EventType, events[i], StringComparison.Ordinal))
                ? FilingSignalWeight : 0;
        v[94] = structured.Count > 0 ? structured.Max(f => Math.Clamp(f.Relevance, 0, 1)) * FilingSignalWeight : 0;
        v[95] = structured.Count > 0
            ? Math.Clamp(structured.Average(f => Math.Clamp(f.Sentiment, -1, 1)), -1, 1) * FilingSignalWeight : 0;
    }
}
