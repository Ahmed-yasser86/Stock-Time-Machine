namespace StockTimeMachine;

// Persisted embedding vector for one article under one model. Read-through
// cache: repeat investigations reuse vectors instead of re-spending provider
// quota (every narratives load used to re-embed everything). Keyed by
// article id + model so a model change cleanly misses instead of mixing
// vector spaces.
public class ArticleEmbedding
{
    public string ArticleId { get; set; } = "";
    public string Model { get; set; } = "";
    public string VectorJson { get; set; } = "";
    public DateTime CachedAt { get; set; }
}
