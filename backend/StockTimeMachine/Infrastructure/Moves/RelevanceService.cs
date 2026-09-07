using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

public class RelevanceService : IRelevanceService
{
    private const int BatchSize = 25;
    private const int MaxVariations = 8;
    // Below this confidence the verdict is UNCERTAIN, never forced relevant.
    // 0.65 (not 0.55): the model is overconfident on invented "broader
    // sector" connections, so low-confidence RELEVANT claims are excluded.
    private const double UncertainBelow = 0.65;
    private const int MinRelevantEvidence = 5;

    private static readonly string[] QueryVariations = new[]
    {
        "earnings", "revenue", "guidance", "lawsuit", "regulation",
        "merger acquisition", "layoffs", "product launch",
    };

    private readonly IHistoricalDataRepository _dataRepo;
    private readonly IGeminiClient _gemini;
    private readonly GdeltNewsProvider _project;
    private readonly ILogger<RelevanceService> _logger;

    public RelevanceService(
        IHistoricalDataRepository dataRepo,
        IGeminiClient gemini,
        GdeltNewsProvider project,
        ILogger<RelevanceService> logger)
    {
        _dataRepo = dataRepo;
        _gemini = gemini;
        _project = project;
        _logger = logger;
    }

    public static bool PassesGate(ArticleRelevance? row) =>
        row is not null && (row.Decision == RelevanceDecisions.Relevant ||
            row.Decision == RelevanceDecisions.UserApproved);

    bool IRelevanceService.PassesGate(ArticleRelevance? row) => PassesGate(row);

    private static string CompanyDisplayName(string? companyName, string normalized) =>
        !string.IsNullOrWhiteSpace(companyName) ? companyName.Trim() : normalized;

    // Model stamp for AI verdicts: summary model + prompt version. A cached AI
    // row with any other stamp was judged under an older policy.
    private string VerdictModel => _gemini.SummaryModel + "|" + RelevancePrompt.Version;

