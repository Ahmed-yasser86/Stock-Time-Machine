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
    private readonly IHypeFilingService _filings;
    private readonly ILogger<HypeCaseIndexer> _logger;

    public HypeCaseIndexer(
        IHistoricalDataRepository dataRepo,
        IVectorStore vectors,
        IGeminiClient gemini,
        IHypeFilingService filings,
        ILogger<HypeCaseIndexer> logger)
    {
        _dataRepo = dataRepo;
        _vectors = vectors;
        _gemini = gemini;
        _filings = filings;
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
        // Content mean pools qualified articles only (reason: same
        // retrospective rule — the query side in HypeResemblanceService uses
        // this exact set, so both sides of the cosine always agree).
        var qualifiedIds = HypeCaseProjection.QualifiedArticleIds(detail);
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
        // still join the pattern collection. Filing structured dims ride on
        // both the hybrid and the structural point.
        var matches = HypeSignals.Evaluate(detail);
        var filingInputs = await LoadFilingInputsAsync(detail, ct);
        var qualifiedSet = new HashSet<string>(qualifiedIds, StringComparer.Ordinal);
        var mean = HypeCaseVector.MeanPool(vectors
            .Where(v => qualifiedSet.Contains(v.ArticleId))
            .Select(v => (IReadOnlyList<float>)v.Vector).ToList());
        var caseVector = HypeCaseVector.Build(detail, matches, mean, filingInputs);
        if (caseVector.Any(x => x != 0))
            indexed += await _vectors.UpsertCaseAsync(
                caseId, detail.CompanySymbol, detail.PeakDate, caseVector, ct);
        else
            _logger.LogDebug("Skipping zero case vector for {Case}", caseId);
        // Structural-only point (Phase 4 dual-query): same case, structural
        // side with filing structured dims from stored summaries.
        var structural = HypeCaseVector.BuildStructural(detail, matches, filingInputs);
        if (structural.Any(x => x != 0))
            indexed += await _vectors.UpsertStructuralAsync(
                caseId, detail.CompanySymbol, detail.PeakDate, structural, ct);
        if (vectors.Count > 0)
        {
            indexed += await _vectors.UpsertAsync(
                caseId, detail.CompanySymbol, detail.PeakDate, vectors, ct);
            _logger.LogInformation("Indexed hype case {Case}: {Indexed}/{Cached} vectors",
                caseId, indexed, vectors.Count);
        }
        // Structured filing summaries ride along with indexing (same
        // best-effort rule): bounded inside the service, stored rows skipped,
        // so reindexing is cheap after the first pass.
        try
        {
            // Accession may be absent on rows frozen before it was stored:
            // derive it from the directory URL (exact SEC format).
            await _filings.EnsureSummariesAsync(detail.CompanySymbol,
                detail.Evidence.Filings.Select(f => new SecFiling
                {
                    AccessionNumber = string.IsNullOrWhiteSpace(f.AccessionNumber)
                        ? HypeFilingService.DeriveAccessionNumber(f.Url)
                        : f.AccessionNumber,
                    FormType = f.FormType ?? "",
                    FiledAt = f.FiledAt,
                    Url = f.Url ?? "",
                    CompanySymbol = detail.CompanySymbol,
                }),
                detail.PeakDate, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Filing summaries failed during indexing for {Case}; continuing", caseId);
        }
        return indexed;
    }

    private async Task<IReadOnlyList<HypeCaseVector.FilingVectorInput>> LoadFilingInputsAsync(
        HypeCaseDetail detail, CancellationToken ct)
    {
        var inputs = new List<HypeCaseVector.FilingVectorInput>();
        // No take: filing vector dims aggregate over every stored summary;
        // reads are indexed PK lookups, and missing rows simply contribute
        // nothing. Truncating here would silently drop regulatory dimensions.
        foreach (var filing in detail.Evidence.Filings)
        {
            var accession = string.IsNullOrWhiteSpace(filing.AccessionNumber)
                ? HypeFilingService.DeriveAccessionNumber(filing.Url)
                : filing.AccessionNumber;
            if (string.IsNullOrWhiteSpace(accession))
                continue;
            try
            {
                var row = await _dataRepo.GetFilingSummary(accession, ct);
                if (row is null)
                    continue;
                inputs.Add(HypeCaseVector.FromRecord(row, row.StructuredJson));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Filing vector input miss for {Accession}", filing.AccessionNumber);
            }
        }
        return inputs;
    }
}
