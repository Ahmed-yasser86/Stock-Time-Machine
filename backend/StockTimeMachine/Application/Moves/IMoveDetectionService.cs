namespace StockTimeMachine;

public interface IMoveDetectionService
{
    // Analyzes the last 100 trading days on/before asOfDate and attaches
    // per-move evidence, each item already filtered to that move's own cutoff.
    // Never throws for provider failures (layers degrade to honest empty);
    // throws InvalidHistoricalDateException for bad input and
    // HistoricalDataNotFoundException when history is insufficient.
    // topMoves overrides the product top-5 cap for offline case harvesting
    // only (reason: Step 7 of hype-intelligence-plan); null keeps the default.
    Task<MovesWindow> GetMoves(string symbol, DateOnly asOfDate, string? newsSource = null, CancellationToken ct = default, IProgress<SnapshotProgress>? progress = null, int? topMoves = null);
}
