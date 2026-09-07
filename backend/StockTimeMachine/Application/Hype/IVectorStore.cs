namespace StockTimeMachine;

// Vector-store boundary for hype resemblance. Implementations live in
// Infrastructure only (Qdrant today); Application sees plain records.
// Every method degrades to unavailable/empty — callers always keep their
// in-memory path, so a downed store never breaks the feature.
public record VectorPoint(
    string Id,
    float[] Vector,
    string CaseId,
    string Symbol,
    DateOnly PeakDate);

public record VectorHit(
    string Id,
    float[] Vector,
    string CaseId,
    string Symbol,
    DateOnly PeakDate);

public interface IVectorStore
{
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
    Task<(bool Reachable, ulong Points)> HealthAsync(CancellationToken ct = default);
    Task<int> UpsertAsync(
        string caseId, string symbol, DateOnly peakDate,
        IReadOnlyList<(string ArticleId, float[] Vector)> vectors,
        CancellationToken ct = default);
    Task<IReadOnlyList<VectorHit>> SearchAsync(
        float[] query, int limit, CancellationToken ct = default);
    // Case-level pattern points (one vector per hype case, separate
    // collection). Same degrade-to-empty contract as article points.
    Task<int> UpsertCaseAsync(
        string caseId, string symbol, DateOnly peakDate,
        float[] vector, CancellationToken ct = default);
    Task<IReadOnlyList<VectorHit>> SearchCasesAsync(
        float[] query, int limit, CancellationToken ct = default);
    // Structural-only points (Phase 4 dual-query): same cases, structural
    // side only, separate collection. Identical contract.
    Task<int> UpsertStructuralAsync(
        string caseId, string symbol, DateOnly peakDate,
        float[] vector, CancellationToken ct = default);
    Task<IReadOnlyList<VectorHit>> SearchStructuralAsync(
        float[] query, int limit, CancellationToken ct = default);
}
