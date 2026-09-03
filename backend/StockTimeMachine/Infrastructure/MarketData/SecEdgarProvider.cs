using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

public class SecEdgarProvider : ISecEdgarProvider
{
    private readonly HttpClient _http;
    private readonly ILogger<SecEdgarProvider> _logger;
    private readonly string _baseUrl;

    public SecEdgarProvider(HttpClient http, ILogger<SecEdgarProvider> logger, IConfiguration config)
    {
        _http = http;
        _logger = logger;
        _baseUrl = (config["SecEdgar:BaseUrl"] ?? "https://data.sec.gov").TrimEnd('/');
        // User-Agent is configured on the HttpClient (Program.cs, from
        // SecEdgar:UserAgent). SEC requires a contact address; never hardcode one here.
    }

    public async Task<IReadOnlyList<SecFiling>> GetCompanyFilings(string cik, DateOnly? asOfDate = null, CancellationToken ct = default)
    {
        var normalizedCik = NormalizeCik(cik);
        var url = $"{_baseUrl}/submissions/CIK{normalizedCik}.json";

        _logger.LogInformation("Fetching SEC filings for CIK {Cik}", normalizedCik);

        string json;
        try
        {
            using var response = await _http.GetAsync(url, ct);
            if ((int)response.StatusCode == 429)
                throw new RateLimitExceededException("SEC EDGAR rate limit exceeded.");
            response.EnsureSuccessStatusCode();
            json = await response.Content.ReadAsStringAsync(ct);
        }
        catch (RateLimitExceededException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex)
        {
            throw new ExternalProviderException("SEC EDGAR request failed.", ex);
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new ExternalProviderException("SEC EDGAR returned an unreadable response.", ex);
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("filings", out var filings) ||
                !filings.TryGetProperty("recent", out var recent))
                return Array.Empty<SecFiling>();

            if (!recent.TryGetProperty("form", out var forms) || forms.ValueKind != JsonValueKind.Array)
                return Array.Empty<SecFiling>();

            recent.TryGetProperty("filingDate", out var dates);
            recent.TryGetProperty("accessionNumber", out var accessions);
            recent.TryGetProperty("periodOfReport", out var periods);

            var cutoff = asOfDate.HasValue ? TemporalBoundary.GetCutoffUtc(asOfDate.Value) : (DateTime?)null;
            var count = forms.GetArrayLength();
            var result = new List<SecFiling>(Math.Min(count, 256));

            for (int i = 0; i < count; i++)
            {
                var formType = forms[i].GetString() ?? "";
                var filingDate = dates.ValueKind == JsonValueKind.Array && i < dates.GetArrayLength()
                    ? dates[i].GetString() ?? ""
                    : "";

                if (string.IsNullOrEmpty(filingDate) || string.IsNullOrEmpty(formType))
                    continue;

                if (formType is not ("10-K" or "10-Q" or "8-K" or "10-K/A" or "10-Q/A" or "8-K/A"))
                    continue;

                // Eligibility timestamp is the SEC acceptance (filing) date, never the
                // period covered. Filing dates are calendar dates; midnight UTC is the
                // earliest instant of that day, so any end-of-day cutoff on/after the
                // filing date includes it.
                if (!DateOnly.TryParseExact(filingDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var filedDate))
                    continue;

                var filedAtUtc = new DateTime(filedDate.Year, filedDate.Month, filedDate.Day, 0, 0, 0, DateTimeKind.Utc);
                if (cutoff.HasValue && filedAtUtc > cutoff.Value)
                    continue;

                var accession = accessions.ValueKind == JsonValueKind.Array && i < accessions.GetArrayLength()
                    ? accessions[i].GetString() ?? ""
                    : "";
                var cleanAcc = accession.Replace("-", "", StringComparison.Ordinal);
                var period = periods.ValueKind == JsonValueKind.Array && i < periods.GetArrayLength()
                    ? periods[i].GetString() ?? ""
                    : "";

                result.Add(new SecFiling
                {
                    FormType = formType,
                    FiledAt = filedAtUtc,
                    PeriodOfReport = DateOnly.TryParseExact(period, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var periodDate)
                        ? new DateTime(periodDate.Year, periodDate.Month, periodDate.Day, 0, 0, 0, DateTimeKind.Utc)
                        : DateTime.MinValue,
                    AccessionNumber = accession,
                    CompanySymbol = "",
                    Url = $"https://www.sec.gov/Archives/edgar/data/{normalizedCik.TrimStart('0')}/{cleanAcc}/"
                });
            }

            return result;
        }
    }

    public async Task<Company?> GetCompanyProfile(string cik, CancellationToken ct = default)
    {
        var normalizedCik = NormalizeCik(cik);
        var url = $"{_baseUrl}/submissions/CIK{normalizedCik}.json";

        string json;
        try
        {
            using var response = await _http.GetAsync(url, ct);
            if ((int)response.StatusCode == 429)
                throw new RateLimitExceededException("SEC EDGAR rate limit exceeded.");
            response.EnsureSuccessStatusCode();
            json = await response.Content.ReadAsStringAsync(ct);
        }
        catch (RateLimitExceededException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex)
        {
            throw new ExternalProviderException("SEC EDGAR request failed.", ex);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            // Multi-class issuers share one CIK; the first ticker is a best-effort pick.
            var ticker = doc.RootElement.TryGetProperty("tickers", out var t) && t.ValueKind == JsonValueKind.Array && t.GetArrayLength() > 0
                ? t[0].GetString() ?? ""
                : "";
            var exchange = doc.RootElement.TryGetProperty("exchanges", out var e) && e.ValueKind == JsonValueKind.Array && e.GetArrayLength() > 0
                ? e[0].GetString() ?? ""
                : "";
            var sector = doc.RootElement.TryGetProperty("sicDescription", out var s) ? s.GetString() ?? "" : "";

            if (string.IsNullOrEmpty(ticker))
                return null;

            return new Company
            {
                Symbol = ticker.ToUpperInvariant(),
                Name = name,
                Cik = normalizedCik,
                Exchange = exchange,
                Sector = sector,
                Industry = ""
            };
        }
        catch (JsonException ex)
        {
            throw new ExternalProviderException("SEC EDGAR returned an unreadable response.", ex);
        }
    }

    private static string NormalizeCik(string cik) =>
        new string(cik.Where(char.IsDigit).ToArray()).PadLeft(10, '0');
}
