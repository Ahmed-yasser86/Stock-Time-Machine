using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace StockTimeMachine;

// Qdrant-backed IVectorStore. Client lives here in Infrastructure —
// Application sees IVectorStore records only. Collection `hype_threads`
// holds one point per admitted pre-peak thread article (payload carries the
// owning case). Cosine similarity is ALWAYS recomputed locally from returned
// vectors: immune to server score-mapping conventions. Every failure degrades
// to unavailable/empty (logged), never throws.
public class QdrantVectorStore : IVectorStore
{
    public const string CollectionName = "hype_threads";
    // Must equal the embedding model output dims (gemini-embedding-2-preview).
    // Mismatched vectors are skipped loudly, never truncated/padded.
    public const int VectorSize = 3072;
    // Case-level pattern collection: one hybrid vector per hype case (see
    // HypeCaseVector). Versioned name: v1 was 40-d structural-only, v2 is the
    // hybrid. v1 stays untouched for rollback; delete after validation.
    public const string CaseCollectionName = "hype_cases_v2";
    // Structural-only collection (Phase 4 dual-query): same cases, 96-d
    // structural side only, so structural-dominant signals match on pattern
    // regardless of news topic.
    public const string StructuralCollectionName = "hype_cases_structural";

    private readonly QdrantClient? _client;
    private readonly ILogger<QdrantVectorStore> _logger;
    private bool _ensured;

