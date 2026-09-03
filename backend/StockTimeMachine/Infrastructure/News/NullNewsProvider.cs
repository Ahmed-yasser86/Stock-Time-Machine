using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

public class NullNewsProvider : INewsProvider
{
    private readonly ILogger<NullNewsProvider> _logger;

    public NullNewsProvider(ILogger<NullNewsProvider> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<NewsArticle>> SearchAsync(string symbol, DateOnly cutoffDate, CancellationToken ct = default)
    {
        _logger.LogInformation("NullNewsProvider: no news for {Symbol} (cutoff {Cutoff}) — best-effort fallback", symbol, cutoffDate);
        return Task.FromResult<IReadOnlyList<NewsArticle>>(Array.Empty<NewsArticle>());
    }
}