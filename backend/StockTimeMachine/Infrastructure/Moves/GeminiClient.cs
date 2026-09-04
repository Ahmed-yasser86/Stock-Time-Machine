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
        {
            var body = new
            {
                model = $"models/{_embeddingModel}",
                content = new { parts = new[] { new { text = Truncate(text) } } },
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
            vectors.Add(values);
        }
        return vectors;
    }

    public async Task<ClusterBrief?> SummarizeClusterAsync(string prompt, CancellationToken ct = default)
    {
        if (!IsEnabled)
            return null;
        try
        {
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

    private static string Truncate(string text) =>
        text.Length <= MaxEmbedChars ? text : text.Substring(0, MaxEmbedChars);
}
