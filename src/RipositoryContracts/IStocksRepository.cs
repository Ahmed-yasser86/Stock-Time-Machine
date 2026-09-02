using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Linq.Expressions;
using Entities;

namespace RepositoryContracts
{
    public interface IStocksRepository
    {
        Task<BuyOrder> CreateBuyOrder(BuyOrder buyOrder);

        Task<SellOrder> CreateSellOrder(SellOrder sellOrder);

        Task<List<BuyOrder>> GetBuyOrders();

        Task<List<SellOrder>> GetSellOrders();

        Task<List<BuyOrder>> GetFilteredBuyOrders(Expression<Func<BuyOrder, bool>> predicate);

        Task<List<SellOrder>> GetFilteredSellOrders(Expression<Func<SellOrder, bool>> predicate);

        Task<BuyOrder?> GetBuyOrderById(Guid? orderId);

        Task<SellOrder?> GetSellOrderById(Guid? orderId);
    }
}