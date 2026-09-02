using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ServiceContracts;
using System.Net.Http;
using System.Text.Json;
using ServiceContractsContacts;
namespace Services
{
    public class FinnhubService : IFinnhubService
    {
        private readonly HttpClient _httpClient;
        private readonly IOptions<TradingOptions> _tradingOps;
        public FinnhubService(HttpClient httpClient, IOptions<TradingOptions> TradOps)
        {
            _httpClient = httpClient;
            _tradingOps = TradOps;
        }

        public async Task<Dictionary<string, object>?> GetCompanyProfile(string stockSymbol, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(stockSymbol))
                throw new ArgumentNullException(nameof(stockSymbol));

            var token = _tradingOps.Value.FinnhubToken;

            var response = await _httpClient.GetAsync(
                $"https://finnhub.io/api/v1/stock/profile2?symbol={stockSymbol}&token={token}",
                cancellationToken);

            response.EnsureSuccessStatusCode();

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            var responseDictionary = JsonSerializer.Deserialize<Dictionary<string, object>>(responseBody);

            return (responseDictionary == null || responseDictionary.Count == 0) ? null : responseDictionary;
        }


        public async Task<Dictionary<string, object>?> GetStockPriceQuote(string stockSymbol, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(stockSymbol))
                throw new ArgumentNullException(nameof(stockSymbol));

            var token = _tradingOps.Value.FinnhubToken;

            var response = await _httpClient.GetAsync(
                $"https://finnhub.io/api/v1/quote?symbol={stockSymbol}&token={token}",
                cancellationToken);

            response.EnsureSuccessStatusCode();

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            var responseDictionary = JsonSerializer.Deserialize<Dictionary<string, object>>(responseBody);

            if (Convert.ToDouble(responseDictionary["c"].ToString()) == 0)
                return null;

            return (responseDictionary == null || responseDictionary.Count == 0) ? null : responseDictionary;


        }




    }
}