namespace StockTimeMachine;

// Per-filing regulatory context for hype briefs. Ingestion-first: filing rows
// carry metadata + directory URL only, so text is fetched from SEC EDGAR,
// sized (full / section-retrieved / extractive-first), then summarized ONE
// filing at a time. The brief pipeline receives summaries only — never raw
// filing text. Sequential, bounded, fail-soft per filing.
public class HypeFilingSummary
{
    public string FormType { get; set; } = "";
    public DateTime FiledAt { get; set; }
    public string Findings { get; set; } = "";
    public string Disclosures { get; set; } = "";
    // full | partial:{reason} | unavailable:{reason} — always stated.
    public string ConfidenceNote { get; set; } = "unavailable: not processed";
    public int PagesProcessed { get; set; }
    public int TotalPages { get; set; }
}

public interface IHypeFilingService
{
    Task<IReadOnlyList<HypeFilingSummary>> SummarizeFilingsAsync(
        string symbol,
        HypeSignalMatch match,
        HypeCaseDetail detail,
        DateOnly asOfDate,
        CancellationToken ct = default);
}
