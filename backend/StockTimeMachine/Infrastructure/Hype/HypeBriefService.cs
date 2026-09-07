using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

public class HypeBriefService : IHypeBriefService
{
    private const int MaxInputs = 5;
    private const int MaxBodyChars = 1500;

    private readonly IGeminiClient _gemini;
    private readonly ICompanyDirectory _directory;
    private readonly IHypeFilingService _filings;
    private readonly IHistoricalDataRepository _dataRepo;
    private readonly ILogger<HypeBriefService> _logger;

    public HypeBriefService(
        IGeminiClient gemini,
        ICompanyDirectory directory,
        IHypeFilingService filings,
        IHistoricalDataRepository dataRepo,
        ILogger<HypeBriefService> logger)
    {
        _gemini = gemini;
        _directory = directory;
        _filings = filings;
        _dataRepo = dataRepo;
        _logger = logger;
    }

    public async Task<ClusterBrief?> BriefSignalAsync(
        string symbol,
        DateOnly asOfDate,
        HypeSignalMatch match,
        HypeCaseDetail detail,
        CancellationToken ct = default)
    {
        if (match is null || detail is null)
            return null;
        if (!_gemini.IsEnabled)
            return null;
        try
        {
            // Grounding = signal-relevant content ONLY (Issue 2): threads in
            // brief-material categories, news items passing deterministic
            // materiality, social posts passing the same bar, plus the full
            // stage facts via HypeBriefPrompt. Noise is filtered HERE, before
            // any token is spent — never inside the prompt.
            var normalized = symbol.Trim().ToUpperInvariant();
            var wanted = new HashSet<string>(match.TriggerThreadIds, StringComparer.Ordinal);
            var candidateThreads = detail.PrePeakThreads
                .Where(t => t.ArticleIds.Any(id => wanted.Contains(id)))
                .ToList();
            if (candidateThreads.Count == 0)
                candidateThreads = detail.PrePeakThreads.Take(MaxInputs).ToList();
            var threads = HypeBriefInputFilter.FilterThreads(candidateThreads).ToList();
            // Company name resolution (same directory the snapshot lens uses):
            // coverage names companies, not tickers.
            string? companyName = null;
            try
            {
                if (_directory.TryGet(normalized, out var info) && info is not null)
                    companyName = info.Name;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Company lookup failed for {Symbol}; ticker-only filtering", normalized);
            }
            var relevantNews = HypeBriefInputFilter.FilterNews(normalized, companyName, detail.Evidence.News);
            var relevantSocial = HypeBriefInputFilter.FilterSocial(normalized, companyName, detail.Evidence.Social);
            _logger.LogInformation(
                "Hype brief filter for {Signal}: {Threads}/{ThreadsTotal} threads, {News}/{NewsTotal} news, {Social}/{SocialTotal} social pass",
                match.SignalId, threads.Count, candidateThreads.Count,
                relevantNews.Count, detail.Evidence.News.Count,
                relevantSocial.Count, detail.Evidence.Social.Count);
            var newsByTitle = relevantNews
                .GroupBy(n => n.Title ?? "", StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var inputs = new List<(string Title, string Body)>();
            foreach (var t in threads.Take(MaxInputs))
            {
                var body = !string.IsNullOrWhiteSpace(t.BriefSummary)
                    ? t.BriefSummary!
                    : string.Join(" ", t.LabelTerms);
                if (newsByTitle.TryGetValue(t.RepresentativeTitle ?? "", out var item) &&
                    !string.IsNullOrWhiteSpace(item.Description))
                    body = item.Description + " (" + item.Source + ", " + item.PublishedAt.ToString("yyyy-MM-dd") + ")";
                if (body.Length > MaxBodyChars)
                    body = body.Substring(0, MaxBodyChars);
                inputs.Add((t.RepresentativeTitle ?? "(untitled thread)", body));
            }
            // Regulatory context: stored summaries first (zero re-fetch), live
            // generation only for filings with no stored row. Either way the
            // brief receives summaries, never raw filing text.
            IReadOnlyList<HypeFilingSummary> filingSummaries = Array.Empty<HypeFilingSummary>();
            try
            {
                filingSummaries = await SummariesFromStoreAsync(detail, ct)
                    ?? await _filings.SummarizeFilingsAsync(symbol, match, detail, asOfDate, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Filing summaries failed for {Signal}; briefing without regulatory context",
                    match.SignalId);
            }
            var prompt = HypeBriefPrompt.Build(symbol, asOfDate, match, detail, inputs, relevantSocial, filingSummaries);
            return await _gemini.SummarizeClusterAsync(prompt, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hype signal brief failed for {Signal}; continuing without", match.SignalId);
            return null;
        }
    }

    // Stored summaries (Phase 5 reuse): one row per accession, newest three
    // filings first. Null unless EVERY filing has a stored row — partial
    // coverage falls back to live generation so one brief never mixes stored
    // prose with fresh prose.
    private async Task<IReadOnlyList<HypeFilingSummary>?> SummariesFromStoreAsync(
        HypeCaseDetail detail, CancellationToken ct)
    {
        var filings = detail.Evidence.Filings
            .OrderByDescending(f => f.FiledAt)
            .Take(3)
            .ToList();
        if (filings.Count == 0)
            return new List<HypeFilingSummary>();
        var summaries = new List<HypeFilingSummary>();
        foreach (var filing in filings)
        {
            // Same legacy fallback as the indexer: derive from directory URL
            // when the frozen row predates accession storage.
            var accession = string.IsNullOrWhiteSpace(filing.AccessionNumber)
                ? HypeFilingService.DeriveAccessionNumber(filing.Url)
                : filing.AccessionNumber;
            if (string.IsNullOrWhiteSpace(accession))
                return null;
            FilingSummaryRecord? row = null;
            try
            {
                row = await _dataRepo.GetFilingSummary(accession, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Stored filing summary miss for {Accession}", accession);
                return null;
            }
            if (row is null)
                return null;
            summaries.Add(new HypeFilingSummary
            {
                FormType = row.FormType,
                FiledAt = filing.FiledAt,
                Findings = row.Findings,
                Disclosures = row.Disclosures,
                ConfidenceNote = row.ConfidenceNote + " (stored)",
                TotalPages = 0,
                PagesProcessed = 0,
            });
        }
        _logger.LogInformation("Briefing from {Count} stored filing summaries (zero re-fetch)", summaries.Count);
        return summaries;
    }
}
