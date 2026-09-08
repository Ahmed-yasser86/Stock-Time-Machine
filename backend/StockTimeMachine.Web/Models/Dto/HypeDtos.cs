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

public sealed record HypeSignalDto(
    string Id,
    string Name,
    string Trigger,
    IReadOnlyList<string> TriggerEvidence,
    IReadOnlyList<HypeCaseRefDto> SupportingCases,
    // Step 4 resemblance (recall aid); Step 6 realized aftermath below.
    IReadOnlyList<HypeResemblanceDto> Resemblance,
    IReadOnlyList<HypeFollowedCaseDto> Followed,
    HypeFollowedSummaryDto FollowedSummary);

public sealed record HypePeakDto(
    DateOnly PeakDate,
    decimal DailyReturnPct,
    double Score,
    IReadOnlyList<string> Flags,
    string Completeness,
    IReadOnlyList<HypeSignalDto> Signals);

public sealed record HypeSignalsResponse(
    CompanySummaryDto Company,
    DateOnly AsOfDate,
    string NewsSource,
    int CasesConsidered,
    IReadOnlyList<HypePeakDto> Peaks);

public sealed record HypeBriefRequest(
    string? Symbol,
    string? Date,
    string? NewsSource,
    string? PeakDate,
    string? SignalId);

public sealed record HypeBriefResponse(ClusterBriefDto? Brief);
