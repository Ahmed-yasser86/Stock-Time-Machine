using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

public class HypeFilingService : IHypeFilingService
{
    private const int MaxFilings = 3;
    private const int MaxDownloadChars = 1500000;
    private const int ExtractSentences = 60;
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(15);

    private readonly IHttpClientFactory _httpFactory;
    private readonly IGeminiClient _gemini;
    private readonly IConfiguration _config;
    private readonly ILogger<HypeFilingService> _logger;

    public HypeFilingService(
        IHttpClientFactory httpFactory,
        IGeminiClient gemini,
        IConfiguration config,
        ILogger<HypeFilingService> logger)
    {
        _httpFactory = httpFactory;
        _gemini = gemini;
        _config = config;
        _logger = logger;
    }

    public async Task<IReadOnlyList<HypeFilingSummary>> SummarizeFilingsAsync(
        string symbol,
        HypeSignalMatch match,
        HypeCaseDetail detail,
        DateOnly asOfDate,
        CancellationToken ct = default)
    {
        var empty = Array.Empty<HypeFilingSummary>();
        if (match is null || detail is null)
            return empty;
        var filings = detail.Evidence.Filings
            .Where(f => f is not null && !string.IsNullOrWhiteSpace(f.Url))
            .OrderByDescending(f => f.FiledAt)
            .Take(MaxFilings)
            .ToList();
        if (filings.Count == 0 || !_gemini.IsEnabled)
            return empty;

        // Signal vocabulary for section retrieval: triggering thread labels
        // plus the matched threads' categories.
        var wanted = new HashSet<string>(match.TriggerThreadIds, StringComparer.Ordinal);
        var signalTerms = detail.PrePeakThreads
            .Where(t => t.ArticleIds.Any(id => wanted.Contains(id)))
            .SelectMany(t => t.LabelTerms.Concat(new[] { t.TopCategory ?? "" }))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var summaries = new List<HypeFilingSummary>();
        using var http = _httpFactory.CreateClient();
        http.Timeout = FetchTimeout;
        foreach (var filing in filings)
        {
            // Sequential by design (size discipline + SEC etiquette); each
            // filing summarized independently before combining.
            summaries.Add(await SummarizeOneAsync(http, symbol, match, filing, signalTerms, asOfDate, ct));
        }
        return summaries;
    }

