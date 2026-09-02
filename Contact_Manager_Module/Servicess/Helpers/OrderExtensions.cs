using Entities;
using ServiceContracts.DTO;
using ServiceContracts.DTOs;

namespace Servicess.Helpers
{
    public static class OrderExtensions
    {
        public static BuyOrder ToBuyOrder(this BuyOrderRequest request)
        {
            return new BuyOrder
            {
                StockSymbol = request.StockSymbol,
                StockName = request.StockName,
                DateAndTimeOfOrder = DateTime.Now,
                Quantity = request.Quantity,
                Price = request.Price
            };
        }

        public static SellOrder ToSellOrder(this SellOrderRequest request)
        {
            return new SellOrder
            {
                StockSymbol = request.StockSymbol,
                StockName = request.StockName,
                DateAndTimeOfOrder = DateTime.Now,
                Quantity = request.Quantity,
                Price = request.Price
            };
        }

        public static BuyOrderResponse ConvertToBuyOrderResponse(this BuyOrder order)
        {
            return new BuyOrderResponse
            {
                BuyOrderID = order.BuyOrderID,
                StockSymbol = order.StockSymbol,
                StockName = order.StockName,
                DateAndTimeOfOrder = order.DateAndTimeOfOrder,
                Quantity = order.Quantity,
                Price = order.Price
            };
        }

        public static SellOrderResponse ConvertToSellOrderResponse(this SellOrder order)
        {
            return new SellOrderResponse
            {
                SellOrderID = order.SellOrderID,
                StockSymbol = order.StockSymbol,
                StockName = order.StockName,
                DateAndTimeOfOrder = order.DateAndTimeOfOrder,
                Quantity = order.Quantity,
                Price = order.Price
            };
        }
    }
}