namespace StockTimeMachine;

// Persisted semantic relevance verdict for one article under one investigation
// symbol. Relevance is investigation-relative (same article can matter to MSFT
// and not to AAPL), hence the composite key. Unknown (null Relevant) means
// "not classified", never "irrelevant".
public class ArticleRelevance
{
    public string ArticleId { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string Model { get; set; } = "";
    public bool? Relevant { get; set; }
    public string Category { get; set; } = "UNRELATED";
    public double Confidence { get; set; }
    public string Reason { get; set; } = "";
    public DateTime ClassifiedAt { get; set; }
}

public class RelevanceVerdict
{
    public string Id { get; set; } = "";
    public bool Relevant { get; set; }
    public string Category { get; set; } = "UNRELATED";
    public double Confidence { get; set; }
    public string Reason { get; set; } = "";
}
