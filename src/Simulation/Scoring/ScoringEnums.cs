namespace Usa.Simulation.Scoring;

/// <summary>How an objective stood when the mission ended.</summary>
/// <remarks>
/// Mapped by name from Scenario's <c>ObjectiveState</c>: Completed, UnacceptableRisk and Impossible keep their name;
/// an objective still Achievable when the mission ended is <see cref="Abandoned"/>.
/// </remarks>
public enum ObjectiveFinalState
{
    Completed,

    /// <summary>The mission ended while the objective was still safely achievable.</summary>
    Abandoned,

    Impossible,

    UnacceptableRisk,
}

/// <summary>Feasibility values recorded by the scenario's feasibility tracker.</summary>
/// <remarks>Same names and values as Scenario's <c>ObjectiveState</c>. Map between them by name, never by casting.</remarks>
public enum ObjectiveFeasibility
{
    Achievable = 0,
    Completed = 1,
    UnacceptableRisk = 2,
    Impossible = 3,
}

/// <remarks>Same names and values as Scenario's <c>FeasibilityCause</c>. Map between them by name, never by casting.</remarks>
public enum ObjectiveLossCause
{
    Scenario = 0,

    /// <summary>A known acceptable alternative existed at the decision time (GDD 10.3).</summary>
    PlayerAvoidable = 1,
}

public enum RedundancyStatus
{
    NeverLost,
    Restored,
    Lost,
}

public enum ScoreDimension
{
    CrewOutcome,
    SafeReturn,
    Objectives,
    VehicleCondition,
    DecisionQuality,
}

public enum SPlusPath
{
    None,

    /// <summary>Every objective completed, crew home without complications.</summary>
    A,

    /// <summary>Objectives lost for scenario reasons were abandoned, the rest completed, crew home without complications.</summary>
    B,
}
