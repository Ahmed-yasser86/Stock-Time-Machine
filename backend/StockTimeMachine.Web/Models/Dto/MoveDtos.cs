namespace StockTimeMachine.Web.Models.Dto;

// DTOs for the moves investigation surface. Separate file (not ApiDtos.cs) so
// the existing snapshot contract is never touched by moves work.
public sealed record KeyMoveDto(
    DateOnly Date,
    decimal Close,
    decimal DailyReturnPct,
    double ZScore,
    double VolumeRatio,
    decimal FiveDayMomentumPct,
    double Score,
    IReadOnlyList<string> Flags,
    string SentimentDirection);

public sealed record MarketReactionDto(DateOnly Date, decimal Close);

public sealed record MoveFilingDto(string AccessionNumber, string FormType, DateTime FiledAt, string Url);

public sealed record MoveNewsDto(string Id, string Title, string Source, DateTime PublishedAt, string Url, decimal? SentimentScore);

public sealed record SocialSignalDto(
    string Provider,
    string Community,
    string Title,
    string Excerpt,
    string Url,
    DateTime CreatedAt,
    int Score,
    int CommentCount,
    string? Flair);

public sealed record ArrivalEntryDto(
    string Layer,
    DateTime? FirstSeen,
    string State,
    double? LagHours,
    string Detail);

public sealed record MoveEvidenceDto(
    IReadOnlyList<MoveFilingDto> Filings,
    IReadOnlyList<MoveNewsDto> News,
    IReadOnlyList<SocialSignalDto> Social,
    IReadOnlyList<MarketReactionDto> Reaction,
    IReadOnlyList<string> UnavailableLayers,
    IReadOnlyList<ArrivalEntryDto> Arrival);

public sealed record WindowSummaryDto(
    int TradingDays,
    decimal CumulativeReturnPct,
    double Volatility,
    decimal MaxDrawdownPct,
    DateOnly? BestDay,
    decimal BestDayReturnPct,
    DateOnly? WorstDay,
    decimal WorstDayReturnPct,
    bool SufficientHistory);

public sealed record UncertaintyComponentDto(string Name, double Weight, double Value, string Detail);

public sealed record UncertaintyIndexDto(double Score, IReadOnlyList<UncertaintyComponentDto> Components);

public sealed record ClusterBriefDto(
    string Summary,
    IReadOnlyList<string> KeyPoints,
    string Model);

public sealed record TopicClusterDto(
    IReadOnlyList<string> LabelTerms,
    IReadOnlyList<string> ArticleIds,
    string RepresentativeTitle,
    DateTime? SpanStart,
    DateTime? SpanEnd,
    ClusterBriefDto? Brief);

public sealed record NarrativesResponse(
    CompanySummaryDto Company,
    DateOnly AsOfDate,
    string NewsSource,
    int ArticlesConsidered,
    string ClusteringMethod,
    IReadOnlyList<TopicClusterDto> Topics);

public sealed record CompareBriefResponse(
    IReadOnlyList<string> Symbols,
    DateOnly AsOfDate,
    string NewsSource,
    IReadOnlyList<string> Terms,
    ClusterBriefDto? Brief);

public sealed record CrossThreadPairDto(
    string ASymbol,
    string ATitle,
    string BSymbol,
    string BTitle,
    double Similarity);

public sealed record CompareThreadsResponse(
    IReadOnlyList<string> Symbols,
    DateOnly AsOfDate,
    string NewsSource,
    IReadOnlyList<CrossThreadPairDto> Pairs);

public sealed record CopilotBriefResponse(
    string Symbol,
    DateOnly AsOfDate,
    string Action,
    ClusterBriefDto? Brief);

public sealed record NoteIssueDto(string Ref, string Verdict, string Detail);

public sealed record ReviewResponse(
    string Symbol,
    DateOnly AsOfDate,
    IReadOnlyList<NoteIssueDto> Issues);

public sealed record MovesResponse(
    CompanySummaryDto Company,
    DateOnly DecisionDate,
    string NewsSource,
    WindowSummaryDto Summary,
    IReadOnlyList<KeyMoveDto> KeyMoves,
    Dictionary<string, MoveEvidenceDto> EvidenceByDate,
    IReadOnlyList<PricePointDto> WindowPrices,
    UncertaintyIndexDto Uncertainty,
    Dictionary<string, string> Regimes);
