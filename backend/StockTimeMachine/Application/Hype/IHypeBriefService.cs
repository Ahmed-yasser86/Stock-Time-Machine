namespace StockTimeMachine;

// Analyst summary for one detected signal: grounded prose over the
// triggering threads' titles + stored briefs, same containment prompt family
// as narrative briefs (cutoff roleplay, citations, causation ban). Narrates
// only — the trigger already fired deterministically. Null when AI is off,
// inputs are empty, or the model declines (fail-soft, like thread briefs).
public interface IHypeBriefService
{
    Task<ClusterBrief?> BriefSignalAsync(
        string symbol,
        DateOnly asOfDate,
        HypeSignalMatch match,
        HypeCaseDetail detail,
        CancellationToken ct = default);
}
