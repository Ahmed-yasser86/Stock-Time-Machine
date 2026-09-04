namespace StockTimeMachine;

// Normalized retail-discussion signal. The temporal basis is always the item's
// own publication instant (CreatedAt); reposts/edited content without original
// timestamps must never be mapped into this model.
public class SocialSignal
{
    public string Id { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Community { get; set; } = "";
    public string Title { get; set; } = "";
    public string Excerpt { get; set; } = "";
    public string Url { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int Score { get; set; }
    public int CommentCount { get; set; }
    public string? Flair { get; set; }
    public string CompanySymbol { get; set; } = "";
}

// Normalized public-attention point. Values are relative indices (0-100), NEVER
// absolute volume — presenters must disclose normalization.
public class InterestPoint
{
    public DateOnly Date { get; set; }
    public int Index { get; set; }
    public string Keyword { get; set; } = "";
}
