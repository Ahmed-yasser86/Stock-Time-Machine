namespace StocksApp2.Areas.TimeMachine.Models;

public class TimeMachineViewModel
{
    public string Symbol { get; set; } = "TSLA";
    public DateOnly SnapshotDate { get; set; } = new DateOnly(2020, 1, 15);
    public decimal? Price { get; set; }
    public decimal? NextDayPrice { get; set; }
    public decimal? GainLoss => Price.HasValue && NextDayPrice.HasValue
        ? NextDayPrice.Value - Price.Value
        : null;
    public decimal? GainLossPercent => Price.HasValue && Price.Value != 0 && GainLoss.HasValue
        ? Math.Round(GainLoss.Value / Price.Value * 100, 2)
        : null;
    public List<FilingInfo> Filings { get; set; } = new();
    public string? Error { get; set; }
}

public class FilingInfo
{
    public string FormType { get; set; } = "";
    public DateTime FiledAt { get; set; }
    public string Url { get; set; } = "";
}
