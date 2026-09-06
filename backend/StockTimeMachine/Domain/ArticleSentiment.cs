namespace StockTimeMachine;

// Persisted FinBERT sentiment for one article version. TextHash binds the
// analysis to the exact analyzed text: if title/description change, the hash
// misses and the article is re-scored instead of silently reusing a stale
// verdict. Key includes the model so a model change cleanly misses.
public class ArticleSentiment
{
    public string ArticleId { get; set; } = "";
    public string Model { get; set; } = "";
    public string TextHash { get; set; } = "";
    public double Pos { get; set; }
    public double Neu { get; set; }
    public double Neg { get; set; }
    public double Score { get; set; }
    public double Confidence { get; set; }
    public DateTime ScoredAt { get; set; }
}
