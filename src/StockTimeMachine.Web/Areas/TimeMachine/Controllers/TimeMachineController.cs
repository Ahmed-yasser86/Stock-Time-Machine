using Microsoft.AspNetCore.Mvc;
using StockTimeMachine.ServiceContracts;
using StocksApp2.Areas.TimeMachine.Models;

namespace StocksApp2.Areas.TimeMachine.Controllers;

[Area("TimeMachine")]
public class TimeMachineController : Controller
{
    private readonly ITimeMachineService _timeMachine;
    private readonly ILogger<TimeMachineController> _logger;

    public TimeMachineController(
        ITimeMachineService timeMachine,
        ILogger<TimeMachineController> logger)
    {
        _timeMachine = timeMachine;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View(new TimeMachineSearchModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Index(TimeMachineSearchModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        return RedirectToAction("Snapshot", new { symbol = model.Symbol, date = model.SnapshotDate.ToString("yyyy-MM-dd") });
    }

    [HttpGet]
    public async Task<IActionResult> Snapshot(string symbol, string date)
    {
        if (string.IsNullOrEmpty(symbol) || !DateOnly.TryParse(date, out var snapshotDate))
            return RedirectToAction("Index");

        var model = new TimeMachineViewModel
        {
            Symbol = symbol.ToUpper(),
            SnapshotDate = snapshotDate
        };

        try
        {
            var snapshot = await _timeMachine.GetSnapshot(model.Symbol, model.SnapshotDate);
            model.Snapshot = snapshot;
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid input for {Symbol} on {Date}", model.Symbol, model.SnapshotDate);
            model.Error = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching snapshot for {Symbol} on {Date}", model.Symbol, model.SnapshotDate);
            model.Error = $"Failed to fetch data: {ex.Message}";
        }

        return View(model);
    }
}
