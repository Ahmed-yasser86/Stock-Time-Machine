
namespace StockTimeMachine;

public interface ITimeMachineService
{
    // Uses the configured default news source.
    Task<HistoricalSnapshot> GetSnapshot(string symbol, DateOnly asOfDate, CancellationToken ct = default);

    // Uses the explicitly selected news source ("gdelt" or "alphavantage").
    // Sources are never mixed or silently substituted.
    Task<HistoricalSnapshot> GetSnapshot(string symbol, DateOnly asOfDate, string? newsSource, CancellationToken ct = default);

    // Rescoped investigation: only the given sections are resolved
    // (see SnapshotSections). Null or empty means all sections.
    // progress receives one honest SnapshotProgress per reconstruction step
    // (US-06); null disables reporting.
    Task<HistoricalSnapshot> GetSnapshot(string symbol, DateOnly asOfDate, string? newsSource, IReadOnlySet<string>? sections, IProgress<SnapshotProgress>? progress = null, CancellationToken ct = default);
}
