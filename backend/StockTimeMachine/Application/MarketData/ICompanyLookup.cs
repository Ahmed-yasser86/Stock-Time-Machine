namespace StockTimeMachine;

/// <summary>
/// Optional fallback company-profile provider. STM's primary sources are
/// the local <see cref="ICompanyDirectory"/> and SEC EDGAR. Implementations
/// of this interface are tried in order; first non-null wins. Used by the
/// Web composition root to wire in Finnhub as a tertiary fallback so the
/// legacy Stocks trade area is reused without coupling STM to it.
/// </summary>
public interface ICompanyLookup
{
    Task<Company?> GetCompanyProfileAsync(string symbol, CancellationToken ct = default);
}
