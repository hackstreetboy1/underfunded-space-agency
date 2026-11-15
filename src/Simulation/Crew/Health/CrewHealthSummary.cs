using Usa.Simulation.Kernel;

namespace Usa.Simulation.Crew.Health;

/// <summary>One astronaut's health story for the debrief and scoring (GDD 10.1, 10.4).</summary>
/// <param name="MissionDamage">All damage since launch, including damage later recovered.</param>
/// <param name="HasComplication">Mission damage exceeded the complication threshold.</param>
/// <param name="Causes">Causes that did damage, largest first.</param>
public sealed record CrewHealthSummary(
    CrewId Crew,
    bool Alive,
    double LaunchHealth,
    double WorstHealth,
    double CurrentHealth,
    double MissionDamage,
    bool HasComplication,
    IReadOnlyList<HealthCauseTotal> Causes);

public sealed record HealthCauseTotal(HealthCause Cause, string Label, double Damage);
