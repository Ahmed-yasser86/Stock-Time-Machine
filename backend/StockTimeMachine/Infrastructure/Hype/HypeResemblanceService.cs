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
    // Self-exclusion window: same-symbol cases this close share most of their
    // pre-peak window (and near-duplicate wire copies), so joins saturate at
    // 1.00 by construction. Resemblance must compare distinct events only.
    private const int SelfExclusionDays = 30;

    // ANN ranks candidates; the join math stays local (same threshold, same
    // exclusions) so Qdrant extends the in-memory path instead of replacing
    // its semantics. Any vector-store failure falls back to the in-memory
    // join below — never an error, never a stall.
    private const int VectorQueryVectors = 8;
    private const int VectorQueryTopK = 10;
    // PROVISIONAL (Issue: threshold decision pending): no cut on pattern
    // matches yet — ranked top-N flow through so the measured score
    // distribution (see hype/case-stats) decides the real threshold.
    // Do not ship a tuned value here without that report.
    private const double PatternThreshold = 0.0;
    private const int PatternTopK = 10;
    private const int NarrativeTopK = 10;
    // Merge policy: pattern hits lead, but at most PatternLeadSlots of them —
    // the rest of the list belongs to narrative-only matches. Without the cap,
    // the uncut pattern query (threshold pending) would starve the narrative
    // layer entirely and the two layers could never be compared.
    private const int PatternLeadSlots = 3;

    private readonly IHistoricalDataRepository _dataRepo;
    private readonly IGeminiClient _gemini;
    private readonly IVectorStore _vectors;
    private readonly ILogger<HypeResemblanceService> _logger;

    public HypeResemblanceService(
        IHistoricalDataRepository dataRepo,
        IGeminiClient gemini,
        IVectorStore vectors,
        ILogger<HypeResemblanceService> logger)
    {
        _dataRepo = dataRepo;
        _gemini = gemini;
        _vectors = vectors;
        _logger = logger;
    }

    public async Task<IReadOnlyList<HypeCaseResemblance>> FindResemblingAsync(
        HypeCaseDetail current,
        IReadOnlyList<string> triggerArticleIds,
        IReadOnlyList<HypeCase> library,
        CancellationToken ct = default)
    {
        var empty = Array.Empty<HypeCaseResemblance>();
        if (current is null)
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

        // Pattern layer first (hype_cases): the current case's 40-d vector is
        // pure local math. Narrative layer second (hype_threads via vector
        // store, else in-memory). Merge ranks pattern hits first,
        // narrative-only after; exclusions apply to both.
        var patternHits = await MatchPatternsAsync(current, currentVectors, ct);
        var narrativeHits = await JoinViaVectorStoreAsync(current, currentVectors, ct)
            ?? await JoinInMemoryAsync(current, currentVectors, model, library, ct);
        // Pattern leads (capped), narrative fills: first-wins dedupe keeps a
        // case both layers agree on at its pattern rank.
        var merged = new List<HypeCaseResemblance>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var hit in patternHits.Take(PatternLeadSlots).Concat(narrativeHits))
        {
            if (seen.Add(hit.CaseId))
                merged.Add(hit);
            if (merged.Count >= MaxResults)
                break;
        }
        return merged;
    }

    private async Task<IReadOnlyList<HypeCaseResemblance>> MatchPatternsAsync(
        HypeCaseDetail current,
        List<(string Id, IReadOnlyList<float> Vector)> currentVectors,
        CancellationToken ct)
    {
        var empty = Array.Empty<HypeCaseResemblance>();
        try
        {
            if (!await _vectors.IsAvailableAsync(ct))
                return empty;
            var mean = HypeCaseVector.MeanPool(currentVectors.Select(v => v.Vector).ToList());
            var query = HypeCaseVector.Build(current, HypeSignals.Evaluate(current), mean);
            if (query.All(x => x == 0))
                return empty;
            var hits = await _vectors.SearchCasesAsync(query, PatternTopK, ct);
            var scored = new List<HypeCaseResemblance>();
            foreach (var hit in hits)
            {
                if (string.Equals(hit.CaseId,
                        current.CompanySymbol + ":" + current.PeakDate.ToString("yyyy-MM-dd"),
                        StringComparison.Ordinal))
                    continue;
                if (Excluded(current, hit.Symbol, hit.PeakDate))
                    continue;
                var sim = EmbeddingClustering.Cosine(query, hit.Vector);
                if (sim >= PatternThreshold)
                    scored.Add(new HypeCaseResemblance
                    {
                        CaseId = hit.CaseId,
                        Symbol = hit.Symbol,
                        PeakDate = hit.PeakDate,
                        Similarity = ReportSimilarity(sim),
                        Kind = HypeResemblanceKinds.Pattern,
                    });
            }
            return scored.OrderByDescending(h => h.Similarity).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pattern match failed; continuing with narrative matches");
            return empty;
        }
    }

    private async Task<IReadOnlyList<HypeCaseResemblance>> JoinInMemoryAsync(
        HypeCaseDetail current,
        List<(string Id, IReadOnlyList<float> Vector)> currentVectors,
        string model,
        IReadOnlyList<HypeCase> library,
        CancellationToken ct)
    {
        var scored = new List<HypeCaseResemblance>();
        foreach (var row in library.Take(MaxLibraryCases))
        {
            if (row.Id == current.CompanySymbol + ":" + current.PeakDate.ToString("yyyy-MM-dd"))
                continue;
            if (Excluded(current, row.CompanySymbol, row.PeakDate))
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
                    Similarity = ReportSimilarity(best),
                });
        }
        return scored.OrderByDescending(s => s.Similarity).Take(MaxResults).ToList();
    }

    public async Task<float[]?> BuildCaseQueryAsync(HypeCaseDetail detail, CancellationToken ct = default)
    {
        if (detail is null || !_gemini.IsEnabled)
            return null;
        var model = _gemini.EmbeddingModel;
        if (string.IsNullOrWhiteSpace(model))
            return null;
        var ids = detail.PrePeakThreads
            .SelectMany(t => t.ArticleIds).Distinct(StringComparer.Ordinal).ToList();
        var vectors = await LoadCachedAsync(ids, model, ct);
        var mean = HypeCaseVector.MeanPool(vectors.Select(v => v.Vector).ToList());
        var built = HypeCaseVector.Build(detail, HypeSignals.Evaluate(detail), mean);
        return built.Any(x => x != 0) ? built : null;
    }

    // Null = vector store unavailable/failed: caller runs the in-memory join.
    private async Task<IReadOnlyList<HypeCaseResemblance>?> JoinViaVectorStoreAsync(
        HypeCaseDetail current,
        List<(string Id, IReadOnlyList<float> Vector)> currentVectors,
        CancellationToken ct)
    {
        try
        {
            if (!await _vectors.IsAvailableAsync(ct))
                return null;
            var currentIds = new HashSet<string>(
                currentVectors.Select(v => v.Id), StringComparer.Ordinal);
            var bestByCase = new Dictionary<string, (HypeCaseResemblance Hit, double Best)>(StringComparer.Ordinal);
            foreach (var (aId, a) in currentVectors.Take(VectorQueryVectors))
            {
                var hits = await _vectors.SearchAsync(a.ToArray(), VectorQueryTopK, ct);
                foreach (var hit in hits)
                {
                    if (string.Equals(aId, hit.Id, StringComparison.Ordinal))
                        continue;
                    if (!Excluded(current, hit.Symbol, hit.PeakDate))
                    {
                        var sim = EmbeddingClustering.Cosine(a, hit.Vector);
                        if (sim >= SimilarityThreshold &&
                            (!bestByCase.TryGetValue(hit.CaseId, out var prev) || sim > prev.Best))
                            bestByCase[hit.CaseId] = (new HypeCaseResemblance
                            {
                                CaseId = hit.CaseId,
                                Symbol = hit.Symbol,
                                PeakDate = hit.PeakDate,
                                Similarity = ReportSimilarity(sim),
                            }, sim);
                    }
                }
            }
            return bestByCase.Values
                .Select(v => v.Hit)
                .OrderByDescending(h => h.Similarity)
                .Take(MaxResults)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vector-store resemblance failed; falling back to in-memory join");
            return null;
        }
    }

    // Display clamp: 0.9995+ would print as "1.00". Exact 1.00 is reserved
    // for byte-identical vectors (already excluded as same-article pairs),
    // so anything reaching the ceiling displays capped, honestly.
    private static double ReportSimilarity(double sim) =>
        sim >= 1.0 ? 0.999 : Math.Min(Math.Round(sim, 3), 0.999);

    private bool Excluded(HypeCaseDetail current, string symbol, DateOnly peakDate)
    {
        if (string.Equals(symbol, current.CompanySymbol, StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(peakDate.DayNumber - current.PeakDate.DayNumber) <= SelfExclusionDays)
            return true;
        return false;
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