    private async Task<HypeFilingSummary> SummarizeOneAsync(
        HttpClient http, string symbol, HypeSignalMatch match,
        HypeCaseFiling filing, IReadOnlyList<string> signalTerms,
        DateOnly asOfDate, CancellationToken ct)
    {
        var summary = new HypeFilingSummary
        {
            FormType = filing.FormType ?? "",
            FiledAt = filing.FiledAt,
        };
        string text;
        try
        {
            text = await FetchFilingTextAsync(http, filing.Url ?? "", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Filing fetch failed for {Form} {Url}", filing.FormType, filing.Url);
            summary.ConfidenceNote = "unavailable: document fetch failed";
            return summary;
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            summary.ConfidenceNote = "unavailable: empty document text";
            return summary;
        }
        summary.TotalPages = HypeFilingText.EstimatePages(text);

        string feeding;
        string note;
        if (summary.TotalPages < HypeFilingText.FullTextPages)
        {
            feeding = text.Length > HypeFilingText.FullTextChars
                ? text.Substring(0, HypeFilingText.FullTextChars) : text;
            summary.PagesProcessed = summary.TotalPages;
            note = "full";
        }
        else if (summary.TotalPages <= HypeFilingText.SectionPages)
        {
            feeding = RetrieveSections(text, signalTerms);
            summary.PagesProcessed = HypeFilingText.EstimatePages(feeding);
            note = $"partial: section retrieval ({summary.PagesProcessed}/{summary.TotalPages} pages by signal relevance)";
        }
        else
        {
            feeding = ExtractRelevant(text, signalTerms);
            summary.PagesProcessed = HypeFilingText.EstimatePages(feeding);
            note = $"partial: extractive first pass ({summary.PagesProcessed}/{summary.TotalPages} pages by signal relevance)";
        }

        try
        {
            var prompt = BuildFilingPrompt(symbol, match, filing, asOfDate, feeding);
            var brief = await _gemini.SummarizeClusterAsync(prompt, ct);
            if (brief is null)
            {
                summary.ConfidenceNote = "unavailable: model declined";
                return summary;
            }
            summary.Findings = brief.Summary ?? "";
            summary.Disclosures = string.Join(" ", brief.KeyPoints ?? Enumerable.Empty<string>());
            summary.ConfidenceNote = note;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Filing summary failed for {Form} {Url}", filing.FormType, filing.Url);
            summary.ConfidenceNote = "unavailable: summarization failed";
        }
        return summary;
    }

    private static string RetrieveSections(string text, IReadOnlyList<string> signalTerms)
    {
        var sections = HypeFilingText.SplitSections(text);
        var ranked = sections
            .Select((s, i) => (Section: s, Index: i,
                Score: HypeFilingText.ScoreText(s.Heading + "\n" + s.Body, signalTerms)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Index)
            .ToList();
        var sb = new StringBuilder();
        foreach (var (section, _, _) in ranked)
        {
            if (sb.Length + section.Body.Length + section.Heading.Length + 16 > HypeFilingText.FullTextChars)
                break;
            sb.AppendLine("## " + section.Heading);
            sb.AppendLine(section.Body);
            sb.AppendLine();
        }
        var result = sb.ToString().Trim();
        return result.Length == 0 ? text.Substring(0, Math.Min(text.Length, HypeFilingText.FullTextChars)) : result;
    }

    private static string ExtractRelevant(string text, IReadOnlyList<string> signalTerms)
    {
        var sentences = HypeFilingText.SplitSentences(text)
            .Select((s, i) => (Sentence: s, Index: i,
                Score: HypeFilingText.ScoreText(s, signalTerms)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Index)
            .Take(ExtractSentences)
            .OrderBy(x => x.Index)
            .Select(x => x.Sentence)
            .ToList();
        return sentences.Count == 0
            ? text.Substring(0, Math.Min(text.Length, HypeFilingText.FullTextChars))
            : string.Join(" ", sentences);
    }

    private static string BuildFilingPrompt(
        string symbol, HypeSignalMatch match, HypeCaseFiling filing,
        DateOnly asOfDate, string feeding)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are a historical research assistant. Today is {asOfDate:yyyy-MM-dd}.");
        sb.AppendLine($"You know NOTHING that happened after this date. Never use outside knowledge.");
        sb.AppendLine();
        sb.AppendLine($"Below is the text of one {filing.FormType} filed {filing.FiledAt:yyyy-MM-dd}, relevant to {symbol.Trim().ToUpperInvariant()} around signal '{match.Name}'.");
        sb.AppendLine($"Summarize ONLY what this document states about the signal topic.");
        sb.AppendLine();
        sb.AppendLine("Hard rules:");
        sb.AppendLine("- State only claims present in the document text below.");
        sb.AppendLine("- NEVER state or imply causation with any stock price move.");
        sb.AppendLine("- NEVER predict, advise, or recommend anything.");
        sb.AppendLine();
        sb.AppendLine("Respond with exactly these sections:");
        sb.AppendLine("FINDINGS: 2-3 sentences on what the filing discloses relevant to the signal.");
        sb.AppendLine("DISCLOSURES AND RISKS: material statements or risk factors mentioned, or 'none stated'.");
        sb.AppendLine();
        sb.AppendLine(feeding);
        return sb.ToString();
    }

    // Filing directory URL → index.json → primary document → stripped text.
    private async Task<string> FetchFilingTextAsync(HttpClient http, string directoryUrl, CancellationToken ct)
    {
        var dir = directoryUrl.TrimEnd('/') + "/";
        using var indexRequest = new HttpRequestMessage(HttpMethod.Get, dir + "index.json");
        ApplyUserAgent(indexRequest);
        using var indexResp = await http.SendAsync(indexRequest, ct);
        indexResp.EnsureSuccessStatusCode();
        var indexJson = await indexResp.Content.ReadAsStringAsync(ct);
        var docName = PickPrimaryDocument(indexJson)
            ?? throw new InvalidOperationException("No primary document in filing index.");
        using var docRequest = new HttpRequestMessage(HttpMethod.Get, dir + docName);
        ApplyUserAgent(docRequest);
        using var docResp = await http.SendAsync(docRequest, ct);
        docResp.EnsureSuccessStatusCode();
        var bytes = await docResp.Content.ReadAsByteArrayAsync(ct);
        var html = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, MaxDownloadChars));
        return HypeFilingText.StripHtml(html);
    }

    // Primary financial document: the .htm that is NOT an R-file, XML, XSD,
    // or Financial_Report companion. Same rule a human reader applies.
    internal static string? PickPrimaryDocument(string indexJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(indexJson);
            if (!doc.RootElement.TryGetProperty("directory", out var directory) ||
                !directory.TryGetProperty("item", out var items) ||
                items.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var item in items.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (name.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                {
                    var lower = name.ToLowerInvariant();
                    if (lower.Contains("financial_report") || lower.EndsWith(".xml") ||
                        System.Text.RegularExpressions.Regex.IsMatch(lower, @"r\d+\.htm$"))
                        continue;
                    return name;
                }
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void ApplyUserAgent(HttpRequestMessage request)
    {
        // SEC etiquette: contact User-Agent from config, never hardcoded
        // (mirrors the SecEdgar provider registration).
        var userAgent = _config["SecEdgar:UserAgent"] ?? "StockTimeMachine/1.0 (contact: your@email.com)";
        request.Headers.UserAgent.TryParseAdd(userAgent);
    }
}
