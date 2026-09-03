namespace StockTimeMachine;

public class HistoricalSnapshot
{
    public string CompanySymbol { get; set; } = "";
    public DateOnly SnapshotDate { get; set; }
    // True when a real closing price exists on/before SnapshotDate. Readers must
    // use this — never Price == 0 — to detect missing market data.
    public bool HasMarketData { get; set; }
    // Trading day the quoted close belongs to. When earlier than SnapshotDate,
    // markets were closed on the selected date (weekend/holiday).
    public DateOnly? PriceDate { get; set; }
    // News source that produced RecentNews: "gdelt" or "alphavantage". Never mixed.
    public string NewsSource { get; set; } = NewsSources.Gdelt;
    public decimal Price { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public long Volume { get; set; }
    public List<PricePoint> RecentPrices { get; set; } = new();
    public List<SecFiling> RecentFilings { get; set; } = new();
    public List<NewsArticle> RecentNews { get; set; } = new();
    public Company? Company { get; set; }
    public List<PricePoint> OutcomePrices { get; set; } = new();
    public decimal? OutcomePrice { get; set; }
    // Post-cutoff SEC filings for the reveal. Never part of the historical sections.
    public List<SecFiling> OutcomeFilings { get; set; } = new();
    // Sections whose provider failed ("prices", "filings", "outcome", "news").
    // Non-empty means the snapshot is PARTIAL and must be labeled as such.
    public List<string> FailedSections { get; set; } = new();
}
