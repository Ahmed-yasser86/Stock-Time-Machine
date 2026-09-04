using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

// Plain REST against generativelanguage.googleapis.com. No Semantic Kernel:
// two endpoints (embedContent, generateContent) do not justify a framework
// dependency in a codebase with a standing no-new-packages discipline.
public class GeminiClient : IGeminiClient
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";
    private const int MaxEmbedChars = 1000;

    private readonly HttpClient _http;
    private readonly ILogger<GeminiClient> _logger;
    private readonly string _apiKey;
    private readonly string _embeddingModel;
    private readonly string _summaryModel;
    // 30k tokens/min budget shared by embeds + summaries: callers wait for
    // budget instead of tripping quota errors mid-investigation.
    private readonly TokenBucketRateLimiter _limiter = new(30000);

    public bool IsEnabled => !string.IsNullOrEmpty(_apiKey);
    public string SummaryModel => _summaryModel;

    public GeminiClient(HttpClient http, ILogger<GeminiClient> logger, IConfiguration config)
    {
        _http = http;
        _logger = logger;
        // Keys live server-side (user-secrets/env); never logged or returned.
        _apiKey = config["Gemini:ApiKey"] ?? "";
        _embeddingModel = config["Gemini:EmbeddingModel"] ?? "gemini-embedding-2-preview";
        _summaryModel = config["Gemini:SummaryModel"] ?? "gemini-3.5-flash-lite";
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (!IsEnabled)
            throw new InvalidOperationException("Gemini:ApiKey is not configured.");
        var vectors = new List<float[]>(texts.Count);
        foreach (var text in texts)
            vectors.Add(await EmbedTextAsync(text, ct));
        return vectors;
    }

    // Over-long articles are chunked and mean-pooled into one vector instead
    // of truncated: truncation silently drops the article's tail, pooling
    // keeps every chunk's vote. Limiter waits per chunk (queueing is pacing).
    private async Task<float[]> EmbedTextAsync(string text, CancellationToken ct)
    {
        var chunks = Chunk(text).ToList();
        var pooled = new float[0];
        int count = 0;
        foreach (var chunk in chunks)
        {
            await _limiter.WaitAsync(TokenBucketRateLimiter.EstimateTokens(chunk), ct);
            var body = new
            {
                model = $"models/{_embeddingModel}",
                content = new { parts = new[] { new { text = chunk } } },
            };
            using var resp = await _http.PostAsJsonAsync(
                $"{BaseUrl}/{_embeddingModel}:embedContent?key={_apiKey}", body, ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var values = doc.RootElement
                .GetProperty("embedding")
                .GetProperty("values")
                .EnumerateArray()
                .Select(e => e.GetSingle())
                .ToArray();
            if (count == 0)
                pooled = new float[values.Length];
            if (values.Length != pooled.Length)
                throw new InvalidOperationException("Embedding dimensions changed mid-text.");
            for (int i = 0; i < values.Length; i++)
                pooled[i] += values[i];
            count++;
        }
        for (int i = 0; i < pooled.Length; i++)
            pooled[i] /= count;
        return pooled;
    }

    private static IEnumerable<string> Chunk(string text)
    {
        if (text.Length <= MaxEmbedChars)
        {
            yield return text;
            yield break;
        }
        for (int i = 0; i < text.Length; i += MaxEmbedChars)
            yield return text.Substring(i, Math.Min(MaxEmbedChars, text.Length - i));
    }

    public async Task<ClusterBrief?> SummarizeClusterAsync(string prompt, CancellationToken ct = default)
    {
        if (!IsEnabled)
            return null;
        try
        {
            // Reserve prompt + output ceiling so summaries share the budget honestly.
            await _limiter.WaitAsync(TokenBucketRateLimiter.EstimateTokens(prompt) + 768, ct);
            var body = new
            {
                generationConfig = new
                {
                    temperature = 0.0,
                    maxOutputTokens = 768,
                    responseMimeType = "application/json",
                    responseJsonSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            summary = new { type = "string" },
                            key_points = new { type = "array", items = new { type = "string" } },
                        },
                        required = new[] { "summary", "key_points" },
                    },
                },
                contents = new[] { new { parts = new[] { new { text = prompt } } } },
            };
            using var resp = await _http.PostAsJsonAsync(
                $"{BaseUrl}/{_summaryModel}:generateContent?key={_apiKey}", body, ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var text = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? "";
            using var brief = JsonDocument.Parse(text);
            return new ClusterBrief
            {
                Summary = brief.RootElement.GetProperty("summary").GetString() ?? "",
                KeyPoints = brief.RootElement.GetProperty("key_points")
                    .EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList(),
                Model = _summaryModel,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gemini cluster summary failed; caller falls back");
            return null;
        }
    }

    public async Task<IReadOnlyList<NoteIssue>> ReviewNoteAsync(string prompt, CancellationToken ct = default)
    {
        var empty = Array.Empty<NoteIssue>();
        if (!IsEnabled)
            return empty;
        try
        {
            await _limiter.WaitAsync(TokenBucketRateLimiter.EstimateTokens(prompt) + 512, ct);
            var body = new
            {
                generationConfig = new
                {
                    temperature = 0.0,
                    maxOutputTokens = 512,
                    responseMimeType = "application/json",
                    responseJsonSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            issues = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        @ref = new { type = "string" },
                                        verdict = new { type = "string" },
                                        detail = new { type = "string" },
                                    },
                                    required = new[] { "ref", "verdict", "detail" },
                                },
                            },
                        },
                        required = new[] { "issues" },
                    },
                },
                contents = new[] { new { parts = new[] { new { text = prompt } } } },
            };
            using var resp = await _http.PostAsJsonAsync(
                $"{BaseUrl}/{_summaryModel}:generateContent?key={_apiKey}", body, ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var text = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? "";
            using var review = JsonDocument.Parse(text);
            return review.RootElement.GetProperty("issues").EnumerateArray().Select(e => new NoteIssue
            {
                Ref = e.TryGetProperty("ref", out var r) ? r.GetString() ?? "" : "",
                Verdict = e.TryGetProperty("verdict", out var v) ? v.GetString() ?? "unclear" : "unclear",
                Detail = e.TryGetProperty("detail", out var d) ? d.GetString() ?? "" : "",
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gemini note review failed");
            return empty;
        }
    }

}
