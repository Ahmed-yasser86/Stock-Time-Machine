namespace StockTimeMachine;

// Deterministic signal evaluator: HypeCaseDetail → fired signals with
// evidence refs. Pure and versioned (catalog hs-v1). Hard-field triggers
// only — categories, flags, label terms, regime path, sentiment direction.
// Completeness-gated per signal: a trigger whose required inputs are Missing
// does not fire (absence of data must never read as a pattern).
public static class HypeSignals
{
    public static IReadOnlyList<HypeSignalMatch> Evaluate(HypeCaseDetail detail)
    {
        if (detail is null)
            throw new ArgumentNullException(nameof(detail));
        var matches = new List<HypeSignalMatch>();
        var financial = ThreadsInCategory(detail, "FINANCIAL");
        if (financial.Count >= 2)
            matches.Add(new HypeSignalMatch
            {
                SignalId = "earnings-chatter",
                Name = "Earnings-chatter clustering",
                TriggerEvidence = financial
                    .Select(t => $"FINANCIAL thread: {t.RepresentativeTitle}")
                    .ToList(),
                TriggerThreadIds = financial.SelectMany(t => t.ArticleIds).Distinct().ToList(),
            });

        var legal = detail.PrePeakThreads
            .Where(t => t.TopCategory == "LEGAL" || t.TopCategory == "REGULATORY")
            .ToList();
        var tenseDays = detail.RegimePath
            .Where(kv => kv.Value == MarketRegimes.Tense)
            .Select(kv => kv.Key)
            .OrderBy(d => d)
            .ToList();
        if (legal.Count > 0 && tenseDays.Count >= 3)
            matches.Add(new HypeSignalMatch
            {
                SignalId = "regulatory-overhang",
                Name = "Regulatory overhang",
                TriggerEvidence = legal
                    .Select(t => $"{t.TopCategory} thread: {t.RepresentativeTitle}")
                    .Concat(new[] { $"tense regime on {tenseDays.Count} pre-peak days ({tenseDays.First()}→{tenseDays.Last()})" })
                    .ToList(),
                TriggerThreadIds = legal.SelectMany(t => t.ArticleIds).Distinct().ToList(),
            });

        // Requires real evidence data: a missing evidence layer must not read
        // as "thin narrative".
        if (AreaOk(detail, "evidence") && HasFlag(detail, MoveFlags.HighVolume) &&
            (HasFlag(detail, MoveFlags.Spike) || HasFlag(detail, MoveFlags.Plunge)) &&
            detail.Evidence.NewsCount <= 2)
            matches.Add(new HypeSignalMatch
            {
                SignalId = "volume-first-divergence",
                Name = "Volume-first divergence",
                TriggerEvidence = new List<string>
                {
                    $"peak flags: {string.Join(", ", detail.Flags)}",
                    $"only {detail.Evidence.NewsCount} pre-peak news item(s) in move evidence",
                },
            });

        var management = ThreadsInCategory(detail, "MANAGEMENT");
        if (management.Count > 0)
            matches.Add(new HypeSignalMatch
            {
                SignalId = "leadership-turbulence",
                Name = "Leadership turbulence",
                TriggerEvidence = management
                    .Select(t => $"MANAGEMENT thread: {t.RepresentativeTitle}")
                    .ToList(),
                TriggerThreadIds = management.SelectMany(t => t.ArticleIds).Distinct().ToList(),
            });

        if (detail.SentimentDirection == SentimentDivergence.Disagree)
            matches.Add(new HypeSignalMatch
            {
                SignalId = "sentiment-split",
                Name = "Sentiment split",
                TriggerEvidence = new List<string>
                {
                    "scored pre-peak news leans against the price move (contrarian divergence)",
                },
            });

        var supply = ThreadsInCategory(detail, "SUPPLY_CHAIN");
        if (supply.Count > 0 && HasWarmingToTenseShift(detail))
            matches.Add(new HypeSignalMatch
            {
                SignalId = "supply-tremor",
                Name = "Supply-chain tremor",
                TriggerEvidence = supply
                    .Select(t => $"SUPPLY_CHAIN thread: {t.RepresentativeTitle}")
                    .Concat(new[] { "regime path shifts warming→tense inside the pre-peak window" })
                    .ToList(),
                TriggerThreadIds = supply.SelectMany(t => t.ArticleIds).Distinct().ToList(),
            });

        return matches;
    }

    private static List<HypeCaseThread> ThreadsInCategory(HypeCaseDetail detail, string category) =>
        detail.PrePeakThreads
            .Where(t => string.Equals(t.TopCategory, category, StringComparison.Ordinal))
            .ToList();

    private static bool HasFlag(HypeCaseDetail detail, string flag) =>
        detail.Flags.Any(f => string.Equals(f, flag, StringComparison.OrdinalIgnoreCase));

    private static bool AreaOk(HypeCaseDetail detail, string area) =>
        detail.CompletenessByArea.TryGetValue(area, out var status) &&
        status != HypeCompleteness.Missing;

    private static bool HasWarmingToTenseShift(HypeCaseDetail detail)
    {
        var ordered = detail.RegimePath
            .OrderBy(kv => kv.Key)
            .Select(kv => kv.Value)
            .ToList();
        var warmingIdx = ordered.FindIndex(v => v == MarketRegimes.Warming);
        return warmingIdx >= 0 && ordered.Skip(warmingIdx + 1).Any(v => v == MarketRegimes.Tense);
    }
}
