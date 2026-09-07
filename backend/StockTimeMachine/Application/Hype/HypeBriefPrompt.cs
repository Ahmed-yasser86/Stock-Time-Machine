using System.Globalization;
using System.Text;

namespace StockTimeMachine;

// The single prompt behind every hype signal brief. Same containment family
// as ClusterBriefPrompt (cutoff roleplay, extractive discipline, causation /
// prediction / advice bans), extended with the investigation's EXACT stage
// text: the brief narrates the full peak dossier (statistics, arrival,
// reaction, filings, news, social), not thread titles alone.
// Inputs are pre-capped by callers; this builder only shapes them.
public static class HypeBriefPrompt
{
    public static string Build(
        string companySymbol,
        DateOnly asOfDate,
        HypeSignalMatch match,
        HypeCaseDetail detail,
        IReadOnlyList<HypeBriefArticle> articles,
        IReadOnlyList<HypeCaseSocialPost>? signalSocial = null,
        IReadOnlyList<HypeFilingSummary>? filingSummaries = null)
    {
        var hasRegulatory = filingSummaries is not null &&
            filingSummaries.Any(f => !string.IsNullOrWhiteSpace(f.Findings));
        var symbol = companySymbol.Trim().ToUpperInvariant();
        var peak = detail.PeakDate;
        var sb = new StringBuilder();
        sb.AppendLine($"You are a historical research assistant. Today is {asOfDate:yyyy-MM-dd}.");
        sb.AppendLine($"You know NOTHING that happened after this date. Never use outside knowledge,");
        sb.AppendLine($"never mention events after this date, and never infer what followed.");
        sb.AppendLine();
        // Retrospective frame (never hindsight-confused): the event has a
        // date, the investigation has a later cutoff, and evidence belongs
        // to exactly one side of the event date.
        sb.AppendLine($"Detected event date: {peak:yyyy-MM-dd}.");
        sb.AppendLine($"Investigation cutoff: {asOfDate:yyyy-MM-dd} (this investigation was run as of this date).");
        sb.AppendLine($"Evidence window: everything below was published on or before the cutoff.");
        sb.AppendLine($"Signal under review: {match.Name} (trigger: {SignalTrigger(match)}).");
        sb.AppendLine($"Peak: {peak:yyyy-MM-dd}, daily return {detail.DailyReturnPct}%, detection score {detail.Score:F3}, flags [{string.Join(", ", detail.Flags)}], sentiment {detail.SentimentDirection}.");
        sb.AppendLine();
        sb.AppendLine("CASE FACTS — exact investigation stages for this peak. Reproduce them exactly;");
        sb.AppendLine("never reinterpret, inflate, or fill gaps in them. These are pipeline facts: mark them (case fact).");
        AppendArrival(sb, detail);
        AppendNewsTiming(sb, detail, peak);
        AppendReaction(sb, detail);
        AppendFilings(sb, detail);
        AppendSocial(sb, detail, signalSocial);
        AppendRegulatoryContext(sb, filingSummaries);
        var regimePath = detail.RegimePath.OrderBy(kv => kv.Key).ToList();
        if (regimePath.Count > 0)
            sb.AppendLine($"- Regime path (pre-peak): {string.Join(", ", regimePath.Select(kv => $"{kv.Key}={kv.Value})"))}.");
        sb.AppendLine();
        AppendArticleGroups(sb, articles, peak);
        sb.AppendLine("Hard rules:");
        sb.AppendLine("- State only what is in the CASE FACTS or the articles; cite article claims like [1], [2]; mark pipeline stage facts as (case fact) and keep them distinct from externally sourced news claims.");
        sb.AppendLine("- Date every claim by its article's publication date as printed above. NEVER describe an article published after the event date as arriving on, known on, or available at the event date.");
        sb.AppendLine("- Arrival counts use the NEWS TIMING breakdown above (per-date published counts), never the FirstSeen date for the whole set: 'N articles published June 4, M published June 5' — not 'N+M articles arriving June 5'.");
        sb.AppendLine("- NEVER use the blanket phrase 'contemporary coverage' for the whole set: name each group (at-event vs post-peak) explicitly.");
        sb.AppendLine("- NEVER state or imply that post-peak coverage caused the price move. Post-peak items are subsequent developments: they may corroborate or contextualize, never explain away the event.");
        sb.AppendLine("- NEVER predict, advise, or recommend anything.");
        sb.AppendLine("- Invitations, announcements, and reactions are separate dated facts: when one article reports an invitation and a later article reports the response, date the response by the LATER article — never backdate it to the invitation.");
        sb.AppendLine("- When sources disagree on a specification number, report each figure with its source attribution instead of silently picking one.");
        sb.AppendLine("- Every specification number, dollar figure, or percentage in the output must name its reporting outlet inline (e.g. 'techradar reports 784GB'); never state a bare number as established fact.");
        sb.AppendLine("- One article alone is never consensus: say 'one article reports...' when unsourced elsewhere.");
        sb.AppendLine("- Empty stages (no news, no filings, silent layers) are facts: report them as absent evidence, never invent content for them.");
        sb.AppendLine();
        sb.AppendLine("Respond with exactly these sections (all three are mandatory —");
        sb.AppendLine("omitting any section is a failure, even when content is thin):");
        sb.AppendLine("SUMMARY: one paragraph, max 150 words: state the event date and cutoff first, then what the stages show, then at-event coverage, then post-peak developments — in that order.");
        sb.AppendLine("KEY POINTS: up to 6 bullets, each cited [n] or marked (case fact), each carrying its date.");
        if (hasRegulatory)
        {
            sb.AppendLine("Bullet 1 MUST summarize the REGULATORY CONTEXT findings above (or state");
            sb.AppendLine("plainly that the filings carried no usable content) — never skip it.");
        }
        sb.AppendLine("DISAGREEMENTS AND GAPS: what is contested or missing; write the literal");
        sb.AppendLine("sentence 'none visible' if uniform — do not drop this section.");
        return sb.ToString();
    }

    // Per-date evidence census: the arrival line's FirstSeen/count must never
    // mix dates, so the exact per-day breakdown rides alongside it.
    private static void AppendNewsTiming(StringBuilder sb, HypeCaseDetail detail, DateOnly peak)
    {
        var dated = detail.Evidence.News
            .GroupBy(n => DateOnly.FromDateTime(n.PublishedAt))
            .OrderBy(g => g.Key)
            .ToList();
        if (dated.Count == 0)
            return;
        var parts = dated.Select(g =>
            $"{g.Count()} published {g.Key:yyyy-MM-dd}" +
            (g.Key <= peak ? " (at-event)" : " (post-peak)"));
        sb.AppendLine($"- News timing (case fact): {string.Join("; ", parts)}.");
    }

    // Article groups split by the event date. Undated items (legacy rows)
    // show their thread span so the model sees a range, never an invented
    // date. Global [n] numbering preserved across groups for citations.
    private static void AppendArticleGroups(
        StringBuilder sb, IReadOnlyList<HypeBriefArticle> articles, DateOnly peak)
    {
        var dated = articles
            .Select((a, i) => (Article: a, Index: i))
            .Where(x => x.Article.PublishedAt.HasValue)
            .ToList();
        var atEvent = dated.Where(x => x.Article.PublishedAt!.Value <= peak).ToList();
        var postPeak = dated.Where(x => x.Article.PublishedAt!.Value > peak).ToList();
        var undated = articles
            .Select((a, i) => (Article: a, Index: i))
            .Where(x => !x.Article.PublishedAt.HasValue)
            .ToList();
        if (articles.Count == 0)
        {
            sb.AppendLine("EVIDENCE ARTICLES [0] — no article passed the signal-relevance filter. Narrate the CASE FACTS only.");
            sb.AppendLine();
            return;
        }
        var n = 0;
        void Block(string heading, IEnumerable<(HypeBriefArticle Article, int Index)> items,
            Func<HypeBriefArticle, string> dateline)
        {
            var list = items.ToList();
            if (list.Count == 0)
                return;
            sb.AppendLine(heading);
            foreach (var (article, _) in list)
            {
                n++;
                sb.AppendLine($"[{n}] {dateline(article)} {article.Title}");
                if (!string.IsNullOrWhiteSpace(article.Body))
                    sb.AppendLine(article.Body);
                sb.AppendLine();
            }
        }
        Block($"AT-EVENT ARTICLES [{atEvent.Count}] — published on or before {peak:yyyy-MM-dd}, knowable around the event:",
            atEvent, a => $"({a.PublishedAt!.Value:yyyy-MM-dd})");
        Block($"POST-PEAK ARTICLES [{postPeak.Count}] — published after the event (subsequent context only, never event-day knowledge, never causes):",
            postPeak, a => $"({a.PublishedAt!.Value:yyyy-MM-dd})");
        Block("UNDATED ARTICLES — thread-span range shown, do not assign these to either side:",
            undated, a => string.IsNullOrWhiteSpace(a.SpanLabel) ? "(date unknown)" : $"({a.SpanLabel})");
    }

    private static string SignalTrigger(HypeSignalMatch match)
    {
        var def = HypeSignalCatalog.ById(match.SignalId);
        return def?.Trigger ?? match.SignalId;
    }

    private static void AppendArrival(StringBuilder sb, HypeCaseDetail detail)
    {
        var arrival = detail.Evidence.Arrival;
        if (arrival.Count == 0)
        {
            sb.AppendLine("- Arrival: not recorded for this peak (case fact).");
            return;
        }
        foreach (var a in arrival)
        {
            var seen = a.FirstSeen.HasValue ? a.FirstSeen.Value.ToString("o") : "unknown";
            var lag = a.LagHours.HasValue ? a.LagHours.Value.ToString("F1", CultureInfo.InvariantCulture) + "h" : "n/a";
            sb.AppendLine($"- Arrival {a.Layer}: {a.State}, first seen {seen}, lag {lag}. {a.Detail}".TrimEnd());
        }
    }

    private static void AppendReaction(StringBuilder sb, HypeCaseDetail detail)
    {
        if (detail.Reaction.Count == 0)
        {
            sb.AppendLine("- Market reaction: no recorded closes after this peak (case fact).");
            return;
        }
        sb.AppendLine($"- Market reaction (recorded closes only): {string.Join("; ", detail.Reaction.Select(r => $"{r.Date:yyyy-MM-dd} {r.Close}"))} (case fact).");
    }

    private static void AppendFilings(StringBuilder sb, HypeCaseDetail detail)
    {
        var filings = detail.Evidence.Filings;
        if (filings.Count == 0)
        {
            sb.AppendLine("- Regulatory: no filings in this peak's evidence (case fact).");
            return;
        }
        sb.AppendLine($"- Regulatory filings ({filings.Count}): {string.Join("; ", filings.Select(f => $"{f.FormType} filed {f.FiledAt:yyyy-MM-dd}"))} (case fact).");
    }

    // Dedicated regulatory section: filing-document summaries live here,
    // clearly separated from general narrative — never mixed in.
    private static void AppendRegulatoryContext(
        StringBuilder sb, IReadOnlyList<HypeFilingSummary>? filingSummaries)
    {
        if (filingSummaries is null || filingSummaries.Count == 0)
        {
            sb.AppendLine("REGULATORY CONTEXT: no filing summaries available for this peak (case fact).");
            sb.AppendLine();
            return;
        }
        sb.AppendLine("REGULATORY CONTEXT (from filing documents — separate from news narrative):");
        foreach (var f in filingSummaries)
        {
            sb.AppendLine($"- {f.FormType} filed {f.FiledAt:yyyy-MM-dd}: {f.Findings} Disclosures/risks: {f.Disclosures} [content: {f.ConfidenceNote}, {f.PagesProcessed}/{f.TotalPages} pages]".TrimEnd());
        }
        sb.AppendLine();
    }

    private static void AppendSocial(StringBuilder sb, HypeCaseDetail detail,
        IReadOnlyList<HypeCaseSocialPost>? signalSocial = null)
    {
        // Only signal-relevant posts narrate (pre-filtered by the caller);
        // noise never reaches this prompt.
        var posts = signalSocial ?? detail.Evidence.Social;
        if (posts.Count == 0)
        {
            sb.AppendLine("- Social: no posts in this peak's evidence (case fact).");
            return;
        }
        foreach (var p in posts.Take(5))
            sb.AppendLine($"- Social [{p.Community}, {p.CreatedAt:yyyy-MM-dd}]: {p.Title} — {p.Excerpt}".TrimEnd());
    }
}
