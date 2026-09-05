namespace StockTimeMachine;

// Quarantined AI surface. Everything behind this interface is explicitly
// NON-deterministic and hindsight-exposed (model weights postdate every
// investigation cutoff): outputs are labeled AI-generated, never feed the
// deterministic pipeline, and every failure degrades to the TF-IDF path.
public class ClusterBrief
{
    public string Summary { get; set; } = "";
    public List<string> KeyPoints { get; set; } = new();
    public string Model { get; set; } = "";
}

public interface IGeminiClient
{
    bool IsEnabled { get; }
    string SummaryModel { get; }
    string EmbeddingModel { get; }
    // One embedding vector per text, order-preserving. Throws on any failure
    // so callers can fall back to deterministic clustering.
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
    // Brief for one pre-clustered thread. Returns null when the model declines
    // or the response is unusable — never throws for content reasons.
    Task<ClusterBrief?> SummarizeClusterAsync(string prompt, CancellationToken ct = default);
    // Structured review of a user note: one verdict per cited claim. Empty
    // (not null) when the model declines — reviewers report, never conclude.
    Task<IReadOnlyList<NoteIssue>> ReviewNoteAsync(string prompt, CancellationToken ct = default);
}
