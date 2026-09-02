namespace StockTimeMachine.Entities;

public class NewsArticle
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Source { get; set; } = "";
    public DateTime PublishedAt { get; set; }
    public string Url { get; set; } = "";
    public string CompanySymbol { get; set; } = "";
}
