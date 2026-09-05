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
    private const int Rolling = 20;
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
    private readonly IHistoricalDataRepository _dataRepo;
    private readonly IAlphaVantageProvider _alphaVantage;
    private readonly ICompanyDirectory _directory;
    private readonly INewsProviderFactory _newsFactory;
    private readonly IEnumerable<ISocialSignalProvider> _social;
    private readonly ILogger<MoveDetectionService> _logger;

    public MoveDetectionService(
        ICompanyRepository companyRepo,
        IHistoricalDataRepository dataRepo,
        IAlphaVantageProvider alphaVantage,
        ICompanyDirectory directory,
        INewsProviderFactory newsFactory,
        IEnumerable<ISocialSignalProvider> social,
        ILogger<MoveDetectionService> logger)
    {
        _companyRepo = companyRepo;
        _dataRepo = dataRepo;
        _alphaVantage = alphaVantage;
        _directory = directory;
        _newsFactory = newsFactory;
        _social = social;
        _logger = logger;
    }

    public async Task<MovesWindow> GetMoves(string symbol, DateOnly asOfDate, string? newsSource = null, CancellationToken ct = default, IProgress<SnapshotProgress>? progress = null)
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
            window.Uncertainty = UncertaintyIndexCalculator.Calculate(window);
            progress?.Report(new SnapshotProgress("detecting", "complete",
                $"insufficient history ({rows.Count} days)", rows.Count));
            return window;
        }

        var asc = rows.OrderBy(p => p.Date).ToList();
        var slice = asc.TakeLast(WindowSize).ToList();
        window.Summary = Summarize(slice);
        window.WindowPrices = slice;
        window.Regimes = RegimeClassifier.Classify(slice);

        var scored = ScoreDays(asc, slice).Take(TopMoves).ToList();
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

        foreach (var move in window.KeyMoves)
        {
            var key = move.Date.ToString("yyyy-MM-dd");
            var scores = window.EvidenceByDate.TryGetValue(key, out var evidence)
                ? evidence.News.Select(n => n.SentimentScore)
                : Enumerable.Empty<decimal?>();
            move.SentimentDirection = SentimentDivergence.Classify(scores, move.DailyReturnPct);
        }

        window.Uncertainty = UncertaintyIndexCalculator.Calculate(window);
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
        var prices = await _dataRepo.GetPricesAsOf(symbol, asOfDate, FetchSize, ct);
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
            await _dataRepo.StorePrices(symbol, fresh, ct);
            return await _dataRepo.GetPricesAsOf(symbol, asOfDate, FetchSize, ct);
        }

        return prices;
    }

    private static WindowSummary Summarize(List<PricePoint> slice)
    {
        var rets = Returns(slice);
        var first = slice.First().Close;
        var last = slice.Last().Close;
        decimal peak = first, maxDd = 0;
        foreach (var p in slice)
        {
            if (p.Close > peak) peak = p.Close;
            var dd = peak == 0 ? 0 : (p.Close - peak) / peak * 100;
            if (dd < maxDd) maxDd = dd;
        }
        var best = rets.Count > 0 ? rets.MaxBy(r => r.Ret) : null;
        var worst = rets.Count > 0 ? rets.MinBy(r => r.Ret) : null;
        var mean = rets.Count > 0 ? rets.Average(r => r.Ret) : 0;
        var variance = rets.Count > 1 ? rets.Sum(r => (r.Ret - mean) * (r.Ret - mean)) / (rets.Count - 1) : 0;

        return new WindowSummary
        {
            TradingDays = slice.Count,
            CumulativeReturnPct = first == 0 ? 0 : Math.Round((last - first) / first * 100, 2),
            Volatility = Math.Round(Math.Sqrt(variance) * Math.Sqrt(252) * 100, 2),
            MaxDrawdownPct = Math.Round(maxDd, 2),
            BestDay = best?.Date,
            BestDayReturnPct = best is null ? 0 : Math.Round((decimal)best.Ret * 100, 2),
            WorstDay = worst is null ? null : worst.Date,
            WorstDayReturnPct = worst is null ? 0 : Math.Round((decimal)worst.Ret * 100, 2),
            SufficientHistory = true,
        };
    }

    private sealed record DayRet(DateOnly Date, double Ret);

    private static List<DayRet> Returns(List<PricePoint> asc)
    {
        var list = new List<DayRet>(asc.Count);
        for (int i = 1; i < asc.Count; i++)
        {
            var prev = (double)asc[i - 1].Close;
            list.Add(new DayRet(asc[i].Date, prev == 0 ? 0 : ((double)asc[i].Close - prev) / prev));
        }
        return list;
    }

    private static List<KeyMove> ScoreDays(List<PricePoint> asc, List<PricePoint> slice)
    {
        var indexOf = new Dictionary<DateOnly, int>();
        for (int i = 0; i < asc.Count; i++) indexOf[asc[i].Date] = i;

        var scored = new List<(KeyMove Move, double Score, double AbsRet)>();
        foreach (var p in slice)
        {
            var i = indexOf[p.Date];
            // Baselines use the 20 days strictly before i (closes [i-21, i-1]
            // for returns, rows [i-20, i-1] for volume/range), hence i >= 21.
            if (i < Rolling + 1)
                continue;

            // The evaluated observation: day i's own return (never part of its baseline).
            var prevClose = (double)asc[i - 1].Close;
            var ret = prevClose == 0 ? 0 : ((double)p.Close - prevClose) / prevClose;

            var rets = new double[Rolling];
            for (int k = 0; k < Rolling; k++)
            {
                int j = i - Rolling + k;
                var pc = (double)asc[j - 1].Close;
                rets[k] = pc == 0 ? 0 : ((double)asc[j].Close - pc) / pc;
            }
            var mean = RollingStats.Average(rets);
            // Volatility floor: after perfectly flat windows any move is extreme.
            // Documented constant; deterministic given the same rows.
            var std = Math.Max(RollingStats.SampleStdDev(rets, mean), 0.0005);
            var z = (ret - mean) / std;

            var vols = new double[Rolling];
            for (int k = 0; k < Rolling; k++) vols[k] = asc[i - Rolling + k].Volume;
            var med = RollingStats.Median(vols);
            var volRatio = med <= 0 ? 1 : (double)p.Volume / med;

            double mom5 = 0;
            if (i >= 5)
            {
                var baseClose = (double)asc[i - 5].Close;
                mom5 = baseClose == 0 ? 0 : ((double)p.Close - baseClose) / baseClose;
            }

            double hi = double.MinValue, lo = double.MaxValue;
            for (int k = 0; k < Rolling; k++)
            {
                hi = Math.Max(hi, (double)asc[i - Rolling + k].High);
                lo = Math.Min(lo, (double)asc[i - Rolling + k].Low);
            }
            var close = (double)p.Close;
            double rangeBreak = close > hi && hi > 0 ? (close - hi) / hi
                : close < lo && lo > 0 ? (lo - close) / lo : 0;

            var score = 0.5 * Math.Min(Math.Abs(z) / 3, 1)
                + 0.3 * Math.Min(Math.Max(volRatio - 1, 0) / 4, 1)
                + 0.2 * Math.Min(rangeBreak * 20, 1);
            if (score <= 0)
                continue;

            var flags = new List<string>();
            if (z > 2) flags.Add(MoveFlags.Spike);
            if (z < -2) flags.Add(MoveFlags.Plunge);
            if (volRatio > 2.5) flags.Add(MoveFlags.HighVolume);
            if (close > hi) flags.Add(MoveFlags.Breakout);
            if (close < lo) flags.Add(MoveFlags.Breakdown);

            scored.Add((new KeyMove
            {
                Date = p.Date,
                Close = p.Close,
                DailyReturnPct = Math.Round((decimal)ret * 100, 2),
                ZScore = Math.Round(z, 2),
                VolumeRatio = Math.Round(volRatio, 2),
                FiveDayMomentumPct = Math.Round((decimal)mom5 * 100, 2),
                Score = Math.Round(score, 4),
                Flags = flags,
            }, score, Math.Abs(ret)));
        }

        return scored
            .OrderByDescending(s => s.Score)
            .ThenByDescending(s => s.Move.Date)
            .ThenByDescending(s => s.AbsRet)
            .Take(TopMoves)
            .Select(s => s.Move)
            .ToList();
    }

    // Rolling statistics live in Application/Moves/RollingStats (shared).

    private async Task<MoveEvidence> BuildEvidence(
        string symbol, string? companyName, string newsSource, DateOnly moveDate,
        (List<SocialSignal> Signals, bool Failed) socialWindow, CancellationToken ct)
    {
        var evidence = new MoveEvidence();

        try
        {
            var filings = await _dataRepo.GetFilingsAsOf(symbol, moveDate, ct);
            evidence.Filings = filings.Take(5).ToList();
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
            var fromSource = (await _dataRepo.GetNewsAsOf(symbol, moveDate, newsSource, ct))
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
                        var fresh = await provider.SearchAsync(symbol, companyName, moveDate, ct);
                        if (fresh.Count > 0)
                        {
                            await _dataRepo.StoreNews(symbol, fresh, ct);
                            var reread = await _dataRepo.GetNewsAsOf(symbol, moveDate, newsSource, ct);
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
            evidence.News = fromSource.Take(5).ToList();
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
            evidence.Social = socialWindow.Signals
                .Where(s => DateOnly.FromDateTime(s.CreatedAt) >= from &&
                            DateOnly.FromDateTime(s.CreatedAt) <= moveDate)
                .OrderByDescending(s => s.Score)
                .Take(3)
                .ToList();
        }

        try
        {
            var after = await _dataRepo.GetPricesAfter(symbol, moveDate, 5, ct);
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
        try
        {
            var latest = moves.Max(m => m.Date);
            var cached = await _dataRepo.GetNewsAsOf(symbol, latest, newsSource, ct);
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
                await _dataRepo.StoreNews(symbol, fresh, ct);
            _logger.LogInformation("Stale news refresh for {Symbol}: newest cached {Newest} vs latest move {Latest}, fetched {Count}",
                symbol, newest, latest, fresh.Count);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stale news refresh failed for {Symbol}; serving cached rows", symbol);
        }
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
