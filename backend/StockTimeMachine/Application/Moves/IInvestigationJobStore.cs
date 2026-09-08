namespace StockTimeMachine;

public interface IInvestigationJobStore
{
    Task<InvestigationJob> CreateAsync(InvestigationJob job, CancellationToken ct = default);
    Task<InvestigationJob?> GetAsync(string id, CancellationToken ct = default);
    Task AppendStageAsync(string id, JobStage stage, CancellationToken ct = default);
    // Terminal transitions are compare-and-swap: only a running job moves.
    // Returns false when the job was already terminal (race lost).
    Task<bool> CompleteAsync(string id, string movesJson, string narrativesJson, CancellationToken ct = default);
    // Backfill writer: replaces the moves payload of a NON-running job
    // (complete/timeout/failed/cancelled). Returns false for unknown ids or
    // running jobs, which must never be rewritten mid-flight.
    Task<bool> UpdateMovesJsonAsync(string id, string movesJson, CancellationToken ct = default);
    Task<bool> FailAsync(string id, string status, string? error, CancellationToken ct = default);
    // Lazy reaper: a job still running past its deadline is declared timed
    // out on read, so restarts and crashes never leave eternal rows.
    Task ReapStaleAsync(TimeSpan timeout, CancellationToken ct = default);
    // Bounded table growth: drop terminal jobs older than the retention.
    Task PruneAsync(TimeSpan retention, CancellationToken ct = default);
    // Backfill enumeration: all rows newest-first (table is retention-
    // bounded, so this stays small). Readers filter by status themselves.
    Task<IReadOnlyList<InvestigationJob>> ListAllAsync(CancellationToken ct = default);
}
