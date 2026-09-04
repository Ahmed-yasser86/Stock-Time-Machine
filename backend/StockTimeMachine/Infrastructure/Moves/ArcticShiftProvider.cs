using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

// Retail discussion via Arctic Shift (community Reddit archive, 2005–present).
// Keyless, ~120k req/hr, monthly releases (not realtime), no SLA.
// Temporal basis: each post's own created_utc instant, filtered to the UTC-date
// window [from, to] inclusive. UTC-date filtering is deliberately conservative:
// it can omit edge-hour posts but can never leak future knowledge.
// Undated/unparseable items are dropped, never coerced. Transport failures throw
// (the caller records honest "unavailable"); valid-empty returns empty.
public class ArcticShiftProvider : ISocialSignalProvider
{
    // Default is the single highest-signal retail venue. The service throttles
    // aggressively per IP: fewer communities per investigation keeps the layer
    // usable. Operators can widen via Social:ArcticShift:Subreddits.
    private static readonly string[] DefaultSubreddits = new[] { "wallstreetbets" };

    private readonly HttpClient _http;
    private readonly ILogger<ArcticShiftProvider> _logger;
    private readonly string _baseUrl;
    private readonly bool _enabled;
    private readonly string[] _subreddits;

    public string ProviderName => "Arctic Shift";

    public ArcticShiftProvider(HttpClient http, ILogger<ArcticShiftProvider> logger, IConfiguration config)
    {
        _http = http;
        _logger = logger;
        _baseUrl = (config["Social:ArcticShift:BaseUrl"] ?? "https://arctic-shift.photon-reddit.com").TrimEnd('/');
        _enabled = (config["Social:ArcticShift:Enabled"] ?? "true").Equals("true", StringComparison.OrdinalIgnoreCase);
        _subreddits = (config["Social:ArcticShift:Subreddits"] ?? string.Join(",", DefaultSubreddits))
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (_subreddits.Length == 0)
            _subreddits = DefaultSubreddits;
    }

    public async Task<IReadOnlyList<SocialSignal>> GetSignals(
        string symbol, string? companyName, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (!_enabled)
            return Array.Empty<SocialSignal>();

        var normalized = symbol.Trim().ToUpperInvariant();
        // One query per community (company name preferred, symbol fallback):
        // this is a free community service, so call volume stays minimal and
        // paced. Burst traffic gets throttled (HTTP 422) and degrades honestly.
        var query = !string.IsNullOrWhiteSpace(companyName) ? companyName.Trim() : normalized;

        var all = new List<SocialSignal>();
        var first = true;
        foreach (var sub in _subreddits.Take(3))
        {
            // Community service with aggressive throttling: gentle pacing.
            if (!first)
                await Task.Delay(1500, ct);
            first = false;
            var batch = await SearchSubreddit(sub, query, normalized, from, to, ct);
            all.AddRange(batch);
        }

        return all
            .GroupBy(s => s.Id)
            .Select(g => g.First())
            .OrderByDescending(s => s.Score)
            .Take(6)
            .ToList();
    }

    private async Task<List<SocialSignal>> SearchSubreddit(
        string subreddit, string query, string symbol, DateOnly from, DateOnly to, CancellationToken ct)
    {
        using var response = await _http.GetAsync(
            $"{_baseUrl}/api/posts/search?query={Uri.EscapeDataString(query)}&subreddit={Uri.EscapeDataString(subreddit)}&limit=25",
            ct);
        if ((int)response.StatusCode == 429)
            throw new RateLimitExceededException("Arctic Shift rate limit exceeded.");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return new List<SocialSignal>();

        var results = new List<SocialSignal>();
        foreach (var post in data.EnumerateArray())
        {
            if (!post.TryGetProperty("created_utc", out var createdEl) ||
                !createdEl.TryGetInt64(out var epoch))
                continue;
            var created = DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
            var createdDay = DateOnly.FromDateTime(created);
            if (createdDay < from || createdDay > to)
                continue;

            var id = post.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
            var title = post.TryGetProperty("title", out var tEl) ? tEl.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(id) || string.IsNullOrWhiteSpace(title))
                continue;

            var selftext = post.TryGetProperty("selftext", out var sEl) ? sEl.GetString() ?? "" : "";
            var permalink = post.TryGetProperty("permalink", out var pEl) ? pEl.GetString() ?? "" : "";
            results.Add(new SocialSignal
            {
                Id = "arctic-" + id,
                Provider = ProviderName,
                Community = "r/" + subreddit,
                Title = title,
                Excerpt = selftext.Length > 300 ? selftext.Substring(0, 300) + "…" : selftext,
                Url = string.IsNullOrEmpty(permalink) ? "" : "https://www.reddit.com" + permalink,
                CreatedAt = DateTime.SpecifyKind(created, DateTimeKind.Utc),
                Score = post.TryGetProperty("score", out var scEl) && scEl.TryGetInt32(out var score) ? score : 0,
                CommentCount = post.TryGetProperty("num_comments", out var ccEl) && ccEl.TryGetInt32(out var cc) ? cc : 0,
                Flair = post.TryGetProperty("link_flair_text", out var fEl) && fEl.ValueKind == JsonValueKind.String
                    ? fEl.GetString() : null,
                CompanySymbol = symbol,
            });
        }

        return results;
    }
}
