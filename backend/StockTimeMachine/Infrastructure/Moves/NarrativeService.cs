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

    public async Task<NarrativeTopicsResult> GetTopics(string symbol, DateOnly asOfDate, string? newsSource, CancellationToken ct = default, IProgress<SnapshotProgress>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new InvalidHistoricalDateException("Symbol is required.");
        HistoricalDate.Create(asOfDate);

        var normalized = symbol.Trim().ToUpperInvariant();
        var selected = NewsSources.Normalize(newsSource);
        var articles = (await _dataRepo.GetNewsAsOf(normalized, asOfDate, selected, ct))
            .Where(n => IsFromSource(n, selected)).ToList();

        var result = new NarrativeTopicsResult
        {
            CompanySymbol = normalized,
            AsOfDate = asOfDate,
            NewsSource = selected,
            ArticlesConsidered = articles.Count,
        };

        if (articles.Count == 0)
        {
            progress?.Report(new SnapshotProgress("clustering", "complete", "no cached articles", 0));
            return result;
        }

        progress?.Report(new SnapshotProgress("clustering", "started", $"{articles.Count} cached articles"));
        if (_gemini.IsEnabled && await TryAiPath(result, articles, progress, ct))
            return result;

        result.Topics = TopicClustering.Cluster(articles);
        result.ClusteringMethod = "tf-idf-fallback";
        progress?.Report(new SnapshotProgress("clustering", "complete",
            $"TF-IDF fallback: {result.Topics.Count} threads", result.Topics.Count));
        return result;
    }

    // True only when embeddings clustered end to end. Brief failures do NOT
    // fail the path: threads keep embedding clusters with labels-only briefs.
    private async Task<bool> TryAiPath(
        NarrativeTopicsResult result, List<NewsArticle> articles,
        IProgress<SnapshotProgress>? progress, CancellationToken ct)
    {
        try
        {
            var docs = articles.Take(TopicClustering.MaxArticles).ToList();
            progress?.Report(new SnapshotProgress("embedding", "started",
                $"0 of {docs.Count} articles embedded"));
            var vectors = await EmbedCached(docs,
                (done, total) => progress?.Report(new SnapshotProgress("embedding", "started",
                    $"{done} of {total} articles embedded")), ct);
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
            progress?.Report(new SnapshotProgress("embedding", "complete",
                $"{docs.Count} of {docs.Count} articles embedded", docs.Count));
            progress?.Report(new SnapshotProgress("clustering", "complete",
                $"{topics.Count} threads (embeddings)", topics.Count));

            var briefed = topics
                .Where(t => t.ArticleIds.Count > 1)
                .OrderByDescending(t => t.ArticleIds.Count)
                .Take(MaxBriefedClusters)
                .ToList();
            int b = 0;
            foreach (var topic in briefed)
            {
                progress?.Report(new SnapshotProgress("briefing", "started",
                    $"thread {++b} of {briefed.Count}: {topic.LabelTerms.FirstOrDefault() ?? "thread"}"));
                topic.Brief = await BriefAsync(result, docs, topic, ct);
            }
            if (briefed.Count > 0)
                progress?.Report(new SnapshotProgress("briefing", "complete",
                    $"{briefed.Count(b => b.Brief is not null)} of {briefed.Count} briefs written",
                    briefed.Count));

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

    // Read-through vector cache: repeat views reuse stored vectors instead of
    // re-spending quota. Keyed by article id + embedding model, so a model
    // change cleanly misses instead of mixing vector spaces. A corrupt cached
    // row is treated as a miss, never fatal.
    private async Task<IReadOnlyList<float[]>> EmbedCached(
        List<NewsArticle> docs, Action<int, int>? onProgress, CancellationToken ct)
    {
        var model = _gemini.EmbeddingModel;
        var vectors = new float[docs.Count][];
        var missingIdx = new List<int>();
        int done = 0;
        for (int i = 0; i < docs.Count; i++)
        {
            float[]? hit = null;
            try
            {
                var row = await _dataRepo.GetEmbedding(docs[i].Id, model, ct);
                if (row is not null)
                    hit = System.Text.Json.JsonSerializer.Deserialize<float[]>(row.VectorJson);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Embedding cache read miss for {Article}", docs[i].Id);
            }
            if (hit is { Length: > 0 })
            {
                vectors[i] = hit;
                onProgress?.Invoke(++done, docs.Count);
            }
            else
            {
                missingIdx.Add(i);
            }
        }
        if (missingIdx.Count > 0)
        {
            var fresh = await _gemini.EmbedAsync(
                missingIdx.Select(i => $"{docs[i].Title} {docs[i].Description}").ToList(), ct);
            onProgress?.Invoke(docs.Count, docs.Count);
            for (int k = 0; k < missingIdx.Count; k++)
            {
                vectors[missingIdx[k]] = fresh[k];
                try
                {
                    await _dataRepo.StoreEmbedding(new ArticleEmbedding
                    {
                        ArticleId = docs[missingIdx[k]].Id,
                        Model = model,
                        VectorJson = System.Text.Json.JsonSerializer.Serialize(fresh[k]),
                        CachedAt = DateTime.UtcNow,
                    }, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Embedding cache store failed for {Article}", docs[missingIdx[k]].Id);
                }
            }
            _logger.LogInformation("Embedded {Fresh}/{Total} articles ({Model}); rest served from cache",
                missingIdx.Count, docs.Count, model);
        }
        return vectors;
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

    public async Task<ClusterBrief?> BriefSharedThread(
        IReadOnlyList<string> symbols, DateOnly asOfDate, string? newsSource,
        IReadOnlyList<string> terms, CancellationToken ct = default)
    {
        // Bounded shared-story brief. Matches are deterministic vocabulary
        // overlap (≥2 shared terms, same rule as the frontend grouping); only
        // the prose is generated, under a prompt that bans cross-company
        // causation and joint verdicts. Total input capped to fit one pass.
        const int MaxSharedArticles = 8;
        const int MaxTotalChars = 60000;
        if (symbols.Count == 0 || symbols.Count > 4 || terms.Count == 0)
            return null;
        if (!_gemini.IsEnabled)
            return null;
        try
        {
            var selected = NewsSources.Normalize(newsSource);
            var wanted = new HashSet<string>(terms, StringComparer.OrdinalIgnoreCase);
            var matched = new List<(NewsArticle Doc, string Symbol)>();
            foreach (var raw in symbols)
            {
                var symbol = raw.Trim().ToUpperInvariant();
                if (string.IsNullOrEmpty(symbol))
                    continue;
                HistoricalDate.Create(asOfDate);
                var cached = await _dataRepo.GetNewsAsOf(symbol, asOfDate, selected, ct);
                foreach (var n in cached.Where(n => IsFromSource(n, selected)))
                {
                    var tokens = new HashSet<string>(
                        TopicClustering.Tokenize($"{n.Title} {n.Description}"), StringComparer.OrdinalIgnoreCase);
                    if (tokens.Intersect(wanted).Count() >= 2)
                        matched.Add((n, symbol));
                    if (matched.Count >= MaxSharedArticles)
                        break;
                }
                if (matched.Count >= MaxSharedArticles)
                    break;
            }
            if (matched.Count == 0)
                return null;
            var inputs = new List<(string Title, string Body)>();
            int used = 0, fetched = 0;
            foreach (var (d, symbol) in matched)
            {
                string bodyText = d.Description ?? "";
                if (_bodies.IsEnabled && fetched < MaxFetchedBodies && !string.IsNullOrWhiteSpace(d.Url))
                {
                    var fb = await _bodies.FetchBodyAsync(d.Url, ct);
                    if (fb is not null)
                    {
                        bodyText = fb.Markdown;
                        fetched++;
                    }
                }
                var block = $"[{symbol}] {d.Title}\n{bodyText}";
                if (used + block.Length > MaxTotalChars)
                    break;
                used += block.Length;
                if (bodyText.Length > MaxBodyChars)
                    bodyText = bodyText.Substring(0, MaxBodyChars);
                inputs.Add(($"[{symbol}] {d.Title}", bodyText));
            }
            if (inputs.Count == 0)
                return null;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"You are a historical research assistant. Today is {asOfDate:yyyy-MM-dd}.");
            sb.AppendLine($"You know NOTHING that happened after this date. Never use outside knowledge.");
            sb.AppendLine();
            sb.AppendLine($"Below are contemporary articles from {matched.Select(m => m.Symbol).Distinct().Count()} companies' coverage, all published on or before today, grouped because they share vocabulary — NOT because the stories are proven related.");
            sb.AppendLine();
            sb.AppendLine("Hard rules:");
            sb.AppendLine("- State only claims present in at least one article; cite each claim like [1], [2] (global numbers below).");
            sb.AppendLine("- NEVER state or imply that one company's events caused another's price move.");
            sb.AppendLine("- NEVER pool the companies into a joint verdict, recommendation, or prediction.");
            sb.AppendLine("- Say plainly where the shared vocabulary may be coincidental.");
            sb.AppendLine();
            sb.AppendLine("Respond with exactly these sections:");
            sb.AppendLine("SUMMARY: one paragraph, max 120 words, of what the coverage collectively reports.");
            sb.AppendLine("KEY POINTS: up to 5 bullets, each cited with its company tag.");
            sb.AppendLine("DISAGREEMENTS AND GAPS: what is contested or missing; 'none visible' if uniform.");
            sb.AppendLine();
            for (int i = 0; i < inputs.Count; i++)
            {
                sb.AppendLine($"[{i + 1}] {inputs[i].Title}");
                if (!string.IsNullOrWhiteSpace(inputs[i].Body))
                    sb.AppendLine(inputs[i].Body);
                sb.AppendLine();
            }
            return await _gemini.SummarizeClusterAsync(sb.ToString(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Shared-thread brief failed");
            return null;
        }
    }

    public async Task<IReadOnlyList<CrossThreadPair>> CrossThreadSimilarity(
        IReadOnlyList<string> symbols, DateOnly asOfDate, string? newsSource,
        CancellationToken ct = default)
    {
        const int MaxDocsPerSymbol = 30;
        const double PairThreshold = 0.70;
        const int MaxPairs = 10;
        var empty = Array.Empty<CrossThreadPair>();
        var picks = symbols.Select(s => s.Trim().ToUpperInvariant())
            .Where(s => s.Length > 0).Distinct().Take(2).ToList();
        if (picks.Count != 2 || !_gemini.IsEnabled)
            return empty;
        try
        {
            HistoricalDate.Create(asOfDate);
            var selected = NewsSources.Normalize(newsSource);
            var perSymbol = new List<(string Symbol, List<NewsArticle> Docs, IReadOnlyList<float[]> Vectors)>();
            foreach (var symbol in picks)
            {
                var cached = await _dataRepo.GetNewsAsOf(symbol, asOfDate, selected, ct);
                var docs = cached.Where(n => IsFromSource(n, selected)).Take(MaxDocsPerSymbol).ToList();
                if (docs.Count == 0)
                    return empty;
                var vectors = await EmbedCached(docs, null, ct);
                perSymbol.Add((symbol, docs, vectors));
            }
            var (aSym, aDocs, aVec) = perSymbol[0];
            var (bSym, bDocs, bVec) = perSymbol[1];
            var aClusters = EmbeddingClustering.Cluster(aVec);
            var bClusters = EmbeddingClustering.Cluster(bVec);
            var pairs = new List<CrossThreadPair>();
            foreach (var ac in aClusters)
                foreach (var bc in bClusters)
                {
                    double best = 0;
                    foreach (var i in ac)
                        foreach (var j in bc)
                            best = Math.Max(best, EmbeddingClustering.Cosine(aVec[i], bVec[j]));
                    if (best >= PairThreshold)
                        pairs.Add(new CrossThreadPair
                        {
                            ASymbol = aSym,
                            ATitle = aDocs[ac.MaxBy(k => aDocs[k].Title.Length)].Title,
                            BSymbol = bSym,
                            BTitle = bDocs[bc.MaxBy(k => bDocs[k].Title.Length)].Title,
                            Similarity = Math.Round(best, 3),
                        });
                }
            return pairs.OrderByDescending(p => p.Similarity).Take(MaxPairs).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cross-thread similarity failed");
            return empty;
        }
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
            var batches = BriefBatcher.Batch(inputs);
            if (batches.Count == 1)
            {
                var prompt = ClusterBriefPrompt.Build(result.CompanySymbol, result.AsOfDate, inputs);
                return await _gemini.SummarizeClusterAsync(prompt, ct);
            }
            // Map-reduce: one brief per batch (global numbering preserved),
            // then a final synthesis over the batch briefs.
            var mapped = new List<(string Title, string Body)>();
            int offset = 1;
            foreach (var batch in batches)
            {
                var prompt = ClusterBriefPrompt.Build(result.CompanySymbol, result.AsOfDate, batch, offset);
                var brief = await _gemini.SummarizeClusterAsync(prompt, ct);
                if (brief is null)
                    return null;
                mapped.Add(($"Batch covering articles {offset}–{offset + batch.Count - 1}",
                    brief.Summary + " " + string.Join(" ", brief.KeyPoints)));
                offset += batch.Count;
            }
            var reducePrompt = ClusterBriefPrompt.Build(
                result.CompanySymbol, result.AsOfDate, mapped, isReduce: true);
            return await _gemini.SummarizeClusterAsync(reducePrompt, ct);
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
