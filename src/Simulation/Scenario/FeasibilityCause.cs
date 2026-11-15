namespace Usa.Simulation.Scenario;

/// <summary>Why an objective was lost; scenario losses leave the scoring denominator (GDD 10.3).</summary>
/// <remarks>Scoring's <c>ObjectiveLossCause</c> has the same names and values. Map between them by name, never by casting.</remarks>
public enum FeasibilityCause
{
    Scenario = 0,
    PlayerAvoidable = 1,
}
