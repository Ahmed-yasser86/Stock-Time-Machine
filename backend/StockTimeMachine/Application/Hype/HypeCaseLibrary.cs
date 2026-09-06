using System.Text.Json;

namespace StockTimeMachine;

// Deserialization boundary for stored cases: corrupt CaseJson yields null
// (caller skips the row and logs) — never an exception, never a half-read
// case flowing into the evaluator.
public static class HypeCaseLibrary
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static HypeCaseDetail? TryReadDetail(HypeCase hypeCase)
    {
        if (hypeCase is null || string.IsNullOrWhiteSpace(hypeCase.CaseJson))
            return null;
        try
        {
            return JsonSerializer.Deserialize<HypeCaseDetail>(hypeCase.CaseJson, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
