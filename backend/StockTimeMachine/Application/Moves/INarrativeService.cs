namespace StockTimeMachine;

public class NarrativeTopicsResult
{
    public string CompanySymbol { get; set; } = "";
    public DateOnly AsOfDate { get; set; }
    public string NewsSource { get; set; } = NewsSources.Gdelt;
    public int ArticlesConsidered { get; set; }
    public List<TopicCluster> Topics { get; set; } = new();
}

public interface INarrativeService
{
    // Window-level narrative threads from CACHED news only (no live fetch:
    // zero quota cost, fully deterministic for a given database state).
    // Empty cache yields empty topics — an honest reflection of coverage,
    // warmed automatically by snapshot/moves investigations.
    Task<NarrativeTopicsResult> GetTopics(string symbol, DateOnly asOfDate, string? newsSource, CancellationToken ct = default);
}
