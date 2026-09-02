using StockTimeMachine.Entities;

namespace StocksApp2.Areas.TimeMachine.Models;

public class TimeMachineViewModel
{
    public string Symbol { get; set; } = "TSLA";
    public DateOnly SnapshotDate { get; set; } = new DateOnly(2020, 1, 15);

    public HistoricalSnapshot? Snapshot { get; set; }
    public string? Error { get; set; }

    public decimal? Price => Snapshot?.Price;
    public decimal? Open => Snapshot?.Open;
    public decimal? High => Snapshot?.High;
    public decimal? Low => Snapshot?.Low;
    public long? Volume => Snapshot?.Volume;
    public string CompanyName => Snapshot?.Company?.Name ?? Symbol;
    public string CompanySector => Snapshot?.Company?.Sector ?? "";
    public List<PricePoint> PriceHistory => Snapshot?.RecentPrices ?? new();
    public List<SecFiling> Filings => Snapshot?.RecentFilings ?? new();
    public List<NewsArticle> News => Snapshot?.RecentNews ?? new();
}
