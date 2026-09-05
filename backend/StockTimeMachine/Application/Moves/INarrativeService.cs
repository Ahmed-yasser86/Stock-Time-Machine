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
    Task<NarrativeTopicsResult> GetTopics(string symbol, DateOnly asOfDate, string? newsSource, CancellationToken ct = default, IProgress<SnapshotProgress>? progress = null);

    // Cross-pick shared-story brief: articles matching the shared terms across
    // the given symbols' caches, briefed as ONE story with per-article
    // citations. Never a joint verdict — the prompt bans cross-company
    // causation and pooled conclusions. Null when nothing matches or AI is off.
    Task<ClusterBrief?> BriefSharedThread(
        IReadOnlyList<string> symbols, DateOnly asOfDate, string? newsSource,
        IReadOnlyList<string> terms, CancellationToken ct = default);

    // Cross-pick thread similarity: per-symbol embedding clusters joined by
    // max-pairwise cosine across picks. Deterministic given the vectors;
    // vectors themselves are model-generated. Empty when AI is off.
    Task<IReadOnlyList<CrossThreadPair>> CrossThreadSimilarity(
        IReadOnlyList<string> symbols, DateOnly asOfDate, string? newsSource,
        CancellationToken ct = default);
}

public class CrossThreadPair
{
    public string ASymbol { get; set; } = "";
    public string ATitle { get; set; } = "";
    public string BSymbol { get; set; } = "";
    public string BTitle { get; set; } = "";
    public double Similarity { get; set; }
}
