using Microsoft.Extensions.Options;
using Moq;
using ServiceContracts;
using ServiceContracts.DTO;
using Services;
using ServiceContractsContacts;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.Logging;
using RepositoryContracts;

namespace StocksUnitTest
{
    public class StocksUnitTest
    {
        private readonly IStocksService _stocksService;
        private readonly IFinnhubService _finnhubService;
        private readonly Mock<HttpMessageHandler> _handlerMock;
        private readonly IOptions<TradingOptions> _options;
        private readonly Mock<ILogger<StocksService>> _loggerMock;
        private readonly Mock<IStocksRepository> _stocksRepositoryMock;
        public StocksUnitTest()
        {
            _handlerMock = new Mock<HttpMessageHandler>();
            var httpClient = new HttpClient(_handlerMock.Object);

            // Create TradingOptions instance for testing
            var tradingOptions = new TradingOptions
            {
                FinnhubToken = "ABC_TEST_TOKEN",
                DefaultStockSymbol = "MSFT"
            };

            // Wrap it with IOptions
            _options = Options.Create(tradingOptions);

            // Pass options to FinnhubService instead of IConfiguration
            _finnhubService = new FinnhubService(httpClient, _options);

                        _loggerMock = new Mock<ILogger<StocksService>>();
            _stocksRepositoryMock = new Mock<IStocksRepository>();
            _stocksService = new StocksService( _stocksRepositoryMock.Object,_loggerMock.Object);
        }
        
        #region CreateBuyOrder

        public async Task CreateBuyOrder_InsertNullValue()
        {
            BuyOrderRequest? request = null;

            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                await _stocksService.CreateBuyOrder(request);
            });
        }

        [Fact]
        public async Task CreateBuyOrder_InsertNullName_ShouldThrowArgumentNullException()
        {
            BuyOrderRequest request = new BuyOrderRequest
            {
                Price = 1000,
                Quantity = 1000,
                StockName = null,
                StockSymbol = "MSFT"
            };

            await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await _stocksService.CreateBuyOrder(request);
            });
        }

        public async Task CreateBuyOrder_InsertNullSympole_ShouldThrowArgumentNullException()
        {
            BuyOrderRequest request = new BuyOrderRequest
            {
                Price = 1000,
                Quantity = 1000,
                StockName = "name",
                StockSymbol = null!
            };

            await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await _stocksService.CreateBuyOrder(request);
            });
        }

        public async Task CreateBuyOrder_InsertNotExsistingStock_ShouldThrowArgumentNullException()
        {
            BuyOrderRequest request = new BuyOrderRequest
            {
                Price = 1000,
                Quantity = 1000,
                StockName = "InvalidName",
                StockSymbol = "INVALIDSYMPOLE"!
            };

            await Assert.ThrowsAsync<Exception>(async () =>
            {
                await _stocksService.CreateBuyOrder(request);
            });
        }

        [Fact]
        public async Task CreateBuyOrder_QuantityIsLessThanOne_ShouldThrowArgumentException()
        {
            BuyOrderRequest request = new BuyOrderRequest { StockSymbol = "MSFT", StockName = "Microsoft", Price = 100, Quantity = 0 };
            await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await _stocksService.CreateBuyOrder(request);
            });
        }

        [Fact]
        public async Task CreateBuyOrder_PriceIsZero_ShouldThrowArgumentException()
        {
            BuyOrderRequest request = new BuyOrderRequest { StockSymbol = "MSFT", StockName = "Microsoft", Price = 0, Quantity = 10 };
            await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await _stocksService.CreateBuyOrder(request);
            });
        }

        [Fact]
        public async Task CreateBuyOrder_ValidData_ShouldReturnSuccessResponse()
        {
            BuyOrderRequest request = new BuyOrderRequest
            {
                StockSymbol = "MSFT",
                StockName = "Microsoft",
                Price = 150.50,
                Quantity = 10,
                DateAndTimeOfOrder = DateTime.UtcNow,
            };

            BuyOrderResponse response = await _stocksService.CreateBuyOrder(request);

            Assert.NotEqual(Guid.Empty, response.BuyOrderID);
            Assert.Equal(request.StockSymbol, response.StockSymbol);
            Assert.True(response.DateAndTimeOfOrder > DateTime.MinValue);
        }

        #endregion

        #region CreateSellOrder

        [Fact]
        public async Task CreateSellOrder_InsertNullValue_ShouldThrowArgumentNullException()
        {
            SellOrderRequest? request = null;
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                await _stocksService.CreateSellOrder(request);
            });
        }

        [Fact]
        public async Task CreateSellOrder_InvalidData_ShouldThrowArgumentException()
        {
            SellOrderRequest request = new SellOrderRequest
            {
                StockSymbol = "MSFT",
                StockName = "Microsoft",
                Price = 0,
                Quantity = 10
            };

            await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await _stocksService.CreateSellOrder(request);
            });
        }

        [Fact]
        public async Task CreateSellOrder_ValidData_ShouldReturnSuccessResponse()
        {
            SellOrderRequest request = new SellOrderRequest
            {
                StockSymbol = "AAPL",
                StockName = "Apple",
                Price = 200.00,
                Quantity = 5,
                DateAndTimeOfOrder = DateTime.UtcNow
            };

            SellOrderResponse response = await _stocksService.CreateSellOrder(request);

            Assert.NotEqual(Guid.Empty, response.SellOrderID);
            Assert.Equal(request.StockSymbol, response.StockSymbol);
            Assert.Equal(1000.00, response.TradeAmount); // 200 * 5
        }

        #endregion

        #region GetSellOrders

        [Fact]
        public async Task GetSellOrders_AddFewOrders_ShouldReturnCorrectList()
        {
            SellOrderRequest request = new SellOrderRequest { StockSymbol = "TSLA", StockName = "Tesla", Price = 500, Quantity = 10, DateAndTimeOfOrder = DateTime.Now };
            await _stocksService.CreateSellOrder(request);

            List<SellOrderResponse> sellOrdersFromGet = await _stocksService.GetSellOrders();
            Assert.Single(sellOrdersFromGet);
            Assert.Equal("TSLA", sellOrdersFromGet[0].StockSymbol);
        }

        #endregion

        #region GetBuyOrders

        [Fact]
        public async Task GetBuyOrders_DefaultList_ShouldBeEmpty()
        {
            List<BuyOrderResponse> buyOrdersFromGet = await _stocksService.GetBuyOrders();
            Assert.Empty(buyOrdersFromGet);
        }

        [Fact]
        public async Task GetBuyOrders_AddFewOrders_ShouldReturnCorrectList()
        {
            BuyOrderRequest request1 = new BuyOrderRequest { StockSymbol = "MSFT", StockName = "Microsoft", Price = 100, Quantity = 1, DateAndTimeOfOrder = DateTime.Parse("2023-01-01") };
            BuyOrderRequest request2 = new BuyOrderRequest { StockSymbol = "AAPL", StockName = "Apple", Price = 200, Quantity = 2, DateAndTimeOfOrder = DateTime.Parse("2023-01-02") };

            await _stocksService.CreateBuyOrder(request1);
            await _stocksService.CreateBuyOrder(request2);

            List<BuyOrderResponse> buyOrdersFromGet = await _stocksService.GetBuyOrders();
            Assert.Equal(2, buyOrdersFromGet.Count);
            Assert.Equal("AAPL", buyOrdersFromGet[0].StockSymbol);
        }

        #endregion
    }
}