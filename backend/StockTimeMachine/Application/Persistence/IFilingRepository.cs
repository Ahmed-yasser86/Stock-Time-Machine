
namespace StockTimeMachine;

// Focused SEC-filing persistence port (ISP split from IHistoricalDataRepository).
// Includes persisted per-filing summaries (hype filing structured extraction).
public interface IFilingRepository
{
    Task StoreFilings(string companySymbol, IEnumerable<SecFiling> filings, CancellationToken ct = default);
    Task<IReadOnlyList<SecFiling>> GetFilingsAsOf(string companySymbol, DateOnly asOfDate, CancellationToken ct = default);
    // Post-cutoff regulatory evidence for the "What Happened Afterwards" reveal.
    // Strictly after the cutoff of fromDate, up to days later.
    Task<IReadOnlyList<SecFiling>> GetFilingsAfter(string companySymbol, DateOnly fromDate, int days = 30, CancellationToken ct = default);
    Task<FilingSummaryRecord?> GetFilingSummary(string accessionNumber, CancellationToken ct = default);
    Task StoreFilingSummary(FilingSummaryRecord row, CancellationToken ct = default);
}
