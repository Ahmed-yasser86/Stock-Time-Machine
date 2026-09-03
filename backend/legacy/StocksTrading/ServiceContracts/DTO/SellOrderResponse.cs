using Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceContracts.DTO
{
    public class SellOrderResponse
    {

        public Guid SellOrderID { set; get; }

        public string StockSymbol { set; get; }

        public string StockName { set; get; }

        public DateTime DateAndTimeOfOrder { set; get; }

        public uint Quantity { set; get; }

        public double Price { set; get; }

        public double TradeAmount { set; get; }

    }



    public static class SellOrderExtensions
    {
        public static SellOrderResponse ConvertToSellOrderResponse(this SellOrder order)
        {
            return new SellOrderResponse
            {
                SellOrderID = order.SellOrderID,
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