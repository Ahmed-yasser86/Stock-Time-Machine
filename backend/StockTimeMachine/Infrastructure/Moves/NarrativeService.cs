using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

public class NarrativeService : INarrativeService
{
    // Briefs are bounded: multi-article threads only (singletons keep labels),
    // largest first, Jina bodies for at most 3 articles per thread. Everything
    // beyond the caps degrades silently to labels-only — never to errors.
    private const int MaxBriefedClusters = 8;
    private const int MaxBriefArticles = 5;
    private const int MaxFetchedBodies = 3;
    private const int MaxBodyChars = 1500;

    private readonly IHistoricalDataRepository _dataRepo;
    private readonly IGeminiClient _gemini;
    private readonly IArticleContentClient _bodies;
    private readonly ILogger<NarrativeService> _logger;

    public NarrativeService(
        IHistoricalDataRepository dataRepo,
        IGeminiClient gemini,
        IArticleContentClient bodies,
        ILogger<NarrativeService> logger)
    {
        _dataRepo = dataRepo;
        _gemini = gemini;
        _bodies = bodies;
        _logger = logger;
    }

    public async Task<NarrativeTopicsResult> GetTopics(string symbol, DateOnly asOfDate, string? newsSource, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new InvalidHistoricalDateException("Symbol is required.");
        HistoricalDate.Create(asOfDate);

        var normalized = symbol.Trim().ToUpperInvariant();
        var selected = NewsSources.Normalize(newsSource);
        var cached = await _dataRepo.GetNewsAsOf(normalized, asOfDate, ct);
        var articles = cached.Where(n => IsFromSource(n, selected)).ToList();

        var result = new NarrativeTopicsResult
        {
            CompanySymbol = normalized,
            AsOfDate = asOfDate,
            NewsSource = selected,
            ArticlesConsidered = articles.Count,
        };

        if (articles.Count == 0)
            return result;

        if (_gemini.IsEnabled && await TryAiPath(result, articles, ct))
            return result;

        result.Topics = TopicClustering.Cluster(articles);
        result.ClusteringMethod = "tf-idf-fallback";
        return result;
    }

    // True only when embeddings clustered end to end. Brief failures do NOT
    // fail the path: threads keep embedding clusters with labels-only briefs.
    private async Task<bool> TryAiPath(
        NarrativeTopicsResult result, List<NewsArticle> articles, CancellationToken ct)
    {
        try
        {
            var docs = articles.Take(TopicClustering.MaxArticles).ToList();
            var vectors = await _gemini.EmbedAsync(
                docs.Select(d => $"{d.Title} {d.Description}").ToList(), ct);
            var mergeSimilarities = new List<double>();
            var rejectedTop = new List<double>();
            var rejectedPairs = new List<string>();
            var memberLists = EmbeddingClustering.Cluster(vectors, mergeSimilarities, rejectedTop,
                rejectedPairs, docs.Select(d => d.Title).ToList());
            _logger.LogDebug("Embedding clustering for {Symbol}: {Merges} merges at [{Similarities}], strongest rejected [{Rejected}] :: {Pairs}",
                result.CompanySymbol, mergeSimilarities.Count,
                string.Join(", ", mergeSimilarities.Select(s => s.ToString("F3"))),
                string.Join(", ", rejectedTop.Select(s => s.ToString("F3"))),
                string.Join(" ;; ", rejectedPairs));
            var topics = memberLists.Select(members => ToCluster(members, docs, vectors)).ToList();

            foreach (var topic in topics
                .Where(t => t.ArticleIds.Count > 1)
                .OrderByDescending(t => t.ArticleIds.Count)
                .Take(MaxBriefedClusters))
            {
                topic.Brief = await BriefAsync(result, docs, topic, ct);
            }

            result.Topics = topics;
            result.ClusteringMethod = "gemini-embeddings";
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI narrative path failed; falling back to TF-IDF");
            return false;
        }
    }

    private TopicCluster ToCluster(List<int> members, List<NewsArticle> docs, IReadOnlyList<float[]> vectors)
    {
        // Labels stay TF-IDF-term based (explainable vocabulary) even on the AI
        // path: embeddings decide MEMBERSHIP, shared terms still name the thread.
        var ordered = members.OrderBy(i => i).ToList();
        var dates = ordered.Select(i => docs[i].PublishedAt).ToList();
        return new TopicCluster
        {
            LabelTerms = LabelTerms(ordered, docs),
            ArticleIds = ordered.Select(i => docs[i].Id).ToList(),
            RepresentativeTitle = docs[ordered.MaxBy(i => docs[i].Title.Length)].Title,
            SpanStart = dates.Min(),
            SpanEnd = dates.Max(),
        };
    }

    private static List<string> LabelTerms(List<int> members, List<NewsArticle> docs)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var i in members)
            foreach (var token in TopicClustering.Tokenize($"{docs[i].Title} {docs[i].Description}"))
                counts[token] = counts.TryGetValue(token, out var c) ? c + 1 : 1;
        return counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key)
            .Take(3).Select(kv => kv.Key).ToList();
    }

    private async Task<ClusterBrief?> BriefAsync(
        NarrativeTopicsResult result, List<NewsArticle> docs, TopicCluster topic, CancellationToken ct)
    {
        try
        {
            var members = docs
                .Select((d, i) => (d, i))
                .Where(x => topic.ArticleIds.Contains(x.d.Id))
                .Take(MaxBriefArticles)
                .ToList();
            var inputs = new List<(string Title, string Body)>();
            int fetched = 0;
            foreach (var (d, _) in members)
            {
                string bodyText = d.Description ?? "";
                if (_bodies.IsEnabled && fetched < MaxFetchedBodies && !string.IsNullOrWhiteSpace(d.Url))
                {
                    var fetched_ = await _bodies.FetchBodyAsync(d.Url, ct);
                    if (fetched_ is not null)
                    {
                        bodyText = fetched_.Markdown;
                        fetched++;
                    }
                }
                if (bodyText.Length > MaxBodyChars)
                    bodyText = bodyText.Substring(0, MaxBodyChars);
                inputs.Add((d.Title, bodyText));
            }
            var prompt = ClusterBriefPrompt.Build(result.CompanySymbol, result.AsOfDate, inputs);
            return await _gemini.SummarizeClusterAsync(prompt, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cluster brief failed; thread keeps labels only");
            return null;
        }
    }

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
