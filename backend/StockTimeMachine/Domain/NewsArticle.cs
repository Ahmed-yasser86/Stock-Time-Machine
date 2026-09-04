namespace StockTimeMachine;

public class NewsArticle
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Source { get; set; } = "";
    public DateTime PublishedAt { get; set; }
    public string Url { get; set; } = "";
    public string CompanySymbol { get; set; } = "";
    // Per-entity sentiment (-1..+1) where the provider supplies it (MarketAux).
    // Transport-only enrichment, ignored by persistence (see DbContext) so
    // existing databases keep working without migrations.
    // Null = provider did not supply a score.
    public decimal? SentimentScore { get; set; }
}
