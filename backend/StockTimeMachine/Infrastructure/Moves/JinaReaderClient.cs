using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

// Jina Reader: article URL in, clean markdown out (no HTML pollution).
// Fail-soft by contract: paywalls, bot-blocks, timeouts, and quota errors all
// yield null so briefs fall back to stored title + description.
public class JinaReaderClient : IArticleContentClient
{
    private const int MaxBodyChars = 1500;

    private readonly HttpClient _http;
    private readonly ILogger<JinaReaderClient> _logger;
    private readonly string _apiKey;
    private readonly AdaptiveRateLimiter _limiter;

    public bool IsEnabled => !string.IsNullOrEmpty(_apiKey);

    public JinaReaderClient(HttpClient http, ILogger<JinaReaderClient> logger, IConfiguration config)
    {
        _http = http;
        _logger = logger;
        _apiKey = config["Jina:ApiKey"] ?? "";
        _http.Timeout = TimeSpan.FromSeconds(45);
        _limiter = RateLimiterRegistry.Get("jina", config);
    }

    public async Task<ArticleBody?> FetchBodyAsync(string articleUrl, CancellationToken ct = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(articleUrl))
            return null;
        await _limiter.AcquireAsync(0, ct);
        try
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Get, $"https://r.jina.ai/{articleUrl}");
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_apiKey}");
            req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.TryAddWithoutValidation("X-Respond-With", "markdown");
            req.Headers.TryAddWithoutValidation("X-Retain-Links", "none");
            req.Headers.TryAddWithoutValidation("X-Timeout", "30");
            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            // Envelope: { code, status, data: "<markdown string>", meta }
            var markdown = doc.RootElement.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.String
                ? data.GetString() ?? ""
                : "";
            if (string.IsNullOrWhiteSpace(markdown))
                return null;
            return new ArticleBody
            {
                Markdown = markdown.Length <= MaxBodyChars ? markdown : markdown.Substring(0, MaxBodyChars),
                RetrievedAtUtc = DateTime.UtcNow,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Jina body fetch failed for {Url}; using stored text", articleUrl);
            return null;
        }
    }
}
