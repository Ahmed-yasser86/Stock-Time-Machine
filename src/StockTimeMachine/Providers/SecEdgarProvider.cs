using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StockTimeMachine.Entities;

namespace StockTimeMachine.Providers;

public class SecEdgarProvider : ISecEdgarProvider
{
    private readonly HttpClient _http;
    private readonly ILogger<SecEdgarProvider> _logger;

    public SecEdgarProvider(HttpClient http, ILogger<SecEdgarProvider> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SecFiling>> GetCompanyFilings(string cik, DateOnly? asOfDate = null, CancellationToken ct = default)
    {
        var paddedCik = cik.PadLeft(10, '0');
        var url = $"https://data.sec.gov/submissions/CIK{paddedCik}.json";

        _logger.LogInformation("Fetching SEC filings for CIK {Cik}", cik);

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "StockTimeMachine/1.0 (research@example.com)");
        request.Headers.Add("Accept", "application/json");

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("filings", out var filings) ||
            !filings.TryGetProperty("recent", out var recent))
            return Array.Empty<SecFiling>();

        var forms = recent.TryGetProperty("form", out var f) ? f : default;
        var dates = recent.TryGetProperty("filingDate", out var d) ? d : default;
        var accessions = recent.TryGetProperty("accessionNumber", out var a) ? a : default;
        var periods = recent.TryGetProperty("periodOfReport", out var p) ? p : default;

        if (forms.ValueKind == JsonValueKind.Undefined)
            return Array.Empty<SecFiling>();

        var count = forms.GetArrayLength();
        var result = new List<SecFiling>();
        var cutoff = asOfDate?.ToString("yyyy-MM-dd");

        for (int i = 0; i < count; i++)
        {
            var formType = forms[i].GetString() ?? "";
            var filingDate = dates[i].GetString() ?? "";

            if (string.IsNullOrEmpty(filingDate) || string.IsNullOrEmpty(formType))
                continue;

            if (cutoff != null && string.Compare(filingDate, cutoff, StringComparison.Ordinal) > 0)
                continue;

            if (formType is not ("10-K" or "10-Q" or "8-K" or "10-K/A" or "10-Q/A" or "8-K/A"))
                continue;

            var accession = accessions[i].GetString() ?? "";
            var cleanAcc = accession.Replace("-", "");
            var period = periods.ValueKind != JsonValueKind.Undefined && i < periods.GetArrayLength()
                ? periods[i].GetString() ?? ""
                : "";

            result.Add(new SecFiling
            {
                FormType = formType,
                FiledAt = DateTime.SpecifyKind(DateTime.Parse(filingDate), DateTimeKind.Utc),
                PeriodOfReport = string.IsNullOrEmpty(period) ? DateTime.MinValue : DateTime.SpecifyKind(DateTime.Parse(period), DateTimeKind.Utc),
                AccessionNumber = accession,
                CompanySymbol = "",
                Url = $"https://www.sec.gov/Archives/edgar/data/{cik}/{cleanAcc}/"
            });
        }

        return result;
    }

    public async Task<Company?> GetCompanyProfile(string cik, CancellationToken ct = default)
    {
        var paddedCik = cik.PadLeft(10, '0');
        var url = $"https://data.sec.gov/submissions/CIK{paddedCik}.json";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "StockTimeMachine/1.0 (research@example.com)");

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);

        var name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var ticker = doc.RootElement.TryGetProperty("tickers", out var t)
            ? t.GetArrayLength() > 0 ? t[0].GetString() ?? "" : ""
            : "";
        var exchange = doc.RootElement.TryGetProperty("exchanges", out var e)
            ? e.GetArrayLength() > 0 ? e[0].GetString() ?? "" : ""
            : "";
        var sector = doc.RootElement.TryGetProperty("sicDescription", out var s) ? s.GetString() ?? "" : "";

        return new Company
        {
            Symbol = ticker,
            Name = name,
            Cik = cik,
            Exchange = exchange,
            Sector = sector,
            Industry = ""
        };
    }
}
