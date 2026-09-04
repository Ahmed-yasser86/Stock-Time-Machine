namespace StockTimeMachine;

// Keyword-overlap narrative clustering over cached news text. Deliberately NOT
// embeddings/LLM: no packages, keys, budget, or vector store exist, and TF-IDF
// + cosine is explainable, deterministic, and sufficient for grouping headlines
// into narrative threads. Quality limits (Latin-script text, weak on very short
// titles, threshold-sensitive; non-English strays form their own coherent
// threads) are documented in Methodology, not hidden.
public class TopicCluster
{
    public List<string> LabelTerms { get; set; } = new();
    public List<string> ArticleIds { get; set; } = new();
    public string RepresentativeTitle { get; set; } = "";
    public DateTime? SpanStart { get; set; }
    public DateTime? SpanEnd { get; set; }
    // AI brief when the Gemini path produced one; null on the TF-IDF path or
    // when the model declined. Presenters must label it AI-generated.
    public ClusterBrief? Brief { get; set; }
}

public static class TopicClustering
{
    public const double SimilarityThreshold = 0.25;
    public const int MaxArticles = 60;

    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "the", "and", "for", "with", "from", "that", "this", "have", "has",
        "will", "would", "could", "should", "about", "into", "over", "after",
        "before", "between", "than", "then", "them", "they", "their", "there",
        "what", "when", "where", "which", "while", "your", "amid", "among",
        "also", "says", "said", "just", "more", "most", "dont", "does", "did",
        "are", "was", "were", "been", "being", "you", "your", "says", "new",
        "its", "out", "off", "all", "any", "can",
    };

    public static List<TopicCluster> Cluster(IReadOnlyList<NewsArticle> articles)
    {
        var docs = articles.Take(MaxArticles).ToList();
        if (docs.Count == 0)
            return new List<TopicCluster>();

        var tokenized = docs.Select(d => Tokenize(d.Title + " " + d.Description)).ToList();
        var docFreq = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var tokens in tokenized)
            foreach (var term in new HashSet<string>(tokens))
                docFreq[term] = docFreq.TryGetValue(term, out var c) ? c + 1 : 1;

        int n = docs.Count;
        var vectors = tokenized.Select(tokens =>
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var t in tokens)
                counts[t] = counts.TryGetValue(t, out var c) ? c + 1 : 1;
            var vec = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var (term, count) in counts)
            {
                // Smoothed IDF: terms appearing in every document (inevitable
                // in tiny windows, e.g. the company name) keep nonzero weight
                // instead of collapsing all vectors to zero.
                var idf = Math.Log(1.0 + (double)n / docFreq[term]);
                vec[term] = count / (double)tokens.Count * idf;
            }
            return vec;
        }).ToList();

        // Greedy agglomerative, single linkage: repeatedly merge the most
        // similar pair at or above threshold. Index-ordered scan makes ties
        // deterministic.
        var clusters = Enumerable.Range(0, n).Select(i => new List<int> { i }).ToList();
        while (clusters.Count > 1)
        {
            double best = SimilarityThreshold;
            int bestA = -1, bestB = -1;
            for (int a = 0; a < clusters.Count; a++)
            {
                for (int b = a + 1; b < clusters.Count; b++)
                {
                    var sim = ClusterSimilarity(clusters[a], clusters[b], vectors);
                    if (sim > best)
                    {
                        best = sim;
                        bestA = a;
                        bestB = b;
                    }
                }
            }
            if (bestA < 0)
                break;
            clusters[bestA].AddRange(clusters[bestB]);
            clusters.RemoveAt(bestB);
        }

        return clusters
            .OrderByDescending(c => c.Count)
            .Select(members => ToCluster(members, docs, vectors))
            .ToList();
    }

    private static double ClusterSimilarity(
        List<int> a, List<int> b, List<Dictionary<string, double>> vectors)
    {
        double best = 0;
        foreach (var i in a)
            foreach (var j in b)
                best = Math.Max(best, Cosine(vectors[i], vectors[j]));
        return best;
    }

    private static double Cosine(Dictionary<string, double> a, Dictionary<string, double> b)
    {
        double dot = 0, na = 0, nb = 0;
        foreach (var (term, va) in a)
        {
            na += va * va;
            if (b.TryGetValue(term, out var vb))
                dot += va * vb;
        }
        foreach (var vb in b.Values) nb += vb * vb;
        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    private static TopicCluster ToCluster(
        List<int> members, List<NewsArticle> docs, List<Dictionary<string, double>> vectors)
    {
        var termWeight = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var i in members)
            foreach (var (term, w) in vectors[i])
                termWeight[term] = termWeight.TryGetValue(term, out var s) ? s + w : w;

        var ordered = members.OrderBy(i => i).ToList();
        var dates = ordered.Select(i => docs[i].PublishedAt).ToList();
        return new TopicCluster
        {
            LabelTerms = termWeight.OrderByDescending(kv => kv.Value).Take(3).Select(kv => kv.Key).ToList(),
            ArticleIds = ordered.Select(i => docs[i].Id).ToList(),
            RepresentativeTitle = docs[ordered.MaxBy(i => docs[i].Title.Length)].Title,
            SpanStart = dates.Min(),
            SpanEnd = dates.Max(),
        };
    }

    // Public so the AI path can reuse identical term vocabulary for labels:
    // embeddings decide membership, shared terms still name the thread.
    public static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var ch in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                current.Append(ch);
            }
            else if (current.Length > 0)
            {
                AddToken(current.ToString(), tokens);
                current.Clear();
            }
        }
        if (current.Length > 0)
            AddToken(current.ToString(), tokens);
        return tokens;
    }

    private static void AddToken(string token, List<string> tokens)
    {
        if (token.Length < 3 || Stopwords.Contains(token))
            return;
        if (token.All(char.IsDigit))
            return;
        tokens.Add(token);
    }
}
