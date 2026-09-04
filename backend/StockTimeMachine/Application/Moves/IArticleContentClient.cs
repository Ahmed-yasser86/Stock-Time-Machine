namespace StockTimeMachine;

// Full-article body fetch (Jina Reader). Bodies are retrieved NOW, not at the
// investigation cutoff: pages may have been updated since publication, so every
// returned body carries RetrievedAtUtc and presenters must disclose it.
public class ArticleBody
{
    public string Markdown { get; set; } = "";
    public DateTime RetrievedAtUtc { get; set; }
}

public interface IArticleContentClient
{
    bool IsEnabled { get; }
    // Null on any failure (paywall, bot-block, timeout, quota): callers fall
    // back to stored title + description. Never throws for content reasons.
    Task<ArticleBody?> FetchBodyAsync(string articleUrl, CancellationToken ct = default);
}
