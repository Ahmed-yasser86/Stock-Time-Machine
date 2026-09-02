using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Entities;

namespace ServiceContracts.DTO
{
    public class SellOrderRequest
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
        public SellOrder ToSellOrder()
        {
            return new SellOrder
            {
                StockSymbol = this.StockSymbol,
                StockName = this.StockName,
                DateAndTimeOfOrder = this.DateAndTimeOfOrder,
                Quantity = this.Quantity,
                Price = this.Price
            };
        }

        // Convert DTO to Response (you already have this)
        public async Task<SellOrderResponse> ConvertToSellOrderResponse()
        {
            return new SellOrderResponse
            {
                SellOrderID = Guid.NewGuid(),
                StockSymbol = this.StockSymbol,
                StockName = this.StockName,
                Price = this.Price,
                Quantity = this.Quantity,
                DateAndTimeOfOrder = this.DateAndTimeOfOrder,
                TradeAmount = (double)this.Price * (double)this.Quantity
            };
        }
    }
}