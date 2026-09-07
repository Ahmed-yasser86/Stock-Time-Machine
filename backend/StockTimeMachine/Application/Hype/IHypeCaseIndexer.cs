namespace StockTimeMachine;

// Case → vector-store indexing. Called after cases are frozen (job runner
// hook, harvest endpoint, reindex endpoint): reads cached article vectors,
// writes thread-article points. Best-effort by contract — indexing failures
// are logged, never thrown into the investigation path.
public interface IHypeCaseIndexer
{
    Task<int> IndexCaseAsync(HypeCaseDetail detail, CancellationToken ct = default);
}
