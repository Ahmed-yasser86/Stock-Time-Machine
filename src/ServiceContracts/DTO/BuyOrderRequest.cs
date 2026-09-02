using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Entities;

namespace ServiceContracts.DTO
{
    public class BuyOrderRequest
    {
        [Required]
        public string StockSymbol { set; get; }

        [Required]
        public string StockName { set; get; }

        public DateTime DateAndTimeOfOrder { set; get; }

        [Range(1, 100000)]
        public uint Quantity { set; get; }

        [Range(1, 100000)]
        public double Price { set; get; }

        // Convert DTO to Entity
        public BuyOrder ToBuyOrder()
        {
            return new BuyOrder
            {
                StockSymbol = this.StockSymbol,
                StockName = this.StockName,
                DateAndTimeOfOrder = this.DateAndTimeOfOrder,
                Quantity = this.Quantity,
                Price = this.Price
            };
        }

        public BuyOrderResponse ConvertToBuyOrderResponse()
        {
            return new BuyOrderResponse
            {
                BuyOrderID = Guid.NewGuid(),
                DateAndTimeOfOrder = DateAndTimeOfOrder,
                Price = this.Price,
                Quantity = Quantity,
                StockName = StockName,
                StockSymbol = StockSymbol,
                TradeAmount = (double)Price * (double)Quantity
            };
        }
    }
}