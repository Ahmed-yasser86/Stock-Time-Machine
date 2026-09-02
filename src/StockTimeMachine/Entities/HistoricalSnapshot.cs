namespace StockTimeMachine.Entities;

public class HistoricalSnapshot
{
    public string CompanySymbol { get; set; } = "";
    public DateOnly SnapshotDate { get; set; }
    public decimal Price { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public long Volume { get; set; }
    public List<PricePoint> RecentPrices { get; set; } = new();
    public List<SecFiling> RecentFilings { get; set; } = new();
    public List<NewsArticle> RecentNews { get; set; } = new();
    public Company? Company { get; set; }
}