    // Bounded retrieval expansion: deterministic finance keyword variations
    // against the Project DOC keyword API (the only GDELT transport with
    // keyword search), one single-range call each, cutoff-filtered, stored,
    // then classified through the same gate. At most MaxVariations calls.
    public async Task<ExpansionResult> ExpandAsync(
        string symbol, DateOnly asOfDate, string? companyName, CancellationToken ct = default)
    {
        var result = new ExpansionResult();
        var normalized = symbol.Trim().ToUpperInvariant();
        HistoricalDate.Create(asOfDate);
        var cutoff = TemporalBoundary.GetCutoffUtc(asOfDate);
        var from = asOfDate.AddDays(-7);
        foreach (var variation in QueryVariations.Take(MaxVariations))
        {
            ct.ThrowIfCancellationRequested();
            var query = $"{normalized} {variation}";
            List<NewsArticle> found;
            try
            {
                found = (await _project.SearchRangeAsync(
                    query, normalized, from, asOfDate, asOfDate, 20, ct)).ToList();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                result.FailedQueries.Add(query);
                _logger.LogWarning(ex, "Expansion query failed: {Query}", query);
                continue;
            }
            result.QueriesRun++;
            var fresh = new List<NewsArticle>();
            foreach (var article in found)
            {
                if (article.PublishedAt > cutoff)
                    continue;
                ArticleRelevance? existing = null;
                try
                {
                    existing = await _dataRepo.GetRelevance(article.Id, normalized, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Expansion cache check failed for {Article}", article.Id);
                }
                if (existing is not null)
                    continue;
                fresh.Add(article);
            }
            if (fresh.Count == 0)
                continue;
            try
            {
                await _dataRepo.StoreNews(normalized, fresh, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Expansion store failed for {Query}", query);
                continue;
            }
            result.NewCandidates += fresh.Count;
            var company = CompanyDisplayName(companyName, normalized);
            string? sector = null;
            var map = await ClassifyAsync(normalized, asOfDate, company, sector, fresh, ct);
            result.NewRelevant += map.Values.Count(v => v.Decision == RelevanceDecisions.Relevant);
        }
        _logger.LogInformation(
            "Expansion for {Symbol}: {Queries} queries, {Candidates} new candidates, {Relevant} relevant, failed [{Failed}]",
            normalized, result.QueriesRun, result.NewCandidates, result.NewRelevant,
            string.Join(",", result.FailedQueries));
        return result;
    }

    public Task<IReadOnlyList<ArticleRelevance>> CandidatesAsync(
        string symbol, DateOnly asOfDate, CancellationToken ct = default) =>
        _dataRepo.GetUncertain(symbol, asOfDate, ct);

    public Task<bool> ApproveAsync(string symbol, string articleId, CancellationToken ct = default) =>
        _dataRepo.SetRelevanceDecision(articleId, symbol,
            RelevanceDecisions.UserApproved, RelevanceSources.User, ct);

    public Task<bool> RejectAsync(string symbol, string articleId, CancellationToken ct = default) =>
        _dataRepo.SetRelevanceDecision(articleId, symbol,
            RelevanceDecisions.Irrelevant, RelevanceSources.User, ct);

    // The single authoritative relevance decision. Verdict precedence:
    // USER (explicit approval/rejection — final, never re-judged) > AI
    // (semantic classification) > RULE (deterministic materiality fallback
    // when AI is unavailable). RULE rows are placeholders: a later AI pass
    // re-judges them. Fresh verdicts are ALWAYS returned to the caller — the
    // persistence store is a cache optimization, never a precondition for
    // gating (a store failure must not open the gate).
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
            if (cached is null)
            {
                missing.Add(article);
            }
            else if (cached.DecisionSource == RelevanceSources.User)
            {
                result[article.Id] = cached;
            }
            else if (cached.DecisionSource == RelevanceSources.Rule && _gemini.IsEnabled)
            {
                // Degraded-mode placeholder: AI is back, re-judge properly.
                missing.Add(article);
            }
            else if (cached.DecisionSource == RelevanceSources.Ai &&
                     cached.Model != VerdictModel &&
                     _gemini.IsEnabled)
            {
                // Judged under an older prompt version: re-judge under the
                // current policy instead of trusting stale semantics.
                missing.Add(article);
            }
            else
            {
                result[article.Id] = cached;
            }
        }
        if (missing.Count == 0)
            return result;

        string company = CompanyDisplayName(companyName, normalized);
        if (!_gemini.IsEnabled)
        {
            // AI off: deterministic materiality verdicts, never unknowns.
            await StoreFreshAsync(ClassifyRule(normalized, company, missing), result, ct);
            return result;
        }

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
                // Batch failed: RULE verdicts for THIS batch, never unknowns.
                _logger.LogWarning(ex, "Relevance batch failed for {Symbol}; rule fallback", normalized);
                await StoreFreshAsync(ClassifyRule(normalized, company, batch), result, ct);
                continue;
            }
            var byId = verdicts
                .Where(v => batch.Any(d => d.Id == v.Id))
                .GroupBy(v => v.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var rows = new List<ArticleRelevance>();
            var omitted = new List<NewsArticle>();
            foreach (var doc in batch)
            {
                if (!byId.TryGetValue(doc.Id, out var verdict))
                {
                    // Model omitted this id: RULE fallback, never a silent drop.
                    omitted.Add(doc);
                    continue;
                }
                // Normalize defensively: any IGeminiClient implementation feeds
                // this path, not just the validating one.
                var category = (verdict.Category ?? "").ToUpperInvariant();
                if (!RelevancePrompt.Categories.Contains(category))
                    category = "UNRELATED";
                var confidence = Math.Clamp(verdict.Confidence, 0, 1);
                // UNCERTAIN below threshold: never force a borderline article
                // into either bucket.
                var uncertain = confidence < UncertainBelow;
                rows.Add(new ArticleRelevance
                {
                    ArticleId = doc.Id,
                    Symbol = normalized,
                    Model = VerdictModel,
                    Relevant = uncertain ? null : verdict.Relevant,
                    Category = uncertain ? "UNRELATED" : category,
                    Confidence = confidence,
                    Reason = verdict.Reason.Length > 500 ? verdict.Reason.Substring(0, 500) : verdict.Reason,
                    Decision = uncertain ? RelevanceDecisions.Uncertain :
                        verdict.Relevant ? RelevanceDecisions.Relevant : RelevanceDecisions.Irrelevant,
                    DecisionSource = RelevanceSources.Ai,
                });
            }
            if (omitted.Count > 0)
                await StoreFreshAsync(ClassifyRule(normalized, company, omitted), result, ct);
            await StoreFreshAsync(rows, result, ct);
        }
        return result;
    }

    // Deterministic materiality verdicts (RULE provenance) for one batch.
    private static List<ArticleRelevance> ClassifyRule(
        string normalized, string company, List<NewsArticle> docs)
    {
        return docs.Select(d =>
        {
            var judged = MaterialityRules.Judge(normalized, company, d.Title ?? "", d.Description ?? "");
            return new ArticleRelevance
            {
                ArticleId = d.Id,
                Symbol = normalized,
                Model = "rule-v1",
                Relevant = judged.Relevant,
                Category = judged.Category,
                Confidence = judged.Confidence,
                Reason = judged.Reason,
                Decision = judged.Decision,
                DecisionSource = RelevanceSources.Rule,
            };
        }).ToList();
    }

    // Fresh verdicts enter the returned map FIRST (the current request's gate
    // must use them); persistence is best-effort cache. A store failure must
    // never discard verdicts and open the gate.
    private async Task StoreFreshAsync(
        List<ArticleRelevance> rows,
        Dictionary<string, ArticleRelevance> result,
        CancellationToken ct)
    {
        if (rows.Count == 0)
            return;
        foreach (var row in rows)
            result[row.ArticleId] = row;
        try
        {
            await _dataRepo.StoreRelevances(rows, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Relevance store failed for {Symbol}; verdicts still apply to this request", rows[0].Symbol);
        }
    }
}
