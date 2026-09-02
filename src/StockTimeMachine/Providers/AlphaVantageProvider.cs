using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StockTimeMachine.Entities;

namespace StockTimeMachine.Providers;

public class AlphaVantageProvider : IAlphaVantageProvider
{
    private readonly HttpClient _http;
    private readonly ILogger<AlphaVantageProvider> _logger;
    private readonly string _apiKey;

    public AlphaVantageProvider(HttpClient http, ILogger<AlphaVantageProvider> logger, IConfiguration config)
    {
        _http = http;
        _logger = logger;
        _apiKey = config["AlphaVantage:ApiKey"] ?? "";
    }

    public async Task<IReadOnlyList<PricePoint>> GetDailyPrices(string symbol, DateOnly? asOfDate = null, int days = 365, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogWarning("Alpha Vantage API key not configured");
            return Array.Empty<PricePoint>();
        }

        var url = $"https://www.alphavantage.co/query?function=TIME_SERIES_DAILY&symbol={symbol}&apikey={_apiKey}&outputsize=compact";
        _logger.LogInformation("Fetching daily prices from Alpha Vantage for {Symbol}", symbol);

        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("Time Series (Daily)", out var timeSeries))
        {
            _logger.LogWarning("No price data returned for {Symbol}", symbol);
            return Array.Empty<PricePoint>();
        }

        var cutoff = asOfDate?.ToString("yyyy-MM-dd");
        var result = new List<PricePoint>();

        foreach (var day in timeSeries.EnumerateObject())
        {
            var dateStr = day.Name;

            if (cutoff != null && string.Compare(dateStr, cutoff, StringComparison.Ordinal) > 0)
                continue;

            if (!DateOnly.TryParse(dateStr, out var date))
                continue;

            var data = day.Value;
            result.Add(new PricePoint
            {
                CompanySymbol = symbol.ToUpper(),
                Date = date,
                Open = decimal.Parse(data.GetProperty("1. open").GetString()!),
                High = decimal.Parse(data.GetProperty("2. high").GetString()!),
                Low = decimal.Parse(data.GetProperty("3. low").GetString()!),
                Close = decimal.Parse(data.GetProperty("4. close").GetString()!),
                Volume = long.Parse(data.GetProperty("5. volume").GetString()!)
            });
        }

        result.Sort((a, b) => b.Date.CompareTo(a.Date));

        if (result.Count > days)
            result.RemoveRange(days, result.Count - days);

        return result;
    }
}
