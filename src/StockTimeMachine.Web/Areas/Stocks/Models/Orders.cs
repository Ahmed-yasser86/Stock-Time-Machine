using Entities;
using ServiceContracts.DTO;

namespace StocksApp2.Areas.Stocks.Models
{
    public class Orders
    {
      public  List<BuyOrderResponse> BuyOrders { get; set; }
      public   List<SellOrderResponse> SellOrders { get; set; }


    }
}
