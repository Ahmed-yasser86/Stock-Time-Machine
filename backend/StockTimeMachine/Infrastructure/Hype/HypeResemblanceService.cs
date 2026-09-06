using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

public class HypeResemblanceService : IHypeResemblanceService
{
    // Same join threshold as compare/threads: below this, pairs are vocabulary
    // coincidence, not resemblance worth showing.
    private const double SimilarityThreshold = 0.70;
    private const int MaxResults = 5;
    private const int MaxTriggerArticles = 30;
    private const int MaxLibraryCases = 100;

    private readonly IHistoricalDataRepository _dataRepo;
    private readonly IGeminiClient _gemini;
    private readonly ILogger<HypeResemblanceService> _logger;

    public HypeResemblanceService(
        IHistoricalDataRepository dataRepo,
        IGeminiClient gemini,
        ILogger<HypeResemblanceService> logger)
    {
        _dataRepo = dataRepo;
        _gemini = gemini;
        _logger = logger;
    }

    public async Task<IReadOnlyList<HypeCaseResemblance>> FindResemblingAsync(
        HypeCaseDetail current,
        IReadOnlyList<string> triggerArticleIds,
        IReadOnlyList<HypeCase> library,
        CancellationToken ct = default)
    {
        var empty = Array.Empty<HypeCaseResemblance>();
        if (current is null || library.Count == 0)
            return empty;
        if (!_gemini.IsEnabled)
            return empty;
        var model = _gemini.EmbeddingModel;
        if (string.IsNullOrWhiteSpace(model))
            return empty;

        // Trigger threads when the signal has a thread basis, else the full
        // pre-peak thread set (volume/sentiment signals carry no articles).
        var wanted = triggerArticleIds.Count > 0
            ? triggerArticleIds
            : current.PrePeakThreads.SelectMany(t => t.ArticleIds).Distinct().ToList();
        var currentVectors = await LoadCachedAsync(wanted.Take(MaxTriggerArticles).ToList(), model, ct);
        if (currentVectors.Count == 0)
            return empty;

        var scored = new List<HypeCaseResemblance>();
        foreach (var row in library.Take(MaxLibraryCases))
        {
            if (row.Id == current.CompanySymbol + ":" + current.PeakDate.ToString("yyyy-MM-dd"))
                continue;
            var detail = HypeCaseLibrary.TryReadDetail(row);
            if (detail is null)
                continue;
            var ids = detail.PrePeakThreads.SelectMany(t => t.ArticleIds).Distinct().Take(MaxTriggerArticles).ToList();
            if (ids.Count == 0)
                continue;
            var vectors = await LoadCachedAsync(ids, model, ct);
            if (vectors.Count == 0)
                continue;
            // Same-article pairs (overlapping investigation windows share
            // cached rows) join at 1.0 by construction and prove nothing:
            // resemblance must come from distinct articles only.
            var best = 0.0;
            foreach (var (aId, a) in currentVectors)
                foreach (var (bId, b) in vectors)
                {
                    if (string.Equals(aId, bId, StringComparison.Ordinal))
                        continue;
                    best = Math.Max(best, EmbeddingClustering.Cosine(a, b));
                }
            if (best >= SimilarityThreshold)
                scored.Add(new HypeCaseResemblance
                {
                    CaseId = row.Id,
                    Symbol = row.CompanySymbol,
                    PeakDate = row.PeakDate,
                    Similarity = Math.Round(best, 3),
                });
        }
        return scored.OrderByDescending(s => s.Similarity).Take(MaxResults).ToList();
    }

    // Cache-only: a missing vector is skipped, never embedded on demand
    // (resemblance must not spend quota or stall the page). Ids ride along
    // so same-article pairs can be excluded from the join.
    private async Task<List<(string Id, IReadOnlyList<float> Vector)>> LoadCachedAsync(
        IReadOnlyList<string> ids, string model, CancellationToken ct)
    {
        var vectors = new List<(string, IReadOnlyList<float>)>();
        foreach (var id in ids)
        {
            try
            {
                var row = await _dataRepo.GetEmbedding(id, model, ct);
                if (row is null || string.IsNullOrWhiteSpace(row.VectorJson))
                    continue;
                var vec = JsonSerializer.Deserialize<float[]>(row.VectorJson);
                if (vec is { Length: > 0 })
                    vectors.Add((id, vec));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Resemblance cache miss for {Article}", id);
            }
        }
        return vectors;
    }
}
