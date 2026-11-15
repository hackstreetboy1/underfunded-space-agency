using Usa.Simulation.Kernel;

namespace Usa.Simulation.Forecast;

/// <summary>Safe autonomy as a range in hours from <see cref="ForecastResult.ComputedAt"/>; never a single number.</summary>
/// <param name="HighH"><see cref="double.PositiveInfinity"/> when the optimistic case outlasts the horizon.</param>
public readonly record struct AutonomyRange(double LowH, double HighH)
{
    public bool IsOpenEnded => double.IsPositiveInfinity(HighH);
}

/// <summary>Pessimistic and optimistic time to the reserve; +∞ beyond the horizon.</summary>
public sealed record ResourceForecast(string Id, double LowH, double HighH, InputKnowledge WorstKnowledge);

/// <summary>A condition the forecast relies on, for UI text: key from <see cref="ForecastAssumptionKeys"/>, plus a subject and value.</summary>
public sealed record ForecastAssumption(string Key, string Subject, double Value);

/// <param name="LimitingFactorId">Resource that runs out first in the pessimistic case; empty when none does within the horizon.</param>
/// <param name="Resources">Per-resource projections, ordered by id.</param>
public sealed record ForecastResult(
    MissionTime ComputedAt,
    AutonomyRange SafeAutonomy,
    string LimitingFactorId,
    ForecastConfidence Confidence,
    PlanCoverage Coverage,
    IReadOnlyList<ResourceForecast> Resources,
    IReadOnlyList<ForecastAssumption> Assumptions)
{
    public bool BeyondHorizon => LimitingFactorId.Length == 0;
}
