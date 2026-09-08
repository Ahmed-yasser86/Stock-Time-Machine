namespace StockTimeMachine.Web.Models.Dto;

// DTOs for the hype-cycle surface. Separate file (like MoveDtos.cs) so
// existing contracts are never touched by hype work.
public sealed record HypeCaseRefDto(
    string Id,
    string Symbol,
    DateOnly PeakDate,
    IReadOnlyList<string> Flags,
    string Completeness,
    // Source provenance of the registry case (registry joins are
    // cross-source by design; the badge tells which source it came from).
    string NewsSource);

public sealed record HypeResemblanceDto(
    string CaseId,
    string Symbol,
    DateOnly PeakDate,
    double Similarity,
    string Kind,
    // Same provenance rule as supporters; "" = unknown (no badge).
    string NewsSource);

public sealed record HypeReactionDto(DateOnly Date, decimal Close);

public sealed record HypeFollowedCaseDto(
    string CaseId,
    string Symbol,
    DateOnly PeakDate,
    IReadOnlyList<HypeReactionDto> Reaction,
    // Same provenance rule as supporters.
    string NewsSource);

// Aggregate realized aftermath across supporting cases: first→last recorded
// close per case, then median/high/low. Description only — the UI labels it
// "Observed in past cases — never a forecast".
public sealed record HypeFollowedSummaryDto(
    int CasesWithReaction,
    decimal? MedianMovePct,
    decimal? ObservedHighPct,
    decimal? ObservedLowPct);

// Pattern-score distribution over the registry: each case's best non-self
// pattern similarity. Decides the PatternThreshold — never hardcoded blind.
public sealed record HypeCaseStatsResponse(
    int CasesScanned,
    int CasesMatched,
    double Min,
    double Max,
    double Median,
    double P25,
    double P75,
    IReadOnlyList<int> Buckets);

// Structured trigger evidence (Issue 7): display text plus the thread
// metadata behind it, so users weight each item themselves.
public sealed record TriggerEvidenceItemDto(
    string Text,
    int ThreadSize,
    double? RelevanceRate,
    string Category,
    string CategoryBasis);

public sealed record HypeSignalDto(
    string Id,
    string Name,
    string Trigger,
    IReadOnlyList<TriggerEvidenceItemDto> TriggerEvidence,
    IReadOnlyList<HypeCaseRefDto> SupportingCases,
    // Step 4 resemblance (recall aid); Step 6 realized aftermath below.
    IReadOnlyList<HypeResemblanceDto> Resemblance,
    IReadOnlyList<HypeFollowedCaseDto> Followed,
    HypeFollowedSummaryDto FollowedSummary,
    // Non-directionality disclosure (Issue 6): the signal describes
    // pre-peak information conditions, never move direction.
    string DirectionalNote,
    // Regime-relativity footnote (Issue 8): regimes are tertiled within
    // each case's own window. Rendered once per card section, not per row.
    string RegimeNote);

public sealed record HypePeakDto(
    DateOnly PeakDate,
    decimal DailyReturnPct,
    double Score,
    IReadOnlyList<string> Flags,
    string Completeness,
    IReadOnlyList<HypeSignalDto> Signals,
    // Freeze-then-read label (Issue 4): "" when the live recomputation
    // matches the frozen registry row (or no row exists yet), otherwise
    // "Recomputed now — may differ from frozen registry record."
    string RecomputedNote);

public sealed record HypeSignalsResponse(
    CompanySummaryDto Company,
    DateOnly AsOfDate,
    string NewsSource,
    int CasesConsidered,
    IReadOnlyList<HypePeakDto> Peaks);

// Sector sweep row (Issue 10): one symbol's peaks, or an error row when
// that symbol alone failed. Error is null on success — never both.
public sealed record HypeSectorRowDto(
    string Symbol,
    CompanySummaryDto Company,
    string? Error,
    IReadOnlyList<HypePeakDto> Peaks);

// Sector sweep envelope (Issue 10): one shared cutoff for every row; each
// row stands alone — no pooled verdicts, no cross-symbol scores.
public sealed record HypeSectorResponse(
    DateOnly AsOfDate,
    string NewsSource,
    IReadOnlyList<HypeSectorRowDto> Rows);

public sealed record HypeBriefRequest(
    string? Symbol,
    string? Date,
    string? NewsSource,
    string? PeakDate,
    string? SignalId,
    // Frozen-row brief path (Issue 4, optional): "SYMBOL:yyyy-MM-dd".
    // When it resolves to a readable registry row, the frozen detail is
    // briefed; otherwise the request falls back to live recomputation.
    string? CaseId = null);

public sealed record HypeBriefResponse(ClusterBriefDto? Brief);
