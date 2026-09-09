using System.Text.Json;

namespace StockTimeMachine;

// Read-only projection: MovesWindow + one KeyMove + narrative threads →
// HypeCase. Pure and deterministic (same inputs → same case). Partial inputs
// never throw: gaps are recorded in CompletenessByArea + overall Completeness
// so downstream signal triggers and the UI can see exactly what is missing.
// Only null window/move are programmer errors (ArgumentNullException).
public static class HypeCaseProjection
{
    // Pre-peak narrative window in calendar days (news carries timestamps).
    public const int PrePeakDays = 20;

    // Projection code version (Issue 4): stamped on every built detail so
    // live recomputation can be compared against frozen registry rows.
    public const string ProjectionVersion = "hcp-v1";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static HypeCase Build(MovesWindow window, KeyMove move, NarrativeTopicsResult? topics)
    {
        if (window is null)
            throw new ArgumentNullException(nameof(window));
        if (move is null)
            throw new ArgumentNullException(nameof(move));

        var symbol = window.CompanySymbol;
        var peak = move.Date;
        var from = peak.AddDays(-PrePeakDays);
        var completeness = new Dictionary<string, string>();

        // Threads: window overlap keeps only pre-peak narrative; null spans
        // cannot be placed, so they ride along flagged partial (excluded from
        // span-sensitive triggers by the evaluator, never silently dropped).
        var threads = new List<HypeCaseThread>();
        if (topics is null || topics.Topics.Count == 0)
        {
            completeness["threads"] = HypeCompleteness.Missing;
        }
        else
        {
            var unknownSpan = false;
            foreach (var t in topics.Topics)
            {
                var start = t.SpanStart.HasValue ? DateOnly.FromDateTime(t.SpanStart.Value) : (DateOnly?)null;
                var end = t.SpanEnd.HasValue ? DateOnly.FromDateTime(t.SpanEnd.Value) : (DateOnly?)null;
                if (start is null || end is null)
                {
                    unknownSpan = true;
                    threads.Add(ToThread(t));
                }
                else if (end >= from && start <= peak)
                {
                    threads.Add(ToThread(t));
                }
            }
            completeness["threads"] = threads.Count == 0
                ? HypeCompleteness.Missing
                : unknownSpan ? HypeCompleteness.Partial : HypeCompleteness.Full;
        }

        // Regimes: path inside the pre-peak window only.
        var regimePath = new Dictionary<string, string>();
        if (window.Regimes.Count == 0)
        {
            completeness["regimes"] = HypeCompleteness.Missing;
        }
        else
        {
            foreach (var (day, label) in window.Regimes)
            {
                if (DateOnly.TryParse(day, out var d) && d >= from && d <= peak)
                    regimePath[day] = label;
            }
            completeness["regimes"] = regimePath.Count == 0
                ? HypeCompleteness.Missing
                : HypeCompleteness.Full;
        }

        // Evidence: the move's own cutoff-filtered evidence (never re-read),
        // with exact stage text so briefs narrate stages, not just thread
        // titles. Item counts are uncapped (reason: every qualified item
        // flows downstream); only per-field TEXT is clipped, because CaseJson
        // size — not item count — is the real storage bound.
        const int MaxTextChars = 500;
        static string Clip(string? s) =>
            string.IsNullOrWhiteSpace(s) ? "" :
            s.Length > MaxTextChars ? s.Substring(0, MaxTextChars) : s;
        var evidence = new HypeCaseEvidence();
        if (window.EvidenceByDate.TryGetValue(peak.ToString("yyyy-MM-dd"), out var ev) && ev is not null)
        {
            evidence.NewsCount = ev.News.Count;
            evidence.FilingCount = ev.Filings.Count;
            evidence.SocialCount = ev.Social.Count;
            evidence.NewsTitles = ev.News.Select(n => n.Title ?? "").Where(t => t.Length > 0).ToList();
            evidence.UnavailableLayers = ev.UnavailableLayers.ToList();
            evidence.News = ev.News.Select(n => new HypeCaseNewsItem
            {
                Title = n.Title ?? "",
                Description = Clip(n.Description),
                Source = n.Source ?? "",
                PublishedAt = n.PublishedAt,
                Url = n.Url ?? "",
            }).ToList();
            evidence.Filings = ev.Filings.Select(f => new HypeCaseFiling
            {
                AccessionNumber = f.AccessionNumber ?? "",
                FormType = f.FormType ?? "",
                FiledAt = f.FiledAt,
                Url = f.Url ?? "",
            }).ToList();
            evidence.Social = ev.Social.Select(s => new HypeCaseSocialPost
            {
                Title = s.Title ?? "",
                Excerpt = Clip(s.Excerpt),
                Community = s.Community ?? "",
                CreatedAt = s.CreatedAt,
                Url = s.Url ?? "",
            }).ToList();
            evidence.Arrival = ev.Arrival.Select(a => new HypeCaseArrival
            {
                Layer = a.Layer ?? "",
                FirstSeen = a.FirstSeen,
                State = a.State ?? "",
                LagHours = a.LagHours,
                Detail = Clip(a.Detail),
            }).ToList();
            completeness["evidence"] = evidence.NewsCount + evidence.FilingCount + evidence.SocialCount == 0
                ? HypeCompleteness.Partial
                : HypeCompleteness.Full;
        }
        else
        {
            completeness["evidence"] = HypeCompleteness.Missing;
        }

        // Sentiment + reaction: named states, never zero-filled.
        completeness["sentiment"] = move.SentimentDirection == SentimentDivergence.Unknown
            ? HypeCompleteness.Missing
            : HypeCompleteness.Full;
        var reaction = ev?.Reaction
            .Select(r => new HypeCaseReaction { Date = r.Date, Close = r.Close })
            .ToList() ?? new List<HypeCaseReaction>();
        completeness["reaction"] = reaction.Count >= 3
            ? HypeCompleteness.Full
            : reaction.Count > 0 ? HypeCompleteness.Partial : HypeCompleteness.Missing;

        var overall = completeness.Values.All(v => v == HypeCompleteness.Full)
            ? HypeCompleteness.Full
            : completeness.Values.All(v => v == HypeCompleteness.Missing)
                ? HypeCompleteness.Missing
                : HypeCompleteness.Partial;

        var flags = move.Flags.ToList();
        var detail = new HypeCaseDetail
        {
            CompanySymbol = symbol,
            PeakDate = peak,
            Score = move.Score,
            DailyReturnPct = move.DailyReturnPct,
            Flags = flags,
            SentimentDirection = move.SentimentDirection,
            SentimentMean = ev?.SentimentMean is decimal mean ? (double)mean : null,
            PrePeakThreads = threads,
            RegimePath = regimePath,
            Evidence = evidence,
            Reaction = reaction,
            UncertaintyScore = window.Uncertainty?.Score,
            UncertaintyConfidence = window.Uncertainty?.Confidence,
            CompletenessByArea = completeness,
            RegulatoryLookbackDays = RegulatoryEvidence.LookbackDays,
            RegulatoryMethodology = RegulatoryEvidence.MethodologyVersion,
            RegulatoryTiers = RegulatoryEvidence.CountByTier(
                evidence.Filings.Select(f => f.FiledAt), peak) switch
            {
                var t => new Dictionary<string, int>
                {
                    [RegulatoryEvidence.Tiers.VeryClose] = t.VeryClose,
                    [RegulatoryEvidence.Tiers.Recent] = t.Recent,
                    [RegulatoryEvidence.Tiers.Older] = t.Older,
                },
            },
            ProjectionVersion = ProjectionVersion,
            // Decision-date anchored (not wall-clock): the projection must
            // stay byte-deterministic for identical inputs, so "computed at"
            // means the investigation date it was computed for.
            ComputedAtUtc = window.DecisionDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        };

        return new HypeCase
        {
            Id = symbol + ":" + peak.ToString("yyyy-MM-dd"),
            CompanySymbol = symbol,
            PeakDate = peak,
            DecisionDate = window.DecisionDate,
            NewsSource = window.NewsSource,
            Score = move.Score,
            DailyReturnPct = move.DailyReturnPct,
            FlagsCsv = string.Join(";", flags),
            SentimentDirection = move.SentimentDirection,
            Completeness = overall,
            CaseJson = JsonSerializer.Serialize(detail, Json),
            CreatedAtUtc = DateTime.UtcNow,
        };
    }

