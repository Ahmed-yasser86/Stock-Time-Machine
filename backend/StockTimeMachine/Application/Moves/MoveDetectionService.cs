using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

// Deterministic Key Moves detection over the last 100 trading days.
// Methodology (also published on /methodology):
// score = 0.5 * min(|z|/3, 1) + 0.3 * min(max(volRatio-1,0)/4, 1) + 0.2 * rangeBreak,
// where z is the daily-return z-score vs trailing 20 days, volRatio the volume
// vs trailing-20d median, rangeBreak the fractional close beyond the trailing-20d
// high/low scaled x20 (5% break = full). Rank: score desc, date desc, |ret| desc.
// Statistics use double internally for ranking only; all money stays decimal and
// every displayed price is a real close. Same rows in → same moves out.
public class MoveDetectionService : IMoveDetectionService
{
    private const int WindowSize = 100;
    private const int FetchSize = 130;
    private const int MinRows = 30;
    private const int TopMoves = 5;
    // Coverage-freeze guard: a non-empty cache must not shadow later coverage
    // forever. When the newest cached row from the selected source is older
    // than this many days before the latest move, one live refresh runs per
    // investigation (scoped instance = per request), anchored at the latest
    // move so the provider's trailing window covers the most evidence.
    private const int NewsStaleAfterDays = 7;
    private readonly HashSet<string> _newsRefreshed = new(StringComparer.Ordinal);
    // Per-request fetch-failure guard: when the provider throws for one move
    // (throttled, down), the next moves' weeks must not re-hammer it seconds
    // later. Empty-but-successful fetches still retry per move — different
    // weeks, legitimately different answers.
    private readonly HashSet<string> _newsFetchFailed = new(StringComparer.Ordinal);
    // Retail discussion lookback per move: 7 days back from the move date.
    // A single per-investigation fetch covers [earliestMove-7d, asOf], then
    // each move slices its own [moveDate-7d, moveDate] window.
    public const int SocialLookbackDays = 7;

    private readonly ICompanyRepository _companyRepo;
    private readonly IPriceRepository _prices;
    private readonly IFilingRepository _filings;
    private readonly INewsRepository _news;
    private readonly IAlphaVantageProvider _alphaVantage;
    private readonly ICompanyDirectory _directory;
    private readonly INewsProviderFactory _newsFactory;
    private readonly IEnumerable<ISocialSignalProvider> _social;
    private readonly IFinancialSentimentAnalyzer _sentiment;
    private readonly IRelevanceService _relevance;
    private readonly ILogger<MoveDetectionService> _logger;

    public MoveDetectionService(
        ICompanyRepository companyRepo,
        IPriceRepository prices,
        IFilingRepository filings,
        INewsRepository news,
        IAlphaVantageProvider alphaVantage,
        ICompanyDirectory directory,
        INewsProviderFactory newsFactory,
        IEnumerable<ISocialSignalProvider> social,
        IFinancialSentimentAnalyzer sentiment,
        IRelevanceService relevance,
        ILogger<MoveDetectionService> logger)
    {
        _companyRepo = companyRepo;
        _prices = prices;
        _filings = filings;
        _news = news;
        _alphaVantage = alphaVantage;
        _directory = directory;
        _newsFactory = newsFactory;
        _social = social;
        _sentiment = sentiment;
        _relevance = relevance;
        _logger = logger;
    }

    // topMoves override is harvest-only (reason: Step 7 of
    // hype-intelligence-plan): product callers pass null → TopMoves (5).
    public async Task<MovesWindow> GetMoves(string symbol, DateOnly asOfDate, string? newsSource = null, CancellationToken ct = default, IProgress<SnapshotProgress>? progress = null, int? topMoves = null)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new InvalidHistoricalDateException("Symbol is required.");
        HistoricalDate.Create(asOfDate);
        progress?.Report(new SnapshotProgress("detecting", "started"));

        var normalized = symbol.Trim().ToUpperInvariant();
        var selectedNewsSource = NewsSources.Normalize(newsSource ?? _newsFactory.DefaultSource);
        var window = new MovesWindow
        {
            CompanySymbol = normalized,
            DecisionDate = asOfDate,
            NewsSource = selectedNewsSource,
        };

        var rows = await ResolveWindow(normalized, asOfDate, ct);
        if (rows.Count < MinRows)
        {
            window.Summary = new WindowSummary { TradingDays = rows.Count, SufficientHistory = false };
            window.Uncertainty = DecisionContextCalculator.Calculate(window,
                Array.Empty<ScoredArticle>(), _sentiment.ModelId);
            progress?.Report(new SnapshotProgress("detecting", "complete",
                $"insufficient history ({rows.Count} days)", rows.Count));
            // Terminal evidence state on the early return (reason: UI stage
            // rows hang forever on a stage that never reports; no moves
            // means no evidence to attach — stated, not left pending).
            progress?.Report(new SnapshotProgress("evidence", "skipped",
                "no key moves — nothing to attach evidence to", 0));
            return window;
        }

