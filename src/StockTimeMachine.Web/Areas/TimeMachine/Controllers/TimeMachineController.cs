using Microsoft.AspNetCore.Mvc;
using StockTimeMachine.ServiceContracts;
using StocksApp2.Areas.TimeMachine.Models;

namespace StocksApp2.Areas.TimeMachine.Controllers;

[Area("TimeMachine")]
public class TimeMachineController : Controller
{
    private readonly ITimeMachineService _timeMachine;
    private readonly ISimulationService _simulation;
    private readonly ILogger<TimeMachineController> _logger;

    public TimeMachineController(
        ITimeMachineService timeMachine,
        ISimulationService simulation,
        ILogger<TimeMachineController> logger)
    {
        _timeMachine = timeMachine;
        _simulation = simulation;
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Simulate(string symbol, string date, decimal amount, string? exitDate)
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

            DateOnly? exit = DateOnly.TryParse(exitDate, out var parsed) ? parsed : null;
            var simulation = await _simulation.Run(model.Symbol, model.SnapshotDate, amount, exit);
            model.SimulationResult = simulation;
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid input for simulation {Symbol} on {Date}", model.Symbol, model.SnapshotDate);
            model.Error = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running simulation for {Symbol} on {Date}", model.Symbol, model.SnapshotDate);
            model.Error = $"Failed to run simulation: {ex.Message}";
        }

        return View("Snapshot", model);
    }
}
