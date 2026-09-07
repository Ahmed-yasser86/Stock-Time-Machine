using System.Text.RegularExpressions;

namespace StockTimeMachine;

// Pure filing-text utilities: HTML stripping, page estimation, ITEM section
// splitting, and signal-relevance scoring for section retrieval / extractive
// fallback. No I/O, no model calls — fully unit-testable. Mirrors the
// deterministic spirit of MaterialityRules.
public static class HypeFilingText
{
    // Rough page: ~3000 characters of extracted text.
    public const int CharsPerPage = 3000;
    // Full-text budget for the brief pipeline input per filing.
    public const int FullTextChars = 40000;
    // Under 15 pages → full text; 15–100 → section retrieval; over 100 →
    // extractive first.
    public const int FullTextPages = 15;
    public const int SectionPages = 100;

    public static int EstimatePages(string text) =>
        string.IsNullOrEmpty(text) ? 0 : Math.Max(1, (text.Length + CharsPerPage - 1) / CharsPerPage);

    public static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html))
            return "";
        // Inline-XBRL filings hide a large metadata block (contexts, units,
        // facts) that regex tag-stripping would otherwise turn into text
        // soup ("0000789019 ... us-gaap:CommonStockMember ..."). Remove it
        // and hidden elements FIRST — this was polluting brief grounding.
        var s = Regex.Replace(html, @"(?is)<ix:header.*?</ix:header>", " ");
        // Filings are tag soup with tables; regex stripping plus entity
        // decoding is sufficient for retrieval-grade text (not rendering).
        s = Regex.Replace(s, @"(?is)<(script|style)[^>]*>.*?</\1>", " ");
        s = Regex.Replace(s, @"(?is)<!--.*?-->", " ");
        s = Regex.Replace(s, @"<[^>]+>", " ");
        s = System.Net.WebUtility.HtmlDecode(s);
        s = Regex.Replace(s, @"[ \t\xa0]+", " ");
        s = Regex.Replace(s, @" ?\n ?", "\n");
        s = Regex.Replace(s, @"\n{3,}", "\n\n");
        return s.Trim();
    }

    public static IReadOnlyList<(string Heading, string Body)> SplitSections(string text)
    {
        var sections = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(text))
            return sections;
        // 10-K/10-Q ITEM structure ("ITEM 1A. Risk Factors"); 8-Ks rarely
        // have items — no match yields one body section below.
        var matches = Regex.Matches(text, @"(?im)^[^\S\n]{0,8}ITEM\s+(\d+[A-Z]?)\.?\s*([^\n]{0,120})");
        if (matches.Count == 0)
        {
            sections.Add(("FULL TEXT", text));
            return sections;
        }
        for (int i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var heading = "ITEM " + matches[i].Groups[1].Value.ToUpperInvariant() +
                (matches[i].Groups[2].Value.Trim().Length > 0 ? " — " + matches[i].Groups[2].Value.Trim() : "");
            sections.Add((heading, text.Substring(start, Math.Min(end - start, text.Length - start)).Trim()));
        }
        return sections;
    }

    // Deterministic relevance: whole-word hits of signal terms (thread label
    // vocabulary + category words), title-weighted 3×. Ties break by order.
    public static double ScoreText(string text, IReadOnlyList<string> signalTerms)
    {
        if (string.IsNullOrWhiteSpace(text) || signalTerms.Count == 0)
            return 0;
        var lower = text.ToLowerInvariant();
        double score = 0;
        foreach (var raw in signalTerms)
        {
            var term = (raw ?? "").Trim().ToLowerInvariant();
            if (term.Length < 3)
                continue;
            score += Regex.Matches(lower, @"\b" + Regex.Escape(term) + @"\b").Count;
        }
        return score;
    }

    public static IReadOnlyList<string> SplitSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();
        return Regex.Split(text, @"(?<=[.!?])\s+(?=[A-Z0-9""“(\[])")
            .Select(s => s.Trim())
            .Where(s => s.Length >= 40 && s.Length <= 800)
            .ToList();
    }
}
