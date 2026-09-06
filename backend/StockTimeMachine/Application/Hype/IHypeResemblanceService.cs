namespace StockTimeMachine;

// "This peak resembles past peaks": max-pairwise embedding cosine between the
// triggering threads' article vectors and each library case's thread vectors.
// Recall aid only — resemblance scores assist, never trigger (see
// HypeSignals). Cache-only vectors on both sides: no fresh embedding spend,
// no quota; cases without cached vectors are skipped explicitly.
public class HypeCaseResemblance
{
    public string CaseId { get; set; } = "";
    public string Symbol { get; set; } = "";
    public DateOnly PeakDate { get; set; }
    public double Similarity { get; set; }
}

public interface IHypeResemblanceService
{
    Task<IReadOnlyList<HypeCaseResemblance>> FindResemblingAsync(
        HypeCaseDetail current,
        IReadOnlyList<string> triggerArticleIds,
        IReadOnlyList<HypeCase> library,
        CancellationToken ct = default);
}
