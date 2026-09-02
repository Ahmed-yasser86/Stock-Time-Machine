using System.ComponentModel.DataAnnotations;

namespace StocksApp2.Areas.TimeMachine.Models;

public class TimeMachineSearchModel
{
    [Required(ErrorMessage = "Please enter a stock symbol")]
    [Display(Name = "Company Symbol")]
    public string Symbol { get; set; } = "TSLA";

    [Required(ErrorMessage = "Please select a historical date")]
    [Display(Name = "Historical Date")]
    [DataType(DataType.Date)]
    public DateOnly SnapshotDate { get; set; } = new DateOnly(2020, 1, 15);
}
