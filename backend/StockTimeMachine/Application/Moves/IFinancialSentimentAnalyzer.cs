namespace StockTimeMachine;

public class ScoredArticle
{
    public string ArticleId { get; set; } = "";
    public double Score { get; set; }
    public double Confidence { get; set; }
    public string Model { get; set; } = "";
    public string Revision { get; set; } = "";
    public bool FromCache { get; set; }
}

public interface IFinancialSentimentAnalyzer
{
    bool IsEnabled { get; }
    string ModelId { get; }
    // Score cached articles through local FinBERT. Only rows with
    // PublishedAt <= cutoff and non-empty text are eligible; everything else
    // is excluded (never coerced). Disabled/unreachable sidecar yields cached
    // scores only. Never throws for content reasons.
    Task<IReadOnlyList<ScoredArticle>> EnsureScoredAsync(
        IReadOnlyList<NewsArticle> articles, DateOnly cutoff, CancellationToken ct = default);
}
