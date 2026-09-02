using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using StocksApp2.Areas.TimeMachine.Models;

namespace StocksApp2.Areas.TimeMachine.Controllers;

[Area("TimeMachine")]
public class TimeMachineController : Controller
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TimeMachineController> _logger;

    public TimeMachineController(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<TimeMachineController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index()
    {
        var model = new TimeMachineViewModel();
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(TimeMachineViewModel model)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "StockTimeMachine/1.0 (research@example.com)");

            await FetchPrice(client, model);
            await FetchFilings(client, model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching data for {Symbol} on {Date}", model.Symbol, model.SnapshotDate);
            model.Error = $"Failed to fetch data: {ex.Message}";
        }

        return View(model);
    }

    private async Task FetchPrice(HttpClient client, TimeMachineViewModel model)
    {
        var apiKey = _configuration["AlphaVantage:ApiKey"] ?? _configuration["TradingOptions:AlphaVantageKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            model.Error = "Alpha Vantage API key not configured. Set AlphaVantage:ApiKey in appsettings.";
            return;
        }

        var url = $"https://www.alphavantage.co/query?function=TIME_SERIES_DAILY&symbol={model.Symbol}&apikey={apiKey}&outputsize=compact";
        _logger.LogInformation("Fetching price from Alpha Vantage for {Symbol}", model.Symbol);

        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("Time Series (Daily)", out var timeSeries))
        {
            var note = doc.RootElement.TryGetProperty("Note", out var noteEl) ? noteEl.GetString() : null;
            model.Error = note ?? "No price data returned from Alpha Vantage.";
            return;
        }

        var snapshotDateStr = model.SnapshotDate.ToString("yyyy-MM-dd");
        var nextDayStr = model.SnapshotDate.AddDays(1).ToString("yyyy-MM-dd");

        if (timeSeries.TryGetProperty(snapshotDateStr, out var dayData) &&
            dayData.TryGetProperty("4. close", out var closeEl))
        {
            model.Price = decimal.Parse(closeEl.GetString()!);
        }

        if (timeSeries.TryGetProperty(nextDayStr, out var nextDayData) &&
            nextDayData.TryGetProperty("4. close", out var nextCloseEl))
        {
            model.NextDayPrice = decimal.Parse(nextCloseEl.GetString()!);
        }
    }

    private async Task FetchFilings(HttpClient client, TimeMachineViewModel model)
    {
        var cik = model.Symbol.ToUpper() switch
        {
            "TSLA" => "0001318605",
            "AAPL" => "0000320193",
            "MSFT" => "0000789019",
            "GOOGL" => "0001652044",
            "AMZN" => "0001018724",
            _ => null
        };

        if (cik is null)
        {
            _logger.LogWarning("No CIK mapping for {Symbol}", model.Symbol);
            return;
        }

        var paddedCik = cik.PadLeft(10, '0');
        var url = $"https://data.sec.gov/submissions/CIK{paddedCik}.json";
        _logger.LogInformation("Fetching filings from SEC EDGAR for CIK {Cik}", cik);

        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("filings", out var filings) ||
            !filings.TryGetProperty("recent", out var recent))
        {
            return;
        }

        var forms = recent.TryGetProperty("form", out var formsArr) ? formsArr : default;
        var dates = recent.TryGetProperty("filingDate", out var datesArr) ? datesArr : default;
        var accessions = recent.TryGetProperty("accessionNumber", out var accArr) ? accArr : default;

        if (forms.ValueKind == JsonValueKind.Undefined)
            return;

        var count = forms.GetArrayLength();
        var cutoff = model.SnapshotDate.ToString("yyyy-MM-dd");

        for (int i = 0; i < count && model.Filings.Count < 10; i++)
        {
            var formType = forms[i].GetString() ?? "";
            var filingDate = dates[i].GetString() ?? "";

            if (string.IsNullOrEmpty(filingDate) || string.IsNullOrEmpty(formType))
                continue;

            if (String.Compare(filingDate, cutoff, StringComparison.Ordinal) > 0)
                continue;

            if (formType is not ("10-K" or "10-Q" or "8-K" or "10-K/A" or "10-Q/A" or "8-K/A"))
                continue;

            var accession = accessions[i].GetString() ?? "";
            var cleanAcc = accession.Replace("-", "");
            var primaryDoc = recent.TryGetProperty("primaryDocument", out var primDoc)
                ? primDoc[i].GetString() ?? ""
                : "";

            model.Filings.Add(new FilingInfo
            {
                FormType = formType,
                FiledAt = DateTime.Parse(filingDate),
                Url = $"https://www.sec.gov/Archives/edgar/data/{cik}/{cleanAcc}/{primaryDoc}"
            });
        }
    }
}
