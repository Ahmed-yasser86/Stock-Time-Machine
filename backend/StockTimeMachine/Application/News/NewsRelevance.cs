namespace StockTimeMachine;

// Deterministic company-centrality ranking for evidence lists. The product
// never judges materiality (that would be analysis), but it can honestly
// surface which articles name the company in the TITLE versus those that
// matched only on entity tagging: title mentions sort first, everything else
// keeps time order. Nothing is hidden, nothing is scored — the same rows,
// better arranged. Pure and unit-tested.
public static class NewsRelevance
{
    public static bool NamesCompany(NewsArticle article, string symbol, string? companyName)
    {
        var title = article.Title ?? "";
        if (title.Contains(symbol, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrWhiteSpace(companyName))
        {
            // First significant word ("Apple" in "Apple Inc.") catches most
            // headline styles without fuzzy matching.
            var first = companyName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrEmpty(first) && first.Length >= 3 &&
                title.Contains(first, StringComparison.OrdinalIgnoreCase))
                return true;
            if (title.Contains(companyName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static List<NewsArticle> OrderByMention(
        IEnumerable<NewsArticle> articles, string symbol, string? companyName) =>
        articles
            .OrderByDescending(n => NamesCompany(n, symbol, companyName))
            .ThenByDescending(n => n.PublishedAt)
            .ThenBy(n => n.Id)
            .ToList();
}
