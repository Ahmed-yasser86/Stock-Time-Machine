
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RepositoryContracts;
using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SerilogTimings;
using ServiceContractsContacts;

namespace Repositories_Stocks
{
    


        public class FinnhubRepository : IFinnhubRepository
        {
            private readonly HttpClient _httpClient;
            private readonly IOptions<TradingOptions> _tradingOps;
            private readonly ILogger<FinnhubRepository> _logger;

            public FinnhubRepository(HttpClient httpClient, IOptions<TradingOptions> tradingOps, ILogger<FinnhubRepository> logger)
            {
                _httpClient = httpClient;
                _tradingOps = tradingOps;
                _logger = logger;
            }

            public async Task<Dictionary<string, object>?> GetCompanyProfile(string stockSymbol, CancellationToken cancellationToken = default)
            {
                using (Operation.Time("Finnhub GetCompanyProfile API call for symbol: {StockSymbol}", stockSymbol))
                {
                    _logger.LogInformation("Executing {MethodName} method at {Timestamp}. Stock Symbol: {StockSymbol}",
                        nameof(GetCompanyProfile), DateTime.UtcNow, stockSymbol);

                    try
                    {
                        if (string.IsNullOrEmpty(stockSymbol))
                        {
                            _logger.LogWarning("GetCompanyProfile called with null or empty stock symbol");
                            throw new ArgumentNullException(nameof(stockSymbol));
                        }

                        var token = _tradingOps.Value.FinnhubToken;
                        var url = $"https://finnhub.io/api/v1/stock/profile2?symbol={stockSymbol}&token={token}";

                        _logger.LogDebug("Making HTTP GET request to Finnhub API. URL: {Url}", url.Replace(token, "***HIDDEN***"));

                        var response = await _httpClient.GetAsync(url, cancellationToken);

                        _logger.LogDebug("Finnhub API response received. Status Code: {StatusCode}, Symbol: {StockSymbol}",
                            response.StatusCode, stockSymbol);

                        response.EnsureSuccessStatusCode();

                        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                        _logger.LogDebug("Raw JSON response for {StockSymbol}: {JsonResponse}",
                            stockSymbol, responseBody.Length > 500 ? responseBody.Substring(0, 500) + "..." : responseBody);

                        var responseDictionary = JsonSerializer.Deserialize<Dictionary<string, object>>(responseBody);

                        if (responseDictionary == null || responseDictionary.Count == 0)
                        {
                            _logger.LogInformation("No company profile data found for symbol: {StockSymbol}", stockSymbol);
                            return null;
                        }

                        _logger.LogInformation("Successfully retrieved company profile for symbol: {StockSymbol}. Data fields: {FieldCount}",
                            stockSymbol, responseDictionary.Count);

                        return responseDictionary;
                    }
                    catch (HttpRequestException ex)
                    {
                        _logger.LogError(ex, "HTTP request error in {MethodName} for symbol: {StockSymbol}. Error: {ErrorMessage}",
                            nameof(GetCompanyProfile), stockSymbol, ex.Message);
                        throw;
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, "JSON deserialization error in {MethodName} for symbol: {StockSymbol}. Error: {ErrorMessage}",
                            nameof(GetCompanyProfile), stockSymbol, ex.Message);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unexpected error occurred in {MethodName} for symbol: {StockSymbol}",
                            nameof(GetCompanyProfile), stockSymbol);
                        throw;
                    }
                }
            }

            public async Task<Dictionary<string, object>?> GetStockPriceQuote(string stockSymbol, CancellationToken cancellationToken = default)
            {
                using (Operation.Time("Finnhub GetStockPriceQuote API call for symbol: {StockSymbol}", stockSymbol))
                {
                    _logger.LogInformation("Executing {MethodName} method at {Timestamp}. Stock Symbol: {StockSymbol}",
                        nameof(GetStockPriceQuote), DateTime.UtcNow, stockSymbol);

                    try
                    {
                        if (string.IsNullOrEmpty(stockSymbol))
                        {
                            _logger.LogWarning("GetStockPriceQuote called with null or empty stock symbol");
                            throw new ArgumentNullException(nameof(stockSymbol));
                        }

                        var token = _tradingOps.Value.FinnhubToken;
                        var url = $"https://finnhub.io/api/v1/quote?symbol={stockSymbol}&token={token}";

                        _logger.LogDebug("Making HTTP GET request to Finnhub API. URL: {Url}", url.Replace(token, "***HIDDEN***"));

                        var response = await _httpClient.GetAsync(url, cancellationToken);

                        _logger.LogDebug("Finnhub API response received. Status Code: {StatusCode}, Symbol: {StockSymbol}",
                            response.StatusCode, stockSymbol);

                        response.EnsureSuccessStatusCode();

                        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                        _logger.LogDebug("Raw JSON quote response for {StockSymbol}: {JsonResponse}",
                            stockSymbol, responseBody.Length > 500 ? responseBody.Substring(0, 500) + "..." : responseBody);

                        var responseDictionary = JsonSerializer.Deserialize<Dictionary<string, object>>(responseBody);

                        if (responseDictionary == null || responseDictionary.Count == 0)
                        {
                            _logger.LogInformation("No stock price quote data found for symbol: {StockSymbol}", stockSymbol);
                            return null;
                        }

                        // Check if current price is zero (invalid stock or market closed)
                        if (responseDictionary.ContainsKey("c") && Convert.ToDouble(responseDictionary["c"].ToString()) == 0)
                        {
                            _logger.LogWarning("Current price is zero for symbol: {StockSymbol}. This may indicate an invalid stock symbol or market closed condition.",
                                stockSymbol);
                            return null;
                        }

                        _logger.LogInformation("Successfully retrieved stock price quote for symbol: {StockSymbol}. Current Price: {CurrentPrice}, Change: {Change}",
                            stockSymbol,
                            responseDictionary.ContainsKey("c") ? responseDictionary["c"] : "N/A",
                            responseDictionary.ContainsKey("d") ? responseDictionary["d"] : "N/A");

                        return responseDictionary;
                    }
                    catch (HttpRequestException ex)
                    {
                        _logger.LogError(ex, "HTTP request error in {MethodName} for symbol: {StockSymbol}. Error: {ErrorMessage}",
                            nameof(GetStockPriceQuote), stockSymbol, ex.Message);
                        throw;
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, "JSON deserialization error in {MethodName} for symbol: {StockSymbol}. Error: {ErrorMessage}",
                            nameof(GetStockPriceQuote), stockSymbol, ex.Message);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unexpected error occurred in {MethodName} for symbol: {StockSymbol}",
                            nameof(GetStockPriceQuote), stockSymbol);
                        throw;
                    }
                }
            }
        }
    }

