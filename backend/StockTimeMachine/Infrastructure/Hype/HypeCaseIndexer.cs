using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

// Indexes a frozen case's admitted pre-peak thread articles into the vector
// store (vectors come from the ArticleEmbeddings read-through cache — never
// freshly embedded here, so indexing spends zero quota). Called after case
// persistence (job runner hook, harvest, reindex). Best-effort: failures are
// logged, never thrown.
public class HypeCaseIndexer : IHypeCaseIndexer
{
    private readonly IHistoricalDataRepository _dataRepo;
    private readonly IVectorStore _vectors;
    private readonly IGeminiClient _gemini;
    private readonly ILogger<HypeCaseIndexer> _logger;

    public HypeCaseIndexer(
        IHistoricalDataRepository dataRepo,
        IVectorStore vectors,
        IGeminiClient gemini,
        ILogger<HypeCaseIndexer> logger)
    {
        _dataRepo = dataRepo;
        _vectors = vectors;
        _gemini = gemini;
        _logger = logger;
    }

    public async Task<int> IndexCaseAsync(HypeCaseDetail detail, CancellationToken ct = default)
    {
        if (detail is null)
            return 0;
        if (!await _vectors.IsAvailableAsync(ct))
            return 0;
        var model = _gemini.IsEnabled ? _gemini.EmbeddingModel : "";
        if (string.IsNullOrWhiteSpace(model))
            return 0;
        var caseId = detail.CompanySymbol + ":" + detail.PeakDate.ToString("yyyy-MM-dd");
        var indexed = 0;
        var ids = detail.PrePeakThreads
            .SelectMany(t => t.ArticleIds)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var vectors = new List<(string ArticleId, float[] Vector)>();
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
                _logger.LogDebug(ex, "Indexer cache miss for {Article}", id);
            }
        }
        // Case-level pattern point: needs no threads, only any measurable
        // content (score/regimes/sentiment suffice). Thread-less cases must
        // still join the pattern collection.
        var matches = HypeSignals.Evaluate(detail);
        var mean = HypeCaseVector.MeanPool(vectors.Select(v => (IReadOnlyList<float>)v.Vector).ToList());
        var caseVector = HypeCaseVector.Build(detail, matches, mean);
        if (caseVector.Any(x => x != 0))
            indexed += await _vectors.UpsertCaseAsync(
                caseId, detail.CompanySymbol, detail.PeakDate, caseVector, ct);
        else
            _logger.LogDebug("Skipping zero case vector for {Case}", caseId);
        if (vectors.Count > 0)
        {
            indexed += await _vectors.UpsertAsync(
                caseId, detail.CompanySymbol, detail.PeakDate, vectors, ct);
            _logger.LogInformation("Indexed hype case {Case}: {Indexed}/{Cached} vectors",
                caseId, indexed, vectors.Count);
        }
        return indexed;
    }
}
