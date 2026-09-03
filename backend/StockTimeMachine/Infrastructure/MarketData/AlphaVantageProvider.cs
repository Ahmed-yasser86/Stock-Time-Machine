using System.Globalization;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

public class AlphaVantageProvider : IAlphaVantageProvider
{
    private readonly HttpClient _http;
    private readonly ILogger<AlphaVantageProvider> _logger;
    private readonly string _apiKey;
    private readonly string _baseUrl;

    public AlphaVantageProvider(HttpClient http, ILogger<AlphaVantageProvider> logger, IConfiguration config)
    {
        _http = http;
        _logger = logger;
        // Server-side only. Never logged, never returned in API responses.
        _apiKey = config["AlphaVantage:ApiKey"] ?? "";
        _baseUrl = config["AlphaVantage:BaseUrl"] ?? "https://www.alphavantage.co/query";
    }

    public async Task<IReadOnlyList<PricePoint>> GetDailyPrices(string symbol, DateOnly? asOfDate = null, int days = 365, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogWarning("Alpha Vantage API key not configured");
            return Array.Empty<PricePoint>();
        }

        // Free tier: 25 req/day. compact (~100 rows) suffices for small windows;
        // full history is only requested when the caller genuinely needs more.
        var outputSize = days > 100 ? "full" : "compact";
        var url = $"{_baseUrl}?function=TIME_SERIES_DAILY&symbol={Uri.EscapeDataString(symbol)}&outputsize={outputSize}&apikey={_apiKey}";
        _logger.LogInformation("Fetching daily prices from Alpha Vantage for {Symbol} ({OutputSize})", symbol, outputSize);

        string json;
        try
        {
            using var response = await _http.GetAsync(url, ct);
            if ((int)response.StatusCode == 429)
                throw new RateLimitExceededException("Alpha Vantage rate limit exceeded.");
            response.EnsureSuccessStatusCode();
            json = await response.Content.ReadAsStringAsync(ct);
        }
        catch (RateLimitExceededException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex)
        {
            throw new ExternalProviderException("Alpha Vantage request failed.", ex);
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new ExternalProviderException("Alpha Vantage returned an unreadable response.", ex);
        }

        using (doc)
        {
            if (doc.RootElement.TryGetProperty("Information", out var info))
            {
                var message = info.GetString() ?? "";
                _logger.LogWarning("Alpha Vantage information message for {Symbol}: {Info}", symbol, message);
                if (message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("calls per", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("premium", StringComparison.OrdinalIgnoreCase))
                    throw new RateLimitExceededException("Alpha Vantage rate limit exceeded.");
                return Array.Empty<PricePoint>();
            }

            if (doc.RootElement.TryGetProperty("Error Message", out var err))
                throw new ExternalProviderException($"Alpha Vantage error: {err.GetString()}");

            if (!doc.RootElement.TryGetProperty("Time Series (Daily)", out var timeSeries))
            {
                var keys = string.Join(", ", doc.RootElement.EnumerateObject().Select(p => p.Name));
                _logger.LogWarning("No price data returned for {Symbol}. Response keys: {Keys}", symbol, keys);
                return Array.Empty<PricePoint>();
            }

            var normalizedSymbol = symbol.ToUpperInvariant();
            var result = new List<PricePoint>();

            foreach (var day in timeSeries.EnumerateObject())
            {
                if (!DateOnly.TryParseExact(day.Name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    continue;

                // Temporal rule: a daily bar dated D is knowable at end-of-day D,
                // which is inside the cutoff for selected date D. Same-day bars after
                // the selected date are excluded.
                if (asOfDate.HasValue && date > asOfDate.Value)
                    continue;

                var data = day.Value;
                if (!TryParsePrice(data, "1. open", out var open) ||
                    !TryParsePrice(data, "2. high", out var high) ||
                    !TryParsePrice(data, "3. low", out var low) ||
                    !TryParsePrice(data, "4. close", out var close) ||
                    !TryParseVolume(data, out var volume))
                {
                    _logger.LogWarning("Skipping malformed price row for {Symbol} on {Date}", symbol, day.Name);
                    continue;
                }

                result.Add(new PricePoint
                {
                    CompanySymbol = normalizedSymbol,
                    Date = date,
                    Open = open,
                    High = high,
                    Low = low,
                    Close = close,
                    Volume = volume
                });
            }

            result.Sort((a, b) => b.Date.CompareTo(a.Date));

            if (result.Count > days)
                result.RemoveRange(days, result.Count - days);

            return result;
        }
    }

    private static bool TryParsePrice(JsonElement data, string property, out decimal value)
    {
        value = 0;
        return data.TryGetProperty(property, out var el) &&
               decimal.TryParse(el.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseVolume(JsonElement data, out long value)
    {
        value = 0;
        return data.TryGetProperty("5. volume", out var el) &&
               long.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
