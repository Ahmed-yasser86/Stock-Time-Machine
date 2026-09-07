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
        IReadOnlyList<(string Title, string Body)> articles,
        IReadOnlyList<HypeCaseSocialPost>? signalSocial = null,
        IReadOnlyList<HypeFilingSummary>? filingSummaries = null)
    {
        var symbol = companySymbol.Trim().ToUpperInvariant();
        var sb = new StringBuilder();
        sb.AppendLine($"You are a historical research assistant. Today is {asOfDate:yyyy-MM-dd}.");
        sb.AppendLine($"You know NOTHING that happened after this date. Never use outside knowledge,");
        sb.AppendLine($"never mention events after this date, and never infer what followed.");
        sb.AppendLine();
        sb.AppendLine($"Signal under review: {match.Name} (trigger: {SignalTrigger(match)}).");
        sb.AppendLine($"Peak: {detail.PeakDate:yyyy-MM-dd}, daily return {detail.DailyReturnPct}%, detection score {detail.Score:F3}, flags [{string.Join(", ", detail.Flags)}], sentiment {detail.SentimentDirection}.");
        sb.AppendLine();
        sb.AppendLine("CASE FACTS — exact investigation stages for this peak. Reproduce them exactly;");
        sb.AppendLine("never reinterpret, inflate, or fill gaps in them:");
        AppendArrival(sb, detail);
        AppendReaction(sb, detail);
        AppendFilings(sb, detail);
        AppendSocial(sb, detail, signalSocial);
        AppendRegulatoryContext(sb, filingSummaries);
        var regimePath = detail.RegimePath.OrderBy(kv => kv.Key).ToList();
        if (regimePath.Count > 0)
            sb.AppendLine($"- Regime path (pre-peak): {string.Join(", ", regimePath.Select(kv => $"{kv.Key}={kv.Value})"))}.");
        sb.AppendLine();
        if (articles.Count == 0)
            sb.AppendLine("EVIDENCE ARTICLES [0] — no article passed the signal-relevance filter. Narrate the CASE FACTS only.");
        else
            sb.AppendLine($"EVIDENCE ARTICLES [{articles.Count}] — contemporary coverage behind the trigger. Summarize what THEY report.");
        sb.AppendLine();
        for (int i = 0; i < articles.Count; i++)
        {
            sb.AppendLine($"[{i + 1}] {articles[i].Title}");
            if (!string.IsNullOrWhiteSpace(articles[i].Body))
                sb.AppendLine(articles[i].Body);
            sb.AppendLine();
        }
        sb.AppendLine("Hard rules:");
        sb.AppendLine("- State only what is in the CASE FACTS or the articles; cite article claims like [1], [2]; mark stage facts as (case fact).");
        sb.AppendLine("- NEVER state or imply the signal or the coverage caused the price move.");
        sb.AppendLine("- NEVER predict, advise, or recommend anything.");
        sb.AppendLine("- One article alone is never consensus: say 'one article reports...' when unsourced elsewhere.");
        sb.AppendLine("- Empty stages (no news, no filings, silent layers) are facts: report them as absent evidence, never invent content for them.");
        sb.AppendLine();
        sb.AppendLine("Respond with exactly these sections:");
        sb.AppendLine("SUMMARY: one paragraph, max 150 words: what peaked, what the stages show, what the coverage reports.");
        sb.AppendLine("KEY POINTS: up to 6 bullets, each cited [n] or marked (case fact).");
        sb.AppendLine("DISAGREEMENTS AND GAPS: what is contested or missing; 'none visible' if uniform.");
        return sb.ToString();
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
