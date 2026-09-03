
namespace StockTimeMachine;

public interface INewsProvider
{
    Task<IReadOnlyList<NewsArticle>> SearchAsync(string symbol, DateOnly cutoffDate, CancellationToken ct = default);

    // Company-name-aware resolution. Providers that resolve entities by name
    // (e.g. GDELT Cloud) use companyName when present; others ignore it.
    // Default implementation preserves backward compatibility.
    Task<IReadOnlyList<NewsArticle>> SearchAsync(string symbol, string? companyName, DateOnly cutoffDate, CancellationToken ct = default) =>
        SearchAsync(symbol, cutoffDate, ct);
}