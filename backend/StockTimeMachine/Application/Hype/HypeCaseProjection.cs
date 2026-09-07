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
        // with exact stage text (capped) so briefs narrate stages, not just
        // thread titles.
        const int MaxNewsItems = 25;
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
            evidence.News = ev.News.Take(MaxNewsItems).Select(n => new HypeCaseNewsItem
            {
                Title = n.Title ?? "",
                Description = Clip(n.Description),
                Source = n.Source ?? "",
                PublishedAt = n.PublishedAt,
                Url = n.Url ?? "",
            }).ToList();
            evidence.Filings = ev.Filings.Take(MaxNewsItems).Select(f => new HypeCaseFiling
            {
                FormType = f.FormType ?? "",
                FiledAt = f.FiledAt,
                Url = f.Url ?? "",
            }).ToList();
            evidence.Social = ev.Social.Take(MaxNewsItems).Select(s => new HypeCaseSocialPost
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

    private static HypeCaseThread ToThread(TopicCluster t) => new()
    {
        LabelTerms = t.LabelTerms.ToList(),
        RepresentativeTitle = t.RepresentativeTitle ?? "",
        SpanStart = t.SpanStart,
        SpanEnd = t.SpanEnd,
        ArticleIds = t.ArticleIds.ToList(),
        RelevanceRate = t.RelevanceRate,
        TopCategory = t.TopCategory ?? "",
        BriefSummary = t.Brief is null || string.IsNullOrWhiteSpace(t.Brief.Summary)
            ? null
            : t.Brief.Summary.Length > 2000 ? t.Brief.Summary.Substring(0, 2000) : t.Brief.Summary,
    };
}
