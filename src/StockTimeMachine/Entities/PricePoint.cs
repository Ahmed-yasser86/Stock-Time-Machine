namespace StockTimeMachine.Entities;

public class PricePoint
{
    public DateOnly Date { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }
    public string CompanySymbol { get; set; } = "";
}
