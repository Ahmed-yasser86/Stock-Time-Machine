namespace StockTimeMachine;

public class SecFiling
{
    public string AccessionNumber { get; set; } = "";
    public string FormType { get; set; } = "";
    public DateTime FiledAt { get; set; }
    public DateTime PeriodOfReport { get; set; }
    public string Url { get; set; } = "";
    public string Summary { get; set; } = "";
    public string CompanySymbol { get; set; } = "";

    // 8-K (and amendments) are material-event disclosures. This single canonical
    // definition backs the "Corporate Disclosures" split everywhere (service, API, UI).
    public bool IsMaterialDisclosure =>
        string.Equals(FormType, "8-K", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(FormType, "8-K/A", StringComparison.OrdinalIgnoreCase);
}
