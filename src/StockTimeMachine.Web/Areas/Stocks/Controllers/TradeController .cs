using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ServiceContracts;
using StocksApp2.Areas.Stocks.Models;
using System.Reflection;
using System.Text.Json;
using ServiceContractsContacts;
namespace StocksApp2.Areas.StocksUIComponent.Controllers
{

    [Area("Stocks")]
    public class TradeController : Controller
    {
        private readonly IOptions<TradingOptions> _tradingOptions;
        private readonly IFinnhubService _finnhubService;
        private readonly IStocksService _stocksService;
        public TradeController(IFinnhubService finnhubService,IStocksService stocksService,IOptions<TradingOptions> tradingop)
        {

            _finnhubService = finnhubService;
            _stocksService = stocksService;
            _tradingOptions = tradingop;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var symbol = _tradingOptions.Value.DefaultStockSymbol;
            var token = _tradingOptions.Value.FinnhubToken; // get token from appsettings

            var profile = await _finnhubService.GetCompanyProfile(symbol);
            var priceQuote = await _finnhubService.GetStockPriceQuote(symbol);
            var model = new StockTrade
            {
                StockSymbol = symbol,
                StockName = ((JsonElement)profile["name"]).GetString(),
                Price = ((JsonElement)priceQuote["c"]).GetDouble(),
                Quantity = 1
            };

            ViewBag.FinnhubToken = token; // pass token to view
            return View("Index", model);
        }
        [HttpPost]
        public async Task<IActionResult> BuyOrders(StockTrade stockInfo)
        {


            await _stocksService.CreateBuyOrder(new ServiceContracts.DTO.BuyOrderRequest
            {
                StockSymbol = stockInfo.StockSymbol,
                StockName = stockInfo.StockName,
                Price = stockInfo.Price,
                Quantity = stockInfo.Quantity
            });

            return RedirectToAction("Index");
        }
        [HttpPost]

        public async Task<IActionResult> SellOrders(StockTrade stockInfo)
        {
            await _stocksService.CreateSellOrder(new ServiceContracts.DTO.SellOrderRequest
            {
                StockSymbol = stockInfo.StockSymbol,
                StockName = stockInfo.StockName,
                Price = stockInfo.Price,
                Quantity = stockInfo.Quantity
            });
            return RedirectToAction("Index");

        }



        [HttpGet]
        public async Task<IActionResult> Orders()
        {

            var Buyorders = await _stocksService.GetBuyOrders();
            var Sellorders = await _stocksService.GetSellOrders();
            var orders = new Orders
            {

                BuyOrders = Buyorders,
                SellOrders = Sellorders
            };
            return View("Orders", orders);
        }
    }
}
