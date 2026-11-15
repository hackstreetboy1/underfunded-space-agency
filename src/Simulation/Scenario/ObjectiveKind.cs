namespace Usa.Simulation.Scenario;

public enum ObjectiveKind
{
    /// <summary>Weighted mission objective (the weights of all primaries add up to 100 %).</summary>
    Primary,

    /// <summary>Return objective, scored through Safe return and Crew outcome rather than a weight.</summary>
    Return,
}
