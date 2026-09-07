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
    private readonly IHistoricalDataRepository _dataRepo;
    private readonly ILogger<HypeFilingService> _logger;

    public HypeFilingService(
        IHttpClientFactory httpFactory,
        IGeminiClient gemini,
        IConfiguration config,
        IHistoricalDataRepository dataRepo,
        ILogger<HypeFilingService> logger)
    {
        _httpFactory = httpFactory;
        _gemini = gemini;
        _config = config;
        _dataRepo = dataRepo;
        _logger = logger;
    }

    public async Task<int> EnsureSummariesAsync(
        string symbol,
        IEnumerable<SecFiling> filings,
        DateOnly asOfDate,
        CancellationToken ct = default)
    {
        const int MaxPerRun = 5;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int generated = 0;
        foreach (var filing in (filings ?? Enumerable.Empty<SecFiling>())
                     .Where(f => f is not null && !string.IsNullOrWhiteSpace(f.AccessionNumber))
                     .OrderByDescending(f => f.FiledAt))
        {
            if (generated >= MaxPerRun)
                break;
            if (!seen.Add(filing.AccessionNumber))
                continue;
            try
            {
                if (await _dataRepo.GetFilingSummary(filing.AccessionNumber, ct) is not null)
                    continue;
                var record = await SummarizeStructuredAsync(
                    symbol, filing.AccessionNumber, filing.FormType,
                    filing.FiledAt, filing.Url ?? "", asOfDate, ct);
                if (record is not null)
                {
                    await _dataRepo.StoreFilingSummary(record, ct);
                    generated++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Filing summary generation failed for {Accession}; continuing",
                    filing.AccessionNumber);
            }
        }
        return generated;
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
        _logger.LogInformation("Filing text for {Form} {Url}: {Chars} chars, ~{Pages} pages",
            filing.FormType, filing.Url, text.Length, summary.TotalPages);

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

    public async Task<FilingSummaryRecord?> SummarizeStructuredAsync(
        string symbol,
        string accessionNumber,
        string formType,
        DateTime filedAt,
        string documentUrl,
        DateOnly asOfDate,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accessionNumber) || string.IsNullOrWhiteSpace(documentUrl))
            return null;
        if (!IsSupportedFamily(formType))
        {
            _logger.LogDebug("Skipping structured extraction for unsupported form {Form}", formType);
            return null;
        }
        if (!_gemini.IsEnabled)
            return null;
        string text;
        try
        {
            text = await FetchFilingTextAsync(
                _httpFactory.CreateClient(), documentUrl, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Structured filing fetch failed for {Accession}", accessionNumber);
            return null;
        }
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var totalPages = HypeFilingText.EstimatePages(text);
        var feeding = SizeForModel(text, totalPages, Array.Empty<string>());
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text)))
            .ToLowerInvariant();
        try
        {
            var prompt = BuildStructuredPrompt(symbol, formType, filedAt, asOfDate, feeding);
            var json = await _gemini.GenerateJsonAsync(prompt, ct);
            if (string.IsNullOrWhiteSpace(json))
                return null;
            return ParseStructured(accessionNumber, formType, json, hash);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Structured filing parse failed for {Accession}", accessionNumber);
            return null;
        }
    }

    private static bool IsSupportedFamily(string? formType)
    {
        var form = (formType ?? "").Trim().ToUpperInvariant();
        return form.StartsWith("8-K", StringComparison.Ordinal) ||
            form.StartsWith("10-Q", StringComparison.Ordinal) ||
            form.StartsWith("10-K", StringComparison.Ordinal);
    }

    private static bool IsQuarterlyFamily(string? formType)
    {
        var form = (formType ?? "").Trim().ToUpperInvariant();
        return form.StartsWith("10-Q", StringComparison.Ordinal) ||
            form.StartsWith("10-K", StringComparison.Ordinal);
    }

    // Shared fetch+size path (reason: prose and structured extraction must
    // see identical text). Returns null-text marker via empty string.
    private static string SizeForModel(string text, int totalPages, IReadOnlyList<string> signalTerms)
    {
        if (totalPages < HypeFilingText.FullTextPages)
            return text.Length > HypeFilingText.FullTextChars
                ? text.Substring(0, HypeFilingText.FullTextChars) : text;
        if (totalPages <= HypeFilingText.SectionPages)
            return RetrieveSections(text, signalTerms);
        return ExtractRelevant(text, signalTerms);
    }

    private static string BuildStructuredPrompt(
        string symbol, string formType, DateTime filedAt, DateOnly asOfDate, string feeding)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are a historical research assistant. Today is {asOfDate:yyyy-MM-dd}.");
        sb.AppendLine($"Classify the {formType} below (filed {filedAt:yyyy-MM-dd}, relevant to {symbol.Trim().ToUpperInvariant()}).");
        sb.AppendLine("Respond with JSON only (no markdown), exactly these keys:");
        if (IsQuarterlyFamily(formType))
        {
            sb.AppendLine("{ \"financial_direction\": \"improving|deteriorating|stable\",");
            sb.AppendLine("  \"key_risk_flags\": [\"...\", ...] (risk categories present, empty array if none),");
        }
        else
        {
            sb.AppendLine("{ \"event_type\": \"management_change|acquisition|earnings_warning|regulatory|other\",");
            sb.AppendLine("  \"primary_topic\": \"one sentence, plain language\",");
        }
        sb.AppendLine("  \"market_relevance\": \"high|medium|low\",");
        sb.AppendLine("  \"sentiment\": \"positive|negative|neutral\",");
        sb.AppendLine("  \"findings\": \"2-3 sentences: what occurred, who was involved, when (plain language, no jargon)\",");
        sb.AppendLine("  \"disclosures\": \"material statements or risks, or 'none stated'\" }");
        sb.AppendLine();
        sb.AppendLine(feeding);
        return sb.ToString();
    }

    // Defensive parse: whitelists enums, clamps prose, drops anything else.
    // Returns null when required keys are absent (never a half-record).
    // Public for unit tests (pure deterministic helper, same as PickPrimaryDocument).
    public static FilingSummaryRecord? ParseStructured(
        string accessionNumber, string formType, string json, string contentHash)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string Str(string key, int max)
            {
                var s = root.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";
                s = s.Trim();
                return s.Length > max ? s.Substring(0, max) : s;
            }
            var record = new FilingSummaryRecord
            {
                AccessionNumber = accessionNumber,
                FormType = formType,
                ContentHash = contentHash,
                Findings = Str("findings", 2000),
                Disclosures = Str("disclosures", 2000),
                ConfidenceNote = "full",
            };
            var relevance = Str("market_relevance", 16).ToLowerInvariant();
            if (relevance != FilingRelevance.High && relevance != FilingRelevance.Medium)
                relevance = FilingRelevance.Low;
            var sentiment = Str("sentiment", 16).ToLowerInvariant();
            if (sentiment != FilingSentiment.Positive && sentiment != FilingSentiment.Negative)
                sentiment = FilingSentiment.Neutral;
            if (IsQuarterlyFamily(formType))
            {
                var direction = Str("financial_direction", 16).ToLowerInvariant();
                if (direction != "improving" && direction != "deteriorating")
                    direction = "stable";
                var risks = new List<string>();
                if (root.TryGetProperty("key_risk_flags", out var arr) &&
                    arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in arr.EnumerateArray().Take(10))
                    {
                        var r = (e.GetString() ?? "").Trim();
                        if (r.Length > 0 && r.Length <= 64)
                            risks.Add(r);
                    }
                }
                if (string.IsNullOrWhiteSpace(record.Findings))
                    return null;
                record.StructuredJson = JsonSerializer.Serialize(new
                {
                    financial_direction = direction,
                    key_risk_flags = risks,
                    market_relevance = relevance,
                    sentiment,
                });
            }
            else
            {
                var evt = Str("event_type", 32).ToLowerInvariant();
                if (!FilingEventTypes.All.Contains(evt))
                    evt = FilingEventTypes.Other;
                var topic = Str("primary_topic", 500);
                if (string.IsNullOrWhiteSpace(topic) || string.IsNullOrWhiteSpace(record.Findings))
                    return null;
                record.StructuredJson = JsonSerializer.Serialize(new
                {
                    event_type = evt,
                    primary_topic = topic,
                    market_relevance = relevance,
                    sentiment,
                });
            }
            return record;
        }
        catch (JsonException)
        {
            return null;
        }
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

    // Primary financial document: the main .htm — never the filing NAV pages
    // ({accession}-index.html / -index-headers.html sort first in the listing
    // and previously passed every filter, so briefs narrated nav-link soup
    // instead of disclosures; Phase-1 root cause), never R-files, XML/XSD
    // companions, Financial_Report, or static assets. Fallback: the complete
    // submission .txt (full text). Same rule a human reader applies.
    // Directory URL (.../data/{cik}/{18-digit}/) → canonical accession
    // (10-2-6 dashed). Rescues legacy case rows frozen before accession
    // numbers were stored. Pure; unit-tested.
    public static string DeriveAccessionNumber(string? directoryUrl)
    {
        if (string.IsNullOrWhiteSpace(directoryUrl))
            return "";
        var segments = directoryUrl.Trim().TrimEnd('/').Split('/');
        var digits = new string(segments.Last().Where(char.IsDigit).ToArray());
        if (digits.Length != 18)
            return "";
        return $"{digits.Substring(0, 10)}-{digits.Substring(10, 2)}-{digits.Substring(12, 6)}";
    }

    // Public for unit tests (pure deterministic helper, same as HypeFilingText).
    public static string? PickPrimaryDocument(string indexJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(indexJson);
            if (!doc.RootElement.TryGetProperty("directory", out var directory) ||
                !directory.TryGetProperty("item", out var items) ||
                items.ValueKind != JsonValueKind.Array)
                return null;
            string? fallbackTxt = null;
            foreach (var item in items.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var lower = name.ToLowerInvariant();
                if (lower.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) &&
                    !lower.Contains("index"))
                    fallbackTxt ??= name;
                if (lower.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) ||
                    lower.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                {
                    if (lower.Contains("index.html") || lower.Contains("index-headers") ||
                        lower.Contains("financial_report") || lower.EndsWith(".xml") ||
                        System.Text.RegularExpressions.Regex.IsMatch(lower, @"r\d+\.htm(l)?$"))
                        continue;
                    return name;
                }
            }
            return fallbackTxt;
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
