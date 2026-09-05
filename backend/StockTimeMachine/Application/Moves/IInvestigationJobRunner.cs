namespace StockTimeMachine;

public interface IInvestigationJobRunner
{
    // Persist a running job and launch its pipeline in the background.
    // Returns the durable job id; the timer starts now (60 minutes default).
    Task<string> StartAsync(string symbol, DateOnly asOfDate, string newsSource, CancellationToken ct = default);
    // Live task tracking for observability/tests. Terminal jobs leave the map.
    int RunningCount { get; }
    // Hard execution limit, from Start. Read by the stream endpoint so stale
    // rows (crashed restarts) are reaped to timeout on read.
    TimeSpan Timeout { get; }
}
