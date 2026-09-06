using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

public class RelevanceService : IRelevanceService
{
    private const int BatchSize = 25;

    private readonly IHistoricalDataRepository _dataRepo;
    private readonly IGeminiClient _gemini;
    private readonly ILogger<RelevanceService> _logger;

    public RelevanceService(
        IHistoricalDataRepository dataRepo,
        IGeminiClient gemini,
        ILogger<RelevanceService> logger)
    {
        _dataRepo = dataRepo;
        _gemini = gemini;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, ArticleRelevance>> ClassifyAsync(
        string symbol, DateOnly asOfDate, string? companyName, string? sector,
        IReadOnlyList<NewsArticle> articles, CancellationToken ct = default)
    {
        var normalized = symbol.Trim().ToUpperInvariant();
        var result = new Dictionary<string, ArticleRelevance>(StringComparer.Ordinal);
        var missing = new List<NewsArticle>();
        foreach (var article in articles)
        {
            ArticleRelevance? cached = null;
            try
            {
                cached = await _dataRepo.GetRelevance(article.Id, normalized, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Relevance cache read miss for {Article}", article.Id);
            }
            if (cached is not null)
                result[article.Id] = cached;
            else
                missing.Add(article);
        }
        if (missing.Count == 0 || !_gemini.IsEnabled)
            return result;

        string company = !string.IsNullOrWhiteSpace(companyName) ? companyName.Trim() : normalized;
        for (int start = 0; start < missing.Count; start += BatchSize)
        {
            var batch = missing.Skip(start).Take(BatchSize).ToList();
            List<RelevanceVerdict> verdicts;
            try
            {
                var prompt = RelevancePrompt.Build(company, normalized, asOfDate, sector,
                    batch.Select(d => (d.Id, d.Title)).ToList());
                verdicts = (await _gemini.ClassifyRelevanceAsync(prompt, ct)).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Relevance batch failed for {Symbol}; marking unknown", normalized);
                continue;
            }
            var byId = verdicts
                .Where(v => batch.Any(d => d.Id == v.Id))
                .GroupBy(v => v.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var rows = new List<ArticleRelevance>();
            foreach (var doc in batch)
            {
                if (!byId.TryGetValue(doc.Id, out var verdict))
                    continue; // omitted or malformed → unknown, never guessed
                // Normalize defensively: any IGeminiClient implementation feeds
                // this path, not just the validating one.
                var category = (verdict.Category ?? "").ToUpperInvariant();
                if (!RelevancePrompt.Categories.Contains(category))
                    category = "UNRELATED";
                rows.Add(new ArticleRelevance
                {
                    ArticleId = doc.Id,
                    Symbol = normalized,
                    Model = _gemini.SummaryModel,
                    Relevant = verdict.Relevant,
                    Category = category,
                    Confidence = Math.Clamp(verdict.Confidence, 0, 1),
                    Reason = verdict.Reason.Length > 500 ? verdict.Reason.Substring(0, 500) : verdict.Reason,
                });
            }
            if (rows.Count == 0)
                continue;
            try
            {
                await _dataRepo.StoreRelevances(rows, ct);
                foreach (var row in rows)
                    result[row.ArticleId] = row;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Relevance store failed for {Symbol}", normalized);
            }
        }
        return result;
    }
}
