using Microsoft.Extensions.Options;
using ServiceContracts;
using ServiceContracts.DTO;
using Servicess.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RepositoryContracts;
using Entities;
using Microsoft.Extensions.Logging;
using SerilogTimings;

namespace Services
{
    public class StocksService : IStocksService
    {
        private readonly IStocksRepository _stocksRepository;
        private readonly ILogger<StocksService> _logger;

        public StocksService(IStocksRepository stocksRepository, ILogger<StocksService> logger)
        {
            _stocksRepository = stocksRepository;
            _logger = logger;
        }

        public async Task<BuyOrderResponse> CreateBuyOrder(BuyOrderRequest? buyOrderRequest)
        {
            using (Operation.Time("Create buy order operation for Stock: {StockSymbol}", buyOrderRequest?.StockSymbol))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}. Request data: {@BuyOrderRequest}",
                    nameof(CreateBuyOrder), DateTime.UtcNow, buyOrderRequest);

                try
                {
                    if (buyOrderRequest == null)
                    {
                        _logger.LogWarning("CreateBuyOrder called with null request parameter");
                        throw new ArgumentNullException(nameof(buyOrderRequest));
                    }

                    _logger.LogDebug("Validating buy order request");
                    ValidationHelpers.ValidationFunction(buyOrderRequest);

                    _logger.LogDebug("Converting BuyOrderRequest to BuyOrder entity");
                    var buyOrder = buyOrderRequest.ToBuyOrder();
                    buyOrder.BuyOrderID = Guid.NewGuid();

                    _logger.LogDebug("Adding new buy order with ID: {OrderID}, Stock: {StockSymbol}",
                        buyOrder.BuyOrderID, buyOrder.StockSymbol);

                    await _stocksRepository.CreateBuyOrder(buyOrder);

                    var buyOrderResponse = buyOrder.ConvertToBuyOrderResponse();

                    _logger.LogInformation("Successfully added new buy order. ID: {OrderID}, Stock: {StockSymbol}, Quantity: {Quantity}, Price: {Price}",
                        buyOrderResponse.BuyOrderID, buyOrderResponse.StockSymbol, buyOrderResponse.Quantity, buyOrderResponse.Price);

                    return buyOrderResponse;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error occurred in CreateBuyOrder for request: {@BuyOrderRequest}", buyOrderRequest);
                    throw;
                }
            }
        }

        public async Task<SellOrderResponse> CreateSellOrder(SellOrderRequest? sellOrderRequest)
        {
            using (Operation.Time("Create sell order operation for Stock: {StockSymbol}", sellOrderRequest?.StockSymbol))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}. Request data: {@SellOrderRequest}",
                    nameof(CreateSellOrder), DateTime.UtcNow, sellOrderRequest);

                try
                {
                    if (sellOrderRequest == null)
                    {
                        _logger.LogWarning("CreateSellOrder called with null request parameter");
                        throw new ArgumentNullException(nameof(sellOrderRequest));
                    }

                    _logger.LogDebug("Validating sell order request");
                    ValidationHelpers.ValidationFunction(sellOrderRequest);

                    _logger.LogDebug("Converting SellOrderRequest to SellOrder entity");
                    var sellOrder = sellOrderRequest.ToSellOrder();
                    sellOrder.SellOrderID = Guid.NewGuid();

                    _logger.LogDebug("Adding new sell order with ID: {OrderID}, Stock: {StockSymbol}",
                        sellOrder.SellOrderID, sellOrder.StockSymbol);

                    await _stocksRepository.CreateSellOrder(sellOrder);

                    var sellOrderResponse = sellOrder.ConvertToSellOrderResponse();

                    _logger.LogInformation("Successfully added new sell order. ID: {OrderID}, Stock: {StockSymbol}, Quantity: {Quantity}, Price: {Price}",
                        sellOrderResponse.SellOrderID, sellOrderResponse.StockSymbol, sellOrderResponse.Quantity, sellOrderResponse.Price);

                    return sellOrderResponse;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error occurred in CreateSellOrder for request: {@SellOrderRequest}", sellOrderRequest);
                    throw;
                }
            }
        }

        public async Task<List<BuyOrderResponse>> GetBuyOrders()
        {
            using (Operation.Time("Get all buy orders operation"))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}",
                    nameof(GetBuyOrders), DateTime.UtcNow);

                try
                {
                    var buyOrders = await _stocksRepository.GetBuyOrders();

                    if (buyOrders == null)
                    {
                        _logger.LogDebug("Repository returned null, returning empty list");
                        return new List<BuyOrderResponse>();
                    }

                    var result = buyOrders.Select(b => b.ConvertToBuyOrderResponse()).ToList();

                    _logger.LogInformation("{MethodName} completed successfully. Retrieved {Count} buy orders",
                        nameof(GetBuyOrders), result.Count);

                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in {MethodName} method", nameof(GetBuyOrders));
                    throw;
                }
            }
        }

        public async Task<List<SellOrderResponse>> GetSellOrders()
        {
            using (Operation.Time("Get all sell orders operation"))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}",
                    nameof(GetSellOrders), DateTime.UtcNow);

                try
                {
                    var sellOrders = await _stocksRepository.GetSellOrders();

                    if (sellOrders == null)
                    {
                        _logger.LogDebug("Repository returned null, returning empty list");
                        return new List<SellOrderResponse>();
                    }

                    var result = sellOrders.Select(s => s.ConvertToSellOrderResponse()).ToList();

                    _logger.LogInformation("{MethodName} completed successfully. Retrieved {Count} sell orders",
                        nameof(GetSellOrders), result.Count);

                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in {MethodName} method", nameof(GetSellOrders));
                    throw;
                }
            }
        }
    }
}