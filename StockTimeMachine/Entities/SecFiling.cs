namespace StockTimeMachine.Entities;

public class SecFiling
{
    public string AccessionNumber { get; set; } = "";
    public string FormType { get; set; } = "";
    public DateTime FiledAt { get; set; }
    public DateTime PeriodOfReport { get; set; }
    public string Url { get; set; } = "";
    public string Summary { get; set; } = "";
    public string CompanySymbol { get; set; } = "";
}
