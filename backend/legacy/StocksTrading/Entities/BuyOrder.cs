using System.ComponentModel.DataAnnotations;

namespace Entities
{
    public class BuyOrder
    {

        [Key]
    public    Guid BuyOrderID { set; get; }

        [Required]
     public   string StockSymbol { set; get; }

        [Required]
    public    string StockName { set; get; }

     public   DateTime DateAndTimeOfOrder { set; get; }
        [Range(1,100000)]
       public uint Quantity;//[Value should be between 1 and 100000]
       
        [Range(1,100000)]
       public double Price { set; get; }//
    }
}
