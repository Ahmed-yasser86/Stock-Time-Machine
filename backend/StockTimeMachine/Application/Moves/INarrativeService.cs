namespace StockTimeMachine;

public class NarrativeTopicsResult
{
    public string CompanySymbol { get; set; } = "";
    public DateOnly AsOfDate { get; set; }
    public string NewsSource { get; set; } = NewsSources.Gdelt;
    public int ArticlesConsidered { get; set; }
    public List<TopicCluster> Topics { get; set; } = new();
    // "gemini-embeddings" when the AI path held end to end, else
    // "tf-idf-fallback" — the UI prints whichever it was.
    public string ClusteringMethod { get; set; } = "tf-idf-fallback";
}

public interface INarrativeService
{
    // Window-level narrative threads from CACHED news only (no live provider
    // fetch; warmed automatically by snapshot/moves investigations). Empty
    // cache yields empty topics — an honest reflection of coverage.
    // AI path (Gemini embeddings + per-thread briefs) is attempted first when
    // configured; any failure degrades to the deterministic TF-IDF path.
    Task<NarrativeTopicsResult> GetTopics(string symbol, DateOnly asOfDate, string? newsSource, CancellationToken ct = default);
}
