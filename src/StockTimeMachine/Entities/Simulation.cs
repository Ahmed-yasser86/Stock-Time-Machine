namespace StockTimeMachine.Entities;

public class Simulation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CompanySymbol { get; set; } = "";
    public DateOnly EntryDate { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal InvestmentAmount { get; set; }
    public decimal SharesPurchased { get; set; }
    public DateOnly? ExitDate { get; set; }
    public decimal? ExitPrice { get; set; }
    public decimal FinalValue { get; set; }
    public decimal ReturnPercentage { get; set; }
}
