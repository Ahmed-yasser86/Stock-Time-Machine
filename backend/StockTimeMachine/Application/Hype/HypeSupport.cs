using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

// Registry-mining helpers for hype signals: supporter search (same-trigger
// cases with no-hindsight exclusion) and realized-aftermath aggregation.
// Pure logic over already-loaded rows — no IO, no providers. Extracted from
// HypeController so the pipeline math is unit-testable without HTTP.
public static class HypeSupport
{
    // Registry cases where the same signal trigger fired, excluding the
    // current case itself, each paired with its readable detail (single
    // evaluation pass feeds both supporter refs and the realized-aftermath
    // panel). Unreadable rows are skipped (never fatal: mining degrades to
    // fewer supporters, logged loudly).
    public static List<(HypeCase Row, HypeCaseDetail Detail)> FindSupportingCases(
        IReadOnlyList<HypeCase> library,
        string signalId,
        string excludeId,
        DateOnly peakDate,
        ILogger logger)
    {
        var supporters = new List<(HypeCase, HypeCaseDetail)>();
        foreach (var row in library)
        {
            if (row.Id == excludeId)
                continue;
            // No hindsight (Issue 1): a supporter must have peaked no later
            // than the peak it explains — future cases never count as
            // "seen before", even when they sit inside the as-of window.
            if (row.PeakDate > peakDate)
                continue;
            var detail = HypeCaseLibrary.TryReadDetail(row);
            if (detail is null)
            {
                logger.LogWarning("Skipping unreadable hype case {Id} in signal search", row.Id);
                continue;
            }
            bool fires;
            try
            {
                fires = HypeSignals.Evaluate(detail).Any(m => m.SignalId == signalId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Signal evaluation failed for hype case {Id}; skipping", row.Id);
                continue;
            }
            if (fires)
                supporters.Add((row, detail));
        }
        return supporters;
    }

    // Aggregate realized move per supporting case (first→last recorded
    // close), then median/high/low across cases with reaction data.
    // Decimal math (money-adjacent); nulls when no case has ≥2 closes.
    public static HypeFollowedSummary SummarizeFollowed(
        List<(HypeCase Row, HypeCaseDetail Detail)> supporting)
    {
        var moves = supporting
            .Select(s => s.Detail.Reaction)
            .Where(r => r.Count >= 2 && r.First().Close != 0)
            .Select(r => (r.Last().Close - r.First().Close) / r.First().Close * 100m)
            .OrderBy(p => p)
            .ToList();
        if (moves.Count == 0)
            return new HypeFollowedSummary(0, null, null, null);
        var mid = moves.Count / 2;
        var median = moves.Count % 2 == 1
            ? moves[mid]
            : (moves[mid - 1] + moves[mid]) / 2m;
        return new HypeFollowedSummary(
            moves.Count,
            Math.Round(median, 2),
            Math.Round(moves.Max(), 2),
            Math.Round(moves.Min(), 2));
    }
}

// Application-level aftermath aggregate. The Web layer maps this 1:1 to
// HypeFollowedSummaryDto; the split keeps Web DTOs out of Application.
public sealed record HypeFollowedSummary(
    int CasesWithReaction,
    decimal? MedianMovePct,
    decimal? ObservedHighPct,
    decimal? ObservedLowPct);
