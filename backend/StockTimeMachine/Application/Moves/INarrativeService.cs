namespace StockTimeMachine;

public class NewsCandidate
{
    public NewsArticle Article { get; set; } = new();
    public ArticleRelevance Relevance { get; set; } = new();
}

public class NarrativeTopicsResult
{
    public string CompanySymbol { get; set; } = "";
    public DateOnly AsOfDate { get; set; }
    public string NewsSource { get; set; } = NewsSources.Gdelt;
    public int ArticlesConsidered { get; set; }
    // Articles actually sent through relevance classification. Must equal
    // ArticlesConsidered — any gap means unevaluated input, which the
    // census invariant below would expose.
    public int ArticlesEvaluated { get; set; }
    public int ArticlesClustered { get; set; }
    // Actual gate output over the evaluated candidates: relevant admitted to
    // embeddings/threads/evidence; irrelevant and uncertain excluded (never
    // silent — the UI prints all three from these fields).
    public int RelevantCount { get; set; }
    public int IrrelevantCount { get; set; }
    public int UncertainCount { get; set; }
    public int ExpansionQueries { get; set; }
    public int ExpansionNew { get; set; }
    public int ExpansionRelevant { get; set; }
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

    // Uncertain candidates awaiting explicit user approval (cutoff-filtered,
    // highest confidence first). Approval/rejection flips the verdict with
    // USER provenance; approved rows enter the normal pipeline downstream.
    Task<IReadOnlyList<NewsCandidate>> GetCandidates(string symbol, DateOnly asOfDate, string? newsSource, CancellationToken ct = default);

    // Thread member inspection (traceability): resolves exact article ids
    // from the SAME cached read the clustering consumed — no re-clustering,
    // no similarity search, no approximation. Order follows the requested
    // ids; ids absent from cache are skipped and logged (the response
    // carries the requested count so shortfalls are visible, never silent).
    // Relevance metadata comes from stored verdicts only — nothing here
    // classifies, embeds, or spends quota.
    Task<IReadOnlyList<NewsCandidate>> GetThreadArticles(
        string symbol, DateOnly asOfDate, string? newsSource,
        IReadOnlyList<string> articleIds, CancellationToken ct = default);

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
