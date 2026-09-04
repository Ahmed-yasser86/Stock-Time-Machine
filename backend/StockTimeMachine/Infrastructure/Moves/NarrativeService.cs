using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

public class NarrativeService : INarrativeService
{
    private readonly IHistoricalDataRepository _dataRepo;
    private readonly ILogger<NarrativeService> _logger;

    public NarrativeService(IHistoricalDataRepository dataRepo, ILogger<NarrativeService> logger)
    {
        _dataRepo = dataRepo;
        _logger = logger;
    }

    public async Task<NarrativeTopicsResult> GetTopics(string symbol, DateOnly asOfDate, string? newsSource, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new InvalidHistoricalDateException("Symbol is required.");
        HistoricalDate.Create(asOfDate);

        var normalized = symbol.Trim().ToUpperInvariant();
        var selected = NewsSources.Normalize(newsSource);
        var cached = await _dataRepo.GetNewsAsOf(normalized, asOfDate, ct);
        var articles = cached.Where(n => IsFromSource(n, selected)).ToList();

        _logger.LogInformation("Narrative topics for {Symbol}: {Count} cached articles", normalized, articles.Count);

        return new NarrativeTopicsResult
        {
            CompanySymbol = normalized,
            AsOfDate = asOfDate,
            NewsSource = selected,
            ArticlesConsidered = articles.Count,
            Topics = TopicClustering.Cluster(articles),
        };
    }

    private static bool IsFromSource(NewsArticle article, string newsSource)
    {
        var source = article.Source ?? "";
        if (newsSource == NewsSources.AlphaVantage)
            return source.Contains("Alpha Vantage", StringComparison.OrdinalIgnoreCase);
        if (newsSource == NewsSources.MarketAux)
            return source.Contains("MarketAux", StringComparison.OrdinalIgnoreCase);
        return source.Contains("GDELT", StringComparison.OrdinalIgnoreCase);
    }
}
