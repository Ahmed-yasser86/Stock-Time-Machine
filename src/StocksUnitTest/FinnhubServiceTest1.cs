using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using ServiceContracts;
using Services;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ServiceContractsContacts;
namespace StocksUnitTest
{
    public class FinnhubServiceTest1
    {
        private readonly IFinnhubService _finnhubService;
        private readonly Mock<HttpMessageHandler> _handlerMock;
        private readonly IOptions<TradingOptions> _options;

        public FinnhubServiceTest1()
        {
            _handlerMock = new Mock<HttpMessageHandler>();
            var httpClient = new HttpClient(_handlerMock.Object);

            // Create TradingOptions instance for test
            var tradingOptions = new TradingOptions
            {
                FinnhubToken = "ABC_TEST_TOKEN",
                DefaultStockSymbol = "MSFT"
            };

            _options = Options.Create(tradingOptions);

            _finnhubService = new FinnhubService(httpClient, _options);
        }

        #region GetCompanyProfile

        //  PRIVATE HELPER (The "Cleaner" Method) 
        private void MockHttpResponse(HttpStatusCode statusCode, string content)
        {
            _handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = statusCode,
                    Content = new StringContent(content)
                });
        }

        // --- UNIT TESTS ---

        [Fact]
        public async Task GetCompanyProfile_NullOrEmpty_ShouldThrowException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => _finnhubService.GetCompanyProfile(null!));
            await Assert.ThrowsAsync<ArgumentNullException>(() => _finnhubService.GetCompanyProfile(""));
        }

        [Fact]
        public async Task GetCompanyProfile_ProperStockSymbol_ShouldReturnData()
        {
            string mockJson = "{\"name\":\"Microsoft Corp\",\"ticker\":\"MSFT\"}";
            MockHttpResponse(HttpStatusCode.OK, mockJson);

            var result = await _finnhubService.GetCompanyProfile("MSFT");

            Assert.NotNull(result);
            Assert.Equal("Microsoft Corp", ((System.Text.Json.JsonElement)result!["name"]).GetString());
            Assert.Equal("MSFT", ((System.Text.Json.JsonElement)result["ticker"]).GetString());
        }

        [Fact]
        public async Task GetCompanyProfile_InvalidStockSymbol_ShouldReturnNull()
        {
            MockHttpResponse(HttpStatusCode.OK, "{}");
            var result = await _finnhubService.GetCompanyProfile("FAKE_SYMBOL");
            Assert.Null(result);
        }

        [Fact]
        public async Task GetCompanyProfile_WhenApiKeyIsInvalid_ShouldThrowException()
        {
            MockHttpResponse(HttpStatusCode.Unauthorized, "Invalid API Key");
            await Assert.ThrowsAsync<HttpRequestException>(() => _finnhubService.GetCompanyProfile("AAPL"));
        }

        [Fact]
        public async Task GetCompanyProfile_WhenSomeFieldsAreMissing_ShouldStillReturnData()
        {
            string partialJson = "{\"name\":\"Apple Inc\",\"ticker\":\"AAPL\"}";
            MockHttpResponse(HttpStatusCode.OK, partialJson);

            var result = await _finnhubService.GetCompanyProfile("AAPL");

            Assert.NotNull(result);
            Assert.Equal("Apple Inc", ((System.Text.Json.JsonElement)result!["name"]).GetString());
            Assert.False(result.ContainsKey("phone"));
        }

        [Fact]
        public async Task GetCompanyProfile_WhenServerIsDown_ShouldThrowException()
        {
            _handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("No network connection"));

            await Assert.ThrowsAsync<HttpRequestException>(() => _finnhubService.GetCompanyProfile("AAPL"));
        }

        #endregion

        #region GetStockPriceQuote

        [Fact]
        public async Task GetStockPriceQuote_NullOrEmpty_ShouldThrowException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => _finnhubService.GetStockPriceQuote(null!));
            await Assert.ThrowsAsync<ArgumentNullException>(() => _finnhubService.GetStockPriceQuote(""));
        }

        [Fact]
        public async Task GetStockPriceQuote_ProperStockSymbol_ShouldReturnData()
        {
            string mockJson = "{\"c\":235.87,\"d\":9.12,\"dp\":4.0221,\"h\":236.6,\"l\":226.06,\"o\":226.24,\"pc\":226.75,\"t\":1666987204}";
            MockHttpResponse(HttpStatusCode.OK, mockJson);

            var result = await _finnhubService.GetStockPriceQuote("AAPL");

            Assert.NotNull(result);
            Assert.Equal(235.87, ((System.Text.Json.JsonElement)result!["c"]).GetDouble());
            Assert.Equal(236.6, ((System.Text.Json.JsonElement)result["h"]).GetDouble());
            Assert.Equal(226.06, ((System.Text.Json.JsonElement)result["l"]).GetDouble());
        }

        [Fact]
        public async Task GetStockPriceQuote_InvalidStockSymbol_ShouldReturnNull()
        {
            MockHttpResponse(HttpStatusCode.OK, "{\"c\":0,\"d\":null,\"dp\":null}");
            var result = await _finnhubService.GetStockPriceQuote("INVALID");
            Assert.Null(result);
        }

        [Fact]
        public async Task GetStockPriceQuote_WhenApiKeyIsInvalid_ShouldThrowException()
        {
            MockHttpResponse(HttpStatusCode.Unauthorized, "Invalid API Key");
            await Assert.ThrowsAsync<HttpRequestException>(() => _finnhubService.GetStockPriceQuote("MSFT"));
        }

        [Fact]
        public async Task GetStockPriceQuote_WhenServerIsDown_ShouldThrowException()
        {
            _handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("No network connection"));

            await Assert.ThrowsAsync<HttpRequestException>(() => _finnhubService.GetStockPriceQuote("MSFT"));
        }

        #endregion

        #region GetStockPriceQuote Edge Cases

        [Fact]
        public async Task GetStockPriceQuote_WhenPriceIsZero_ShouldReturnNull()
        {
            string mockJson = "{\"c\":0,\"d\":null,\"dp\":null,\"h\":0,\"l\":0,\"o\":0,\"pc\":0,\"t\":0}";
            MockHttpResponse(HttpStatusCode.OK, mockJson);

            var result = await _finnhubService.GetStockPriceQuote("SYMBOL_WITH_NO_DATA");
            Assert.Null(result);
        }

        [Fact]
        public async Task GetStockPriceQuote_WhenResponseIsMalformed_ShouldThrowException()
        {
            MockHttpResponse(HttpStatusCode.OK, "{ \"invalid\": \"data\" "); // Missing closing brace
            await Assert.ThrowsAnyAsync<Exception>(() => _finnhubService.GetStockPriceQuote("AAPL"));
        }

        #endregion
    }
}