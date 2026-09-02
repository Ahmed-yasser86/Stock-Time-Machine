using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RepositoryContracts;
using Entities;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SerilogTimings;
using Entities;
using EntitiesStocks;

namespace Repositories
{
    public class StocksRepository : IStocksRepository
    {
        private readonly StocksDbContext _db;
        private readonly ILogger<StocksRepository> _logger;

        public StocksRepository(StocksDbContext db, ILogger<StocksRepository> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<BuyOrder> CreateBuyOrder(BuyOrder buyOrder)
        {
            using (Operation.Time("CreateBuyOrder database operation for Stock: {StockSymbol}", buyOrder?.StockSymbol))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}. BuyOrder: {@BuyOrder}",
                    nameof(CreateBuyOrder), DateTime.UtcNow, buyOrder);

                try
                {
                    if (buyOrder == null)
                    {
                        _logger.LogWarning("CreateBuyOrder called with null buyOrder parameter");
                        throw new ArgumentNullException(nameof(buyOrder));
                    }

                    _logger.LogDebug("Adding buy order with ID: {OrderID}, Stock: {StockSymbol}, Quantity: {Quantity} to database",
                        buyOrder.BuyOrderID, buyOrder.StockSymbol, buyOrder.Quantity);

                    await _db.BuyOrders.AddAsync(buyOrder);
                    await _db.SaveChangesAsync();

                    _logger.LogInformation("Successfully added buy order with ID: {OrderID}, Stock: {StockSymbol}, Quantity: {Quantity}",
                        buyOrder.BuyOrderID, buyOrder.StockSymbol, buyOrder.Quantity);

                    return buyOrder;
                }
                catch (DbUpdateException ex)
                {
                    _logger.LogError(ex, "Database update error while creating buy order. BuyOrder: {@BuyOrder}. Error: {ErrorMessage}",
                        buyOrder, ex.Message);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error occurred in {MethodName} for buy order: {@BuyOrder}",
                        nameof(CreateBuyOrder), buyOrder);
                    throw;
                }
            }
        }

        public async Task<SellOrder> CreateSellOrder(SellOrder sellOrder)
        {
            using (Operation.Time("CreateSellOrder database operation for Stock: {StockSymbol}", sellOrder?.StockSymbol))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}. SellOrder: {@SellOrder}",
                    nameof(CreateSellOrder), DateTime.UtcNow, sellOrder);

                try
                {
                    if (sellOrder == null)
                    {
                        _logger.LogWarning("CreateSellOrder called with null sellOrder parameter");
                        throw new ArgumentNullException(nameof(sellOrder));
                    }

                    _logger.LogDebug("Adding sell order with ID: {OrderID}, Stock: {StockSymbol}, Quantity: {Quantity} to database",
                        sellOrder.SellOrderID, sellOrder.StockSymbol, sellOrder.Quantity);

                    await _db.SellOrders.AddAsync(sellOrder);
                    await _db.SaveChangesAsync();

                    _logger.LogInformation("Successfully added sell order with ID: {OrderID}, Stock: {StockSymbol}, Quantity: {Quantity}",
                        sellOrder.SellOrderID, sellOrder.StockSymbol, sellOrder.Quantity);

                    return sellOrder;
                }
                catch (DbUpdateException ex)
                {
                    _logger.LogError(ex, "Database update error while creating sell order. SellOrder: {@SellOrder}. Error: {ErrorMessage}",
                        sellOrder, ex.Message);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error occurred in {MethodName} for sell order: {@SellOrder}",
                        nameof(CreateSellOrder), sellOrder);
                    throw;
                }
            }
        }

        public async Task<List<BuyOrder>> GetBuyOrders()
        {
            using (Operation.Time("GetBuyOrders database operation"))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}",
                    nameof(GetBuyOrders), DateTime.UtcNow);

                try
                {
                    _logger.LogDebug("Retrieving all buy orders from database");
                    var buyOrders = await _db.BuyOrders.OrderByDescending(b => b.DateAndTimeOfOrder).ToListAsync();

                    _logger.LogInformation("{MethodName} completed successfully. Retrieved {Count} buy orders",
                        nameof(GetBuyOrders), buyOrders.Count);

                    return buyOrders;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in {MethodName} method", nameof(GetBuyOrders));
                    throw;
                }
            }
        }

        public async Task<List<SellOrder>> GetSellOrders()
        {
            using (Operation.Time("GetSellOrders database operation"))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}",
                    nameof(GetSellOrders), DateTime.UtcNow);

                try
                {
                    _logger.LogDebug("Retrieving all sell orders from database");
                    var sellOrders = await _db.SellOrders.OrderByDescending(s => s.DateAndTimeOfOrder).ToListAsync();

                    _logger.LogInformation("{MethodName} completed successfully. Retrieved {Count} sell orders",
                        nameof(GetSellOrders), sellOrders.Count);

                    return sellOrders;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in {MethodName} method", nameof(GetSellOrders));
                    throw;
                }
            }
        }

        public async Task<List<BuyOrder>> GetFilteredBuyOrders(Expression<Func<BuyOrder, bool>> predicate)
        {
            using (Operation.Time("GetFilteredBuyOrders database operation"))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}",
                    nameof(GetFilteredBuyOrders), DateTime.UtcNow);

                try
                {
                    if (predicate == null)
                    {
                        _logger.LogWarning("GetFilteredBuyOrders called with null predicate");
                        throw new ArgumentNullException(nameof(predicate));
                    }

                    _logger.LogDebug("Executing filtered query on BuyOrders");
                    var buyOrders = await _db.BuyOrders.Where(predicate).OrderByDescending(b => b.DateAndTimeOfOrder).ToListAsync();

                    _logger.LogInformation("{MethodName} completed successfully. Retrieved {Count} buy orders matching filter",
                        nameof(GetFilteredBuyOrders), buyOrders.Count);

                    _logger.LogDebug("Filter predicate used: {Predicate}", predicate.ToString());

                    return buyOrders;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in {MethodName} method with predicate: {Predicate}",
                        nameof(GetFilteredBuyOrders), predicate?.ToString());
                    throw;
                }
            }
        }

        public async Task<List<SellOrder>> GetFilteredSellOrders(Expression<Func<SellOrder, bool>> predicate)
        {
            using (Operation.Time("GetFilteredSellOrders database operation"))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}",
                    nameof(GetFilteredSellOrders), DateTime.UtcNow);

                try
                {
                    if (predicate == null)
                    {
                        _logger.LogWarning("GetFilteredSellOrders called with null predicate");
                        throw new ArgumentNullException(nameof(predicate));
                    }

                    _logger.LogDebug("Executing filtered query on SellOrders");
                    var sellOrders = await _db.SellOrders.Where(predicate).OrderByDescending(s => s.DateAndTimeOfOrder).ToListAsync();

                    _logger.LogInformation("{MethodName} completed successfully. Retrieved {Count} sell orders matching filter",
                        nameof(GetFilteredSellOrders), sellOrders.Count);

                    _logger.LogDebug("Filter predicate used: {Predicate}", predicate.ToString());

                    return sellOrders;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in {MethodName} method with predicate: {Predicate}",
                        nameof(GetFilteredSellOrders), predicate?.ToString());
                    throw;
                }
            }
        }

        public async Task<BuyOrder?> GetBuyOrderById(Guid? orderId)
        {
            using (Operation.Time("GetBuyOrderById database query for ID: {OrderId}", orderId))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}. Order ID: {OrderId}",
                    nameof(GetBuyOrderById), DateTime.UtcNow, orderId);

                try
                {
                    if (orderId == null)
                    {
                        _logger.LogWarning("GetBuyOrderById called with null ID");
                        return null;
                    }

                    _logger.LogDebug("Retrieving buy order with ID: {OrderId}", orderId);
                    var buyOrder = await _db.BuyOrders.FindAsync(orderId);

                    if (buyOrder == null)
                    {
                        _logger.LogInformation("No buy order found with ID: {OrderId}", orderId);
                    }
                    else
                    {
                        _logger.LogInformation("Successfully retrieved buy order with ID: {OrderId}, Stock: {StockSymbol}",
                            buyOrder.BuyOrderID, buyOrder.StockSymbol);
                    }

                    return buyOrder;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in {MethodName} for Order ID: {OrderId}",
                        nameof(GetBuyOrderById), orderId);
                    throw;
                }
            }
        }

        public async Task<SellOrder?> GetSellOrderById(Guid? orderId)
        {
            using (Operation.Time("GetSellOrderById database query for ID: {OrderId}", orderId))
            {
                _logger.LogInformation("Executing {MethodName} method at {Timestamp}. Order ID: {OrderId}",
                    nameof(GetSellOrderById), DateTime.UtcNow, orderId);

                try
                {
                    if (orderId == null)
                    {
                        _logger.LogWarning("GetSellOrderById called with null ID");
                        return null;
                    }

                    _logger.LogDebug("Retrieving sell order with ID: {OrderId}", orderId);
                    var sellOrder = await _db.SellOrders.FindAsync(orderId);

                    if (sellOrder == null)
                    {
                        _logger.LogInformation("No sell order found with ID: {OrderId}", orderId);
                    }
                    else
                    {
                        _logger.LogInformation("Successfully retrieved sell order with ID: {OrderId}, Stock: {StockSymbol}",
                            sellOrder.SellOrderID, sellOrder.StockSymbol);
                    }

                    return sellOrder;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in {MethodName} for Order ID: {OrderId}",
                        nameof(GetSellOrderById), orderId);
                    throw;
                }
            }
        }
    }
}