namespace Usa.Simulation.Scenario;

/// <summary>Feasibility state of an objective (GDD 10.3). <see cref="Completed"/> and <see cref="Impossible"/> are terminal.</summary>
/// <remarks>
/// Scoring's <c>ObjectiveFeasibility</c> has the same names and values. Map between them by name, never by casting.
/// </remarks>
public enum ObjectiveState
{
    Achievable = 0,
    Completed = 1,
    UnacceptableRisk = 2,
    Impossible = 3,
}
