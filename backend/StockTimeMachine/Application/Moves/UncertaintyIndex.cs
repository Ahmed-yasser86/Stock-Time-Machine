namespace StockTimeMachine;

// Decision Context (dc-v1) result model: a transparent 0–100 gauge of how
// thin, conflicting, or unstable the knowable evidence is around the decision
// window, plus a Confidence grade so precision never masquerades as certainty.
// Higher score = more uncertain. Every term is visible in Components (each
// with a measured | empty | insufficient | unavailable Status); there are no
// hidden inputs and no thresholds that advise action. See
// DecisionContextCalculator for the methodology.
public class UncertaintyComponent
{
    public string Name { get; set; } = "";
    public double Weight { get; set; }
    public double Value { get; set; }
    public string Detail { get; set; } = "";
    // measured | empty | insufficient | unavailable — never a bare number.
    public string Status { get; set; } = "measured";
}

public class UncertaintyIndex
{
    public double Score { get; set; }
    public List<UncertaintyComponent> Components { get; set; } = new();
    // Confidence grade + calculation version + sentiment model provenance.
    // Additive: old readers ignore them.
    public string Confidence { get; set; } = "Low";
    public string Version { get; set; } = DecisionContextCalculator.Version;
    public string Model { get; set; } = "";
}
