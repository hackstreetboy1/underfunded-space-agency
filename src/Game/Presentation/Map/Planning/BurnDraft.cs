using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Map.Planning;

/// <summary>How the planner's start time was chosen.</summary>
public enum BurnStartMode
{
    /// <summary>At the maneuver node's position (or now when no node is placed).</summary>
    Node,

    /// <summary>At the next pass over a recovery zone.</summary>
    ZonePass,
}

/// <summary>The burn being set up in the planner panel.</summary>
/// <param name="CustomAngleDeg">Degrees from prograde towards radial-out, in (-180, 180]; used with <see cref="BurnDirection.Custom"/>.</param>
/// <param name="StartTime">Null starts the burn now.</param>
/// <param name="StartZoneId">The zone whose pass sets the start, with <see cref="BurnStartMode.ZonePass"/>.</param>
public sealed record BurnDraft(
    string? EngineId,
    BurnDirection Direction,
    double CustomAngleDeg,
    double DurationS,
    MissionTime? StartTime,
    BurnStartMode StartMode = BurnStartMode.Node,
    string? StartZoneId = null)
{
    public static BurnDraft Empty { get; } = new(null, BurnDirection.Prograde, 0, 0, null);
}
