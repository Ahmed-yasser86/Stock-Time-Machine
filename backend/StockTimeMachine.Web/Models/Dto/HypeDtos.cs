namespace StockTimeMachine.Web.Models.Dto;

// DTOs for the hype-cycle surface. Separate file (like MoveDtos.cs) so
// existing contracts are never touched by hype work.
public sealed record HypeCaseRefDto(
    string Id,
    string Symbol,
    DateOnly PeakDate,
    IReadOnlyList<string> Flags,
    string Completeness);

public sealed record HypeResemblanceDto(
    string CaseId,
    string Symbol,
    DateOnly PeakDate,
    double Similarity);

public sealed record HypeReactionDto(DateOnly Date, decimal Close);

public sealed record HypeFollowedCaseDto(
    string CaseId,
    string Symbol,
    DateOnly PeakDate,
    IReadOnlyList<HypeReactionDto> Reaction);

public sealed record HypeSignalDto(
    string Id,
    string Name,
    string Trigger,
    IReadOnlyList<string> TriggerEvidence,
    IReadOnlyList<HypeCaseRefDto> SupportingCases,
    // Step 4 resemblance (recall aid); Step 6 realized aftermath below.
    IReadOnlyList<HypeResemblanceDto> Resemblance,
    IReadOnlyList<HypeFollowedCaseDto> Followed);

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
