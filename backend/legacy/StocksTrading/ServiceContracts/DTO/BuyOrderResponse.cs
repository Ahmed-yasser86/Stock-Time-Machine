using Entities;

namespace ServiceContracts.DTO
{


    public class BuyOrderResponse
    {
        public Guid BuyOrderID { set; get; }

        public string StockSymbol { set; get; }

        public string StockName { set; get; }

        public DateTime DateAndTimeOfOrder { set; get; }

        public uint Quantity { set; get; }

        public double Price { set; get; }

        public double TradeAmount { set; get; }



    };


    public static class BuyOrderResponseExtension
    {
        public static BuyOrderResponse ConvertToBuyOrderResponse(this BuyOrder order)
        {
            return new BuyOrderResponse
            {
                BuyOrderID = order.BuyOrderID,
                StockSymbol = order.StockSymbol,
                StockName = order.StockName,
                DateAndTimeOfOrder = order.DateAndTimeOfOrder,
                Quantity = order.Quantity,
                Price = order.Price,
                TradeAmount = (double)order.Price * (double)order.Quantity
            };
        }
    }
}