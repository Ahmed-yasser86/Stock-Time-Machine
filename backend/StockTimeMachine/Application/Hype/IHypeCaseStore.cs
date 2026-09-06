namespace StockTimeMachine;

// Registry of frozen hype-cycle cases. Rows survive job pruning: this is the
// Layer 1 case library that signal mining queries. Mirrors
// IInvestigationJobStore (same async + cancellation shape).
public interface IHypeCaseStore
{
    // Upsert by stable Id ("{SYMBOL}:{peak yyyy-MM-dd}"): reinvestigating a
    // date refreshes the case instead of duplicating it.
    Task<HypeCase> SaveAsync(HypeCase hypeCase, CancellationToken ct = default);
    Task<HypeCase?> GetAsync(string symbol, DateOnly peakDate, CancellationToken ct = default);
    Task<IReadOnlyList<HypeCase>> ListBySymbolAsync(string symbol, int take = 20, CancellationToken ct = default);
    Task<IReadOnlyList<HypeCase>> ListRecentAsync(int take = 50, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
}
