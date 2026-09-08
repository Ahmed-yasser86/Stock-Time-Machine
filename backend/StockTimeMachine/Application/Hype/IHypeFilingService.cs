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
    // Citation back to the primary document (SEC EDGAR): the brief prompt
    // prints these so every filing claim is traceable. Empty when the
    // frozen row predates accession storage and no URL was kept.
    public string AccessionNumber { get; set; } = "";
    public string Url { get; set; } = "";
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

    // Structured per-filing extraction for persistence (Phase 3): fetches the
    // document, sizes it with the same rules, and returns a validated
    // FilingSummaryRecord (structured JSON + short prose + content hash).
    // Null when the form family is unsupported, the fetch fails, or the
    // model output is invalid. Callers persist the record; briefs and vector
    // dims read it back instead of re-fetching.
    Task<FilingSummaryRecord?> SummarizeStructuredAsync(
        string symbol,
        string accessionNumber,
        string formType,
        DateTime filedAt,
        string documentUrl,
        DateOnly asOfDate,
        CancellationToken ct = default);

    // Bounded auto-generation (max 5 unique filings, most recent first):
    // skips accessions already stored (filings are immutable per accession,
    // so absence is the only re-generation trigger). Returns new row count.
    // Called at investigation completion (job runner) and by the harvest
    // endpoint — never on the read path.
    Task<int> EnsureSummariesAsync(
        string symbol,
        IEnumerable<SecFiling> filings,
        DateOnly asOfDate,
        CancellationToken ct = default);
}