    // Frozen-vs-live comparison (Issue 4, pure): the live recomputation
    // matches the frozen row only when both were built by the same
    // projection version with the same thread count. Anything else —
    // including legacy rows with no version — reads as diverged, and the
    // caller labels it instead of silently presenting recomputed data as
    // the frozen record.
    public static bool MatchesFrozen(HypeCaseDetail? frozen, HypeCaseDetail? live) =>
        frozen is not null && live is not null &&
        frozen.ProjectionVersion == ProjectionVersion &&
        live.ProjectionVersion == ProjectionVersion &&
        frozen.PrePeakThreads.Count == live.PrePeakThreads.Count;

    // Registry case id format "SYMBOL:yyyy-MM-dd" (same construction as
    // Build). Parsed for the frozen-row brief path; null when malformed.
    public static (string Symbol, DateOnly PeakDate)? TryParseCaseId(string? caseId)
    {
        if (string.IsNullOrWhiteSpace(caseId))
            return null;
        var parts = caseId.Split(':');
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) ||
            !DateOnly.TryParse(parts[1], out var peak))
            return null;
        return (parts[0].Trim().ToUpperInvariant(), peak);
    }

    // Trigger/vote qualification (retrospective dating rule): a stored thread
    // votes in signal triggers and the vector category histogram only when at
    // least one of its articles is dated on/before the peak. Threads without
    // any known dates (legacy rows, unknown spans) keep the legacy span rule
    // — absence of dates must never read as absence of pre-peak coverage.
    public static IReadOnlyList<HypeCaseThread> QualifiedThreads(HypeCaseDetail detail)
    {
        if (detail is null)
            return Array.Empty<HypeCaseThread>();
        return detail.PrePeakThreads
            .Where(t => t is not null && (
                t.ArticleDates.Count == 0 ||
                t.ArticleDates.Values.Any(d => d <= detail.PeakDate)))
            .ToList();
    }

    // Article ids behind qualified threads: the single source for content
    // mean-pooling (indexer) and query construction (resemblance, stats).
    // Both sides must pool the same set or cosine compares different things.
    // Post-peak quarantine: members dated after the peak stay visible in the
    // thread display but stop participating here. Only positively post-peak
    // ids are excluded — undated members and legacy dateless threads ride
    // along (absence of dates must never read as absence of coverage).
    public static IReadOnlyList<string> QualifiedArticleIds(HypeCaseDetail detail)
    {
        if (detail is null)
            return Array.Empty<string>();
        return QualifiedThreads(detail)
            .SelectMany(t => t.ArticleIds.Where(id =>
                !t.ArticleDates.TryGetValue(id, out var d) || d <= detail.PeakDate))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static HypeCaseThread ToThread(TopicCluster t) => new()
    {
        LabelTerms = t.LabelTerms.ToList(),
        RepresentativeTitle = t.RepresentativeTitle ?? "",
        SpanStart = t.SpanStart,
        SpanEnd = t.SpanEnd,
        ArticleIds = t.ArticleIds.ToList(),
        ArticleDates = new Dictionary<string, DateOnly>(t.ArticleDates ?? new Dictionary<string, DateOnly>()),
        RelevanceRate = t.RelevanceRate,
        TopCategory = t.TopCategory ?? "",
        CategoryBasis = t.CategoryBasis ?? "",
        CategoryRationale = t.CategoryRationale ?? "",
        BriefSummary = t.Brief is null || string.IsNullOrWhiteSpace(t.Brief.Summary)
            ? null
            : t.Brief.Summary.Length > 2000 ? t.Brief.Summary.Substring(0, 2000) : t.Brief.Summary,
    };
}
