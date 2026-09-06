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
    // Tri-state decision + provenance (RELEVANT | IRRELEVANT | UNCERTAIN |
    // USER_APPROVED; AI | RULE | USER). Unknown (never classified) has no row.
    public string Decision { get; set; } = "UNCERTAIN";
    public string DecisionSource { get; set; } = "AI";
}

public static class RelevanceDecisions
{
    public const string Relevant = "RELEVANT";
    public const string Irrelevant = "IRRELEVANT";
    public const string Uncertain = "UNCERTAIN";
    public const string UserApproved = "USER_APPROVED";
}

public static class RelevanceSources
{
    public const string Ai = "AI";
    public const string Rule = "RULE";
    public const string User = "USER";
}

public class RelevanceVerdict
{
    public string Id { get; set; } = "";
    public bool Relevant { get; set; }
    public string Category { get; set; } = "UNRELATED";
    public double Confidence { get; set; }
    public string Reason { get; set; } = "";
}
