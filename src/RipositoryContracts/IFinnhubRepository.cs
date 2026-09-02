using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Linq.Expressions;
using Entities;

namespace RepositoryContracts
{
    public interface IFinnhubRepository
    {
        Task<Dictionary<string, object>?> GetCompanyProfile(string stockSymbol, CancellationToken cancellationToken = default);

        Task<Dictionary<string, object>?> GetStockPriceQuote(string stockSymbol, CancellationToken cancellationToken = default);
    }
}