namespace StockTimeMachine;

public interface IMoveDetectionService
{
    // Analyzes the last 100 trading days on/before asOfDate and attaches
    // per-move evidence, each item already filtered to that move's own cutoff.
    // Never throws for provider failures (layers degrade to honest empty);
    // throws InvalidHistoricalDateException for bad input and
    // HistoricalDataNotFoundException when history is insufficient.
    Task<MovesWindow> GetMoves(string symbol, DateOnly asOfDate, string? newsSource = null, CancellationToken ct = default, IProgress<SnapshotProgress>? progress = null);
}
