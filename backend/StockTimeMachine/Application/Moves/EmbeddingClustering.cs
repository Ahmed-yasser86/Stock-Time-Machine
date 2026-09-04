namespace StockTimeMachine;

// Same greedy agglomerative merger as TopicClustering, but over dense
// embedding vectors instead of TF-IDF term weights. Cosine over embeddings
// runs hotter (same-story pairs typically 0.8+), so the threshold is higher
// and lives separately: tuning one must never move the other.
// Deterministic given the same vectors; vectors themselves come from the
// (versioned, non-deterministic) embedding model — hence the AI label.
public static class EmbeddingClustering
{
    public const double SimilarityThreshold = 0.75;

    public static List<List<int>> Cluster(
        IReadOnlyList<float[]> vectors,
        IList<double>? mergeSimilarities = null,
        IList<double>? rejectedTop = null,
        IList<string>? rejectedPairs = null,
        IReadOnlyList<string>? titles = null)
    {
        var clusters = Enumerable.Range(0, vectors.Count).Select(i => new List<int> { i }).ToList();
        while (clusters.Count > 1)
        {
            double best = SimilarityThreshold;
            int bestA = -1, bestB = -1;
            for (int a = 0; a < clusters.Count; a++)
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
            if (bestA < 0)
                break;
            mergeSimilarities?.Add(best);
            clusters[bestA].AddRange(clusters[bestB]);
            clusters.RemoveAt(bestB);
        }
        // Tuning signal: strongest similarities that still fell below the bar.
        // Logged by callers at Debug; guides threshold changes with evidence.
        if (rejectedTop is not null)
        {
            var near = new List<double>();
            for (int a = 0; a < clusters.Count; a++)
                for (int b = a + 1; b < clusters.Count; b++)
                    near.Add(ClusterSimilarity(clusters[a], clusters[b], vectors));
            foreach (var s in near.OrderByDescending(s => s).Take(5))
                rejectedTop.Add(s);
        }
        if (rejectedPairs is not null && titles is not null)
        {
            var near = new List<(double Sim, string Pair)>();
            for (int a = 0; a < clusters.Count; a++)
                for (int b = a + 1; b < clusters.Count; b++)
                    near.Add((ClusterSimilarity(clusters[a], clusters[b], vectors),
                        $"{Short(titles[clusters[a][0]])} <> {Short(titles[clusters[b][0]])}"));
            foreach (var (sim, pair) in near.OrderByDescending(x => x.Sim).Take(5))
                rejectedPairs.Add($"{sim:F3} | {pair}");
        }
        return clusters.OrderByDescending(c => c.Count).ToList();
    }

    private static double ClusterSimilarity(List<int> a, List<int> b, IReadOnlyList<float[]> vectors)
    {
        double best = 0;
        foreach (var i in a)
            foreach (var j in b)
                best = Math.Max(best, Cosine(vectors[i], vectors[j]));
        return best;
    }

    private static string Short(string title) =>
        title.Length <= 60 ? title : title.Substring(0, 60);

    public static double Cosine(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        double dot = 0, na = 0, nb = 0;
        int n = Math.Min(a.Count, b.Count);
        for (int i = 0; i < n; i++)
        {
            dot += (double)a[i] * b[i];
            na += (double)a[i] * a[i];
            nb += (double)b[i] * b[i];
        }
        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}
