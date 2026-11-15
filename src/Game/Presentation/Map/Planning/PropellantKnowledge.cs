using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Map.Planning;

/// <summary>How the ground knows a planner input (GDD 4.1), mirrored from the fact's knowledge state.</summary>
public enum PlannerKnowledgeState
{
    /// <summary>Never reported: no value is shown and the preview assumes the full burn.</summary>
    Unknown,

    /// <summary>A fresh reading or report.</summary>
    Known,

    /// <summary>A reading older than the fact's stale-after time: shown dimmed with <c>STALE 12m</c>.</summary>
    Stale,

    /// <summary>A coarse band or a ground-model estimate (GDD 4.2): shown as <c>EST</c> and a range.</summary>
    Estimated,
}

/// <summary>
/// What the ground knows about the propellant an engine can still use (X8, GDD 4.1-4.2). The planner previews and
/// displays from this, never from the engine's true <c>AvailablePropellantKg</c>.
/// </summary>
/// <param name="LowKg">Lower end of the known range; equal to <paramref name="HighKg"/> for a single reading.</param>
/// <param name="ObservedAt">MET of the reading or report; null for an estimate with no single source time.</param>
/// <param name="Source">Who or what supplied it, e.g. <c>reported by June Okafor</c> or <c>downlink band</c>.</param>
public sealed record PropellantKnowledge(
    PlannerKnowledgeState State, double LowKg, double HighKg, MissionTime? ObservedAt = null, string? Source = null)
{
    public static PropellantKnowledge Unknown { get; } = new(PlannerKnowledgeState.Unknown, 0, 0);

    /// <summary>A single reported or read value; <paramref name="stale"/> when it is older than the fact's stale-after time.</summary>
    public static PropellantKnowledge Reported(double kg, MissionTime observedAt, string source, bool stale = false) =>
        new(stale ? PlannerKnowledgeState.Stale : PlannerKnowledgeState.Known, kg, kg, observedAt, source);

    /// <summary>A band, e.g. the downlink's <c>LOW</c> band mapped to its kilogram range.</summary>
    public static PropellantKnowledge Estimated(double lowKg, double highKg, string? source = null, MissionTime? observedAt = null) =>
        new(PlannerKnowledgeState.Estimated, Math.Min(lowKg, highKg), Math.Max(lowKg, highKg), observedAt, source);

    public bool HasValue => State != PlannerKnowledgeState.Unknown && double.IsFinite(LowKg) && double.IsFinite(HighKg);

    /// <summary>The amount the preview assumes: the low end of a band (conservative), the reading otherwise.</summary>
    public double PlanningKg => Math.Max(0, LowKg);
}