        var asc = rows.OrderBy(p => p.Date).ToList();
        var slice = asc.TakeLast(WindowSize).ToList();
        window.Summary = WindowStatistics.Summarize(slice);
        window.WindowPrices = slice;
        window.Regimes = RegimeClassifier.Classify(slice);

        // Harvest override (reason: Step 7): clamped to a sane mining bound.
        var takeMoves = topMoves is > 0 ? Math.Min(topMoves.Value, 20) : TopMoves;
        var scored = MoveScorer.ScoreDays(asc, slice, takeMoves).Take(takeMoves).ToList();
        var company = await _companyRepo.GetBySymbol(normalized, ct);
        var companyName = company?.Name;

        // One social fetch per investigation (not per move): cheap enough for
        // throttled community APIs, then sliced per move by post date below.
        var socialAll = await FetchSocialWindow(normalized, companyName, scored, asOfDate, ct);

        progress?.Report(new SnapshotProgress("detecting", "complete",
            $"{scored.Count} key movements", scored.Count));

        // Stale-cache refresh before evidence: without this, the first fetch's
        // rows shadow all later coverage (DB-first hit on any rows at all).
        await RefreshStaleNews(normalized, companyName, selectedNewsSource, scored, ct);

        int done = 0;
        foreach (var move in scored)
        {
            progress?.Report(new SnapshotProgress("evidence", "started",
                $"move {move.Date:yyyy-MM-dd} ({++done} of {scored.Count})"));
            window.KeyMoves.Add(move);
            window.EvidenceByDate[move.Date.ToString("yyyy-MM-dd")] =
                await BuildEvidence(normalized, companyName, selectedNewsSource, move.Date, socialAll, ct);
        }
        if (scored.Count > 0)
            progress?.Report(new SnapshotProgress("evidence", "complete",
                $"{scored.Count} moves with evidence", scored.Count));
        else
            // Same terminal-state guarantee as above: detection can legally
            // yield zero moves, and the UI must not wait on it.
            progress?.Report(new SnapshotProgress("evidence", "skipped",
                "detection found no key moves", 0));

        // Decision Context: score the window's cached articles through local
        // FinBERT (bounded, cache-first), then run the pure engine over the
        // window + scores. Sentiment that cannot be measured stays missing.
        // (Scored first because per-move divergence below falls back to these
        // scores when providers supply none — reason: gdelt windows otherwise
        // read permanently "unknown". Reorder only; no scoring change.)
        var windowArticles = window.EvidenceByDate.Values
            .SelectMany(e => e.News)
            .GroupBy(n => n.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderByDescending(n => n.PublishedAt)
            .Take(100)
            .ToList();
        var scoredArticles = await _sentiment.EnsureScoredAsync(windowArticles, asOfDate, ct);
        window.Uncertainty = DecisionContextCalculator.Calculate(window, scoredArticles, _sentiment.ModelId);

        // Per-move divergence: provider per-entity scores first; FinBERT
        // window scores (confidence-gated, cutoff-enforced, hash-bound)
        // fill in only where providers are silent. Nothing fabricated:
        // still unknown when fewer than 2 measured scores exist.
        var finbertById = scoredArticles
            .Where(s => s.Confidence >= DecisionContextCalculator.MinConfidenceUsable)
            .GroupBy(s => s.ArticleId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (decimal)g.First().Score, StringComparer.Ordinal);
        foreach (var move in window.KeyMoves)
        {
            var key = move.Date.ToString("yyyy-MM-dd");
            var found = window.EvidenceByDate.TryGetValue(key, out var evidence);
            var scores = found && evidence is not null
                ? evidence.News.Select(n => n.SentimentScore ??
                    (finbertById.TryGetValue(n.Id, out var f) ? (decimal?)f : null))
                    .ToList()
                : new List<decimal?>();
            move.SentimentDirection = SentimentDivergence.Classify(scores, move.DailyReturnPct);
            if (found && evidence is not null)
            {
                // Mean of measured scores only (same inputs as the classifier,
                // so the magnitude dim can never contradict the direction).
                var measured = scores.Where(s => s.HasValue).Select(s => s!.Value).ToList();
                evidence.SentimentMean = measured.Count >= 2
                    ? Math.Round(measured.Average(), 4)
                    : null;
            }
        }
        return window;
    }

