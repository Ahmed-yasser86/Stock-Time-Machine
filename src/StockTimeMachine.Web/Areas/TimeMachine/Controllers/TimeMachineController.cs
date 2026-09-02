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
        var model = new TimeMachineViewModel();
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(TimeMachineViewModel model)
    {
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
