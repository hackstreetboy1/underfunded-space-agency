using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Snapshots;

/// <summary>Survival forecast result fields (GDD 6.4) for the forecast pill and its breakdown.</summary>
/// <param name="SafeAutonomyLowH">Pessimistic hours from <paramref name="ComputedAt"/>; +∞ beyond the horizon.</param>
/// <param name="SafeAutonomyHighH">Optimistic hours; +∞ when it outlasts the horizon.</param>
/// <param name="LimitingFactor">Player-facing name of what runs out first, e.g. <c>re-entry battery</c>; empty when nothing does within the horizon.</param>
public sealed record ForecastSnapshot(
    MissionTime ComputedAt,
    double SafeAutonomyLowH,
    double SafeAutonomyHighH,
    string LimitingFactor,
    ForecastConfidenceLevel Confidence)
{
    /// <summary>Why confidence is not higher, e.g. <c>fuel cell 2 output not checked</c>.</summary>
    public string? ConfidenceReason { get; init; }

    public ForecastCoverage Coverage { get; init; } = ForecastCoverage.NoPlanEnd;

    public IReadOnlyList<ForecastResourceLine> Resources { get; init; } = [];

    /// <summary>Assumptions as player-facing sentences, e.g. <c>Rations at 100 %</c>.</summary>
    public IReadOnlyList<string> Assumptions { get; init; } = [];
}

/// <summary>One resource's projection in the forecast breakdown.</summary>
public sealed record ForecastResourceLine(string Label, double LowH, double HighH, ForecastInputKnowledge Knowledge);
