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

    public static List<List<int>> Cluster(IReadOnlyList<float[]> vectors)
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
            clusters[bestA].AddRange(clusters[bestB]);
            clusters.RemoveAt(bestB);
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