    public QdrantVectorStore(IConfiguration config, ILogger<QdrantVectorStore> logger)
    {
        _logger = logger;
        // Keys travel server-side only (user-secrets/env Qdrant:ApiKey);
        // never logged, never returned, never committed.
        var host = (config["Qdrant:Host"] ?? "").Trim();
        var apiKey = (config["Qdrant:ApiKey"] ?? "").Trim();
        var port = int.TryParse(config["Qdrant:Port"], out var p) && p > 0 ? p : 6334;
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(apiKey))
        {
            _logger.LogInformation("Qdrant not configured (Qdrant:Host/Qdrant:ApiKey); vector store unavailable");
            return;
        }
        try
        {
            _client = new QdrantClient(host, port: port, https: true, apiKey: apiKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Qdrant client construction failed; vector store unavailable");
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (_client is null)
            return false;
        try
        {
            await _client.ListCollectionsAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Qdrant unreachable");
            return false;
        }
    }

    public async Task<(bool Reachable, ulong Points)> HealthAsync(CancellationToken ct = default)
    {
        if (_client is null)
            return (false, 0);
        try
        {
            await _client.HealthAsync(ct);
            await EnsureCollectionAsync(ct);
            await EnsureCollectionAsync(CaseCollectionName, (ulong)HypeCaseVector.Dimensions, ct);
            await EnsureCollectionAsync(StructuralCollectionName, (ulong)HypeCaseVector.StructuralDimensions, ct);
            var threads = await _client.GetCollectionInfoAsync(CollectionName, ct);
            var cases = await _client.GetCollectionInfoAsync(CaseCollectionName, ct);
            var structural = await _client.GetCollectionInfoAsync(StructuralCollectionName, ct);
            return (true, threads.PointsCount + cases.PointsCount + structural.PointsCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Qdrant health check failed");
            return (false, 0);
        }
    }

    public async Task<int> UpsertStructuralAsync(
        string caseId, string symbol, DateOnly peakDate,
        float[] vector, CancellationToken ct = default)
    {
        if (_client is null || vector is null || vector.Length != HypeCaseVector.StructuralDimensions)
        {
            _logger.LogWarning("Skipping structural point for {Case}: {Dims} dims, expected {Expected}",
                caseId, vector?.Length ?? 0, HypeCaseVector.StructuralDimensions);
            return 0;
        }
        try
        {
            await EnsureCollectionAsync(StructuralCollectionName, (ulong)HypeCaseVector.StructuralDimensions, ct);
            await _client.UpsertAsync(StructuralCollectionName, new List<PointStruct>
            {
                new()
                {
                    Id = DeterministicId("hype-case-structural|" + caseId),
                    Vectors = vector,
                    Payload =
                    {
                        ["caseId"] = new Value { StringValue = caseId },
                        ["symbol"] = new Value { StringValue = symbol },
                        ["peakDate"] = new Value { StringValue = peakDate.ToString("yyyy-MM-dd") },
                    },
                },
            }, cancellationToken: ct);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Qdrant structural upsert failed for {Case}", caseId);
            return 0;
        }
    }

    public async Task<IReadOnlyList<VectorHit>> SearchStructuralAsync(
        float[] query, int limit, CancellationToken ct = default)
    {
        var empty = Array.Empty<VectorHit>();
        if (_client is null || query is null || query.Length != HypeCaseVector.StructuralDimensions || limit <= 0)
            return empty;
        try
        {
            await EnsureCollectionAsync(StructuralCollectionName, (ulong)HypeCaseVector.StructuralDimensions, ct);
            return await SearchCollectionAsync(StructuralCollectionName, query, limit, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Qdrant structural search failed; caller falls back");
            return empty;
        }
    }

    public async Task<int> UpsertAsync(
        string caseId, string symbol, DateOnly peakDate,
        IReadOnlyList<(string ArticleId, float[] Vector)> vectors,
        CancellationToken ct = default)
    {
        if (_client is null || vectors.Count == 0)
            return 0;
        try
        {
            await EnsureCollectionAsync(ct);
            var points = new List<PointStruct>();
            foreach (var (articleId, vector) in vectors)
            {
                if (vector is null || vector.Length != VectorSize)
                {
                    _logger.LogWarning("Skipping vector for {Article}: {Dims} dims, expected {Expected}",
                        articleId, vector?.Length ?? 0, VectorSize);
                    continue;
                }
                points.Add(new PointStruct
                {
                    Id = DeterministicId(articleId),
                    Vectors = vector,
                    Payload =
                    {
                        ["articleId"] = new Value { StringValue = articleId },
                        ["caseId"] = new Value { StringValue = caseId },
                        ["symbol"] = new Value { StringValue = symbol },
                        ["peakDate"] = new Value { StringValue = peakDate.ToString("yyyy-MM-dd") },
                    },
                });
            }
            if (points.Count == 0)
                return 0;
            await _client.UpsertAsync(CollectionName, points, cancellationToken: ct);
            return points.Count;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Qdrant upsert failed for case {Case}", caseId);
            return 0;
        }
    }

    public async Task<IReadOnlyList<VectorHit>> SearchAsync(
        float[] query, int limit, CancellationToken ct = default)
    {
        var empty = Array.Empty<VectorHit>();
        if (_client is null || query is null || query.Length != VectorSize || limit <= 0)
            return empty;
        try
        {
            await EnsureCollectionAsync(ct);
            return await SearchCollectionAsync(CollectionName, query, limit, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Qdrant search failed; caller falls back");
            return empty;
        }
    }

    public async Task<int> UpsertCaseAsync(
        string caseId, string symbol, DateOnly peakDate,
        float[] vector, CancellationToken ct = default)
    {
        if (_client is null || vector is null || vector.Length != HypeCaseVector.Dimensions)
        {
            _logger.LogWarning("Skipping case point for {Case}: {Dims} dims, expected {Expected}",
                caseId, vector?.Length ?? 0, HypeCaseVector.Dimensions);
            return 0;
        }
        try
        {
            await EnsureCollectionAsync(CaseCollectionName, (ulong)HypeCaseVector.Dimensions, ct);
            await _client.UpsertAsync(CaseCollectionName, new List<PointStruct>
            {
                new()
                {
                    Id = DeterministicId("hype-case|" + caseId),
                    Vectors = vector,
                    Payload =
                    {
                        ["caseId"] = new Value { StringValue = caseId },
                        ["symbol"] = new Value { StringValue = symbol },
                        ["peakDate"] = new Value { StringValue = peakDate.ToString("yyyy-MM-dd") },
                    },
                },
            }, cancellationToken: ct);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Qdrant case upsert failed for {Case}", caseId);
            return 0;
        }
    }

    public async Task<IReadOnlyList<VectorHit>> SearchCasesAsync(
        float[] query, int limit, CancellationToken ct = default)
    {
        var empty = Array.Empty<VectorHit>();
        if (_client is null || query is null || query.Length != HypeCaseVector.Dimensions || limit <= 0)
            return empty;
        try
        {
            await EnsureCollectionAsync(CaseCollectionName, (ulong)HypeCaseVector.Dimensions, ct);
            return await SearchCollectionAsync(CaseCollectionName, query, limit, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Qdrant case search failed; caller falls back");
            return empty;
        }
    }

    private async Task EnsureCollectionAsync(CancellationToken ct)
    {
        if (_ensured || _client is null)
            return;
        await EnsureCollectionAsync(CollectionName, (ulong)VectorSize, ct);
        _ensured = true;
    }

    private async Task EnsureCollectionAsync(string collection, ulong size, CancellationToken ct)
    {
        if (_client is null)
            return;
        if (!await _client.CollectionExistsAsync(collection, ct))
        {
            await _client.CreateCollectionAsync(collection,
                new VectorParams { Size = size, Distance = Distance.Cosine },
                cancellationToken: ct);
            _logger.LogInformation("Qdrant collection {Collection} created ({Dims}d, cosine)",
                collection, size);
            return;
        }
        // INVARIANT (learned the hard way): a vector redesign MUST rename the
        // collection (v1 → v2 → …). Qdrant rejects wrong-dim writes, and this
        // method deliberately never deletes or migrates (operator action only
        // via explicit delete + reindex). A stale-dim collection surfaces as
        // failed upserts/searches in the per-call warnings below.
    }

    // Shared ANN query: vectors return with hits so callers recompute cosine
    // locally (immune to server score-mapping conventions).
    private async Task<IReadOnlyList<VectorHit>> SearchCollectionAsync(
        string collection, float[] query, int limit, CancellationToken ct)
    {
        var expectedSize = collection == CaseCollectionName ? HypeCaseVector.Dimensions
            : collection == StructuralCollectionName ? HypeCaseVector.StructuralDimensions
            : VectorSize;
        var withPayload = new WithPayloadSelector { Enable = true };
        var withVectors = new WithVectorsSelector { Enable = true };
        var dense = new DenseVector();
        dense.Data.AddRange(query);
        var vectorQuery = new Query { Nearest = new VectorInput { Dense = dense } };
        var scored = await _client!.QueryAsync(
            collection, vectorQuery,
            null, null, null, null, null, (ulong)Math.Min(limit, 50), 0,
            withPayload, withVectors, null, null, null, null, ct);
        var hits = new List<VectorHit>();
        foreach (var point in scored)
        {
            var payload = point.Payload;
            string Str(string key) =>
                payload.TryGetValue(key, out var v) ? v.StringValue ?? "" : "";
            var vec = point.Vectors?.Vector is { } vd &&
                vd.VectorCase == VectorOutput.VectorOneofCase.Dense
                ? vd.Dense.Data.ToArray()
                : null;
            if (vec is null || vec.Length != expectedSize)
                continue;
            if (!DateOnly.TryParseExact(Str("peakDate"), "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var peakDate))
                continue;
            hits.Add(new VectorHit(
                Str("articleId").Length > 0 ? Str("articleId") : point.Id.ToString(),
                vec, Str("caseId"), Str("symbol"), peakDate));
        }
        return hits;
    }

    // Stable point ids (MD5 → Guid): re-indexing a case upserts the same
    // points instead of duplicating them.
    private static Qdrant.Client.Grpc.PointId DeterministicId(string articleId)
    {
        var hash = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes("hype-threads|" + articleId));
        return new Qdrant.Client.Grpc.PointId { Uuid = new Guid(hash).ToString() };
    }
}