    private async Task<(List<SocialSignal> Signals, bool Failed)> FetchSocialWindow(
        string symbol, string? companyName, List<KeyMove> moves, DateOnly asOfDate, CancellationToken ct)
    {
        var all = new List<SocialSignal>();
        var failed = false;
        if (moves.Count == 0)
            return (all, failed);

        var from = moves.Min(m => m.Date).AddDays(-SocialLookbackDays);
        foreach (var social in _social)
        {
            try
            {
                var signals = await social.GetSignals(symbol, companyName, from, asOfDate, ct);
                all.AddRange(signals);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Move social ({Provider}) unavailable for {Symbol}",
                    social.ProviderName, symbol);
                failed = true;
            }
        }

        return (all
            .GroupBy(s => s.Id)
            .Select(g => g.First())
            .OrderByDescending(s => s.Score)
            .ToList(), failed);
    }

    // DB-first; live Alpha Vantage fetch only on a miss and only for CIK-backed
    // identities (same quota discipline as the snapshot engine).
    private async Task<IReadOnlyList<PricePoint>> ResolveWindow(string symbol, DateOnly asOfDate, CancellationToken ct)
    {
        var prices = await _prices.GetPricesAsOf(symbol, asOfDate, FetchSize, ct);
        if (prices.Count > 0)
            return prices;

        var company = await _companyRepo.GetBySymbol(symbol, ct);
        var cik = company?.Cik;
        if (string.IsNullOrEmpty(cik) && _directory.TryGetCik(symbol, out var mapped))
            cik = mapped;
        if (string.IsNullOrEmpty(cik))
            return prices;

        _logger.LogInformation("Fetching 100-day window from Alpha Vantage for {Symbol}", symbol);
        var fresh = await _alphaVantage.GetDailyPrices(symbol, asOfDate, FetchSize, ct);
        if (fresh.Count > 0)
        {
            await _prices.StorePrices(symbol, fresh, ct);
            return await _prices.GetPricesAsOf(symbol, asOfDate, FetchSize, ct);
        }

        return prices;
    }

    private async Task<MoveEvidence> BuildEvidence(
        string symbol, string? companyName, string newsSource, DateOnly moveDate,
        (List<SocialSignal> Signals, bool Failed) socialWindow, CancellationToken ct)
    {
        var evidence = new MoveEvidence();

        try
        {
            // No take: every cutoff-eligible filing is qualified evidence —
            // but movement-level eligibility is the 30-day regulatory window
            // (reg-v1, extra layer over semantic relevance, which is
            // untouched). A 2015 filing is never evidence for a 2026 move.
            var filings = await _filings.GetFilingsAsOf(symbol, moveDate, ct);
            evidence.Filings = RegulatoryEvidence
                .SelectInWindow(filings, moveDate)
                .ToList();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Move filings unavailable for {Symbol} on {Date}", symbol, moveDate);
            evidence.UnavailableLayers.Add("regulatory");
        }

        try
        {
            // DB-first (free-tier discipline): the snapshot engine's news cache
            // serves repeat windows at zero provider cost. Source-filtered
            // inside the query so another source's burst can't push this
            // source's rows out of the read window.
            var fromSource = (await _news.GetNewsAsOf(symbol, moveDate, newsSource, ct))
                .Where(n => IsFromSource(n, newsSource)).ToList();
            var fetchKey = symbol + "|" + newsSource;
            if (fromSource.Count == 0)
            {
                if (_newsFetchFailed.Contains(fetchKey))
                {
                    // Already-known outage this request: skip the fetch but
                    // still mark the layer — silent would misread as "no news".
                    evidence.UnavailableLayers.Add("news");
                }
                else
                {
                    try
                    {
                        var provider = _newsFactory.Get(newsSource);
                        // Proof-of-work (recorded before the call so even a
                        // zero-row or failed fetch leaves the attempt visible;
                        // failures additionally mark the layer unavailable).
                        evidence.NewsFetchedLive = true;
                        var fresh = await provider.SearchAsync(symbol, companyName, moveDate, ct);
                        if (fresh.Count > 0)
                        {
                            await _news.StoreNews(symbol, fresh, ct);
                            var reread = await _news.GetNewsAsOf(symbol, moveDate, newsSource, ct);
                            fromSource = reread.Where(n => IsFromSource(n, newsSource)).ToList();
                        }
                    }
                    catch (Exception)
                    {
                        // One failed fetch per request: provider-level retries
                        // already ran inside the provider; re-hammering per move
                        // only deepens throttling. Re-thrown below via the shared
                        // handler so the layer is still marked honestly.
                        _newsFetchFailed.Add(fetchKey);
                        throw;
                    }
                }
            }
            // Relevance gate (shared policy): only gated articles become
            // evidence. Empty map (AI off/failed) passes everything through.
            fromSource = await ApplyRelevanceGateAsync(
                fromSource, symbol, companyName, moveDate, ct);
            // Company-naming articles first (deterministic centrality, same
            // rows — see NewsRelevance), then most recent. Nothing hidden,
            // nothing cut: every gated article flows to evidence, sentiment,
            // hype cases, and briefs. Bounded consumers (FinBERT 100 for CPU,
            // briefs for token budget) slice explicitly at their own boundary.
            evidence.News = NewsRelevance.OrderByMention(fromSource, symbol, companyName).ToList();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Move news unavailable for {Symbol} on {Date}", symbol, moveDate);
            evidence.UnavailableLayers.Add("news");
        }

        // Social comes from the single per-investigation fetch, sliced to this
        // move's window ([moveDate-7d, moveDate] by post date). A failed fetch
        // marks every move honestly; an empty slice means no discussion found.
        if (socialWindow.Failed)
        {
            evidence.UnavailableLayers.Add("social");
        }
        else
        {
            var from = moveDate.AddDays(-SocialLookbackDays);
            // No take: the window slice is already bounded by post date, and
            // the provider response behind it is capped per community.
            evidence.Social = socialWindow.Signals
                .Where(s => DateOnly.FromDateTime(s.CreatedAt) >= from &&
                            DateOnly.FromDateTime(s.CreatedAt) <= moveDate)
                .OrderByDescending(s => s.Score)
                .ToList();
        }

        try
        {
            var after = await _prices.GetPricesAfter(symbol, moveDate, 5, ct);
            evidence.Reaction = after
                .Select(p => new MarketReaction { Date = p.Date, Close = p.Close })
                .ToList();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Move reaction unavailable for {Symbol} on {Date}", symbol, moveDate);
            evidence.UnavailableLayers.Add("reaction");
        }

        evidence.Arrival = ArrivalMap.Build(moveDate, evidence);
        return evidence;
    }

    private async Task RefreshStaleNews(
        string symbol, string? companyName, string newsSource, List<KeyMove> moves, CancellationToken ct)
    {
        if (moves.Count == 0 || !_newsRefreshed.Add(symbol + "|" + newsSource))
            return;
        // Throttle-aware: when the adaptive rhythm already knows this source
        // is throttled, a refresh would only burn minutes of backoff for rows
        // we likely cannot get. Serve the stale cache honestly instead.
        if ((newsSource == NewsSources.Gdelt || newsSource == NewsSources.MarketAux) &&
            RateLimiterRegistry.TryGet(newsSource == NewsSources.Gdelt ? "gdelt" : "marketaux")?.Recent429s > 0)
        {
            _logger.LogInformation("Skipping stale news refresh for {Symbol}: {Source} currently throttled", symbol, newsSource);
            return;
        }
        try
        {
            var latest = moves.Max(m => m.Date);
            var cached = await _news.GetNewsAsOf(symbol, latest, newsSource, ct);
            var newest = cached
                .Where(n => IsFromSource(n, newsSource))
                .Select(n => (DateTime?)n.PublishedAt)
                .Max();
            if (newest is null)
                return; // Empty: the per-move fallback already fetches live.
            if (DateOnly.FromDateTime(newest.Value) >= latest.AddDays(-NewsStaleAfterDays))
                return; // Fresh enough: provider's trailing window is covered.
            var fresh = await _newsFactory.Get(newsSource).SearchAsync(symbol, companyName, latest, ct);
            if (fresh.Count > 0)
                await _news.StoreNews(symbol, fresh, ct);
            _logger.LogInformation("Stale news refresh for {Symbol}: newest cached {Newest} vs latest move {Latest}, fetched {Count}",
                symbol, newest, latest, fresh.Count);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stale news refresh failed for {Symbol}; serving cached rows", symbol);
        }
    }

    private async Task<List<NewsArticle>> ApplyRelevanceGateAsync(
        List<NewsArticle> rows, string symbol, string? companyName,
        DateOnly moveDate, CancellationToken ct)
    {
        if (rows.Count == 0)
            return rows;
        string? sector = null;
        if (_directory.TryGet(symbol, out var info) && info is not null &&
            !string.IsNullOrWhiteSpace(info.Sector))
            sector = info.Sector;
        IReadOnlyDictionary<string, ArticleRelevance> map;
        try
        {
            map = await _relevance.ClassifyAsync(symbol, moveDate, companyName, sector, rows, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Relevance gate failed for {Symbol}; passing through", symbol);
            return rows;
        }
        if (map.Count == 0)
            return rows;
        return rows.Where(n => RelevanceService.PassesGate(
            map.TryGetValue(n.Id, out var v) ? v : null)).ToList();
    }

    // Same source-membership rule as the snapshot engine: cached rows carry
    // their origin in Source, so per-source filtering never mixes providers.
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
