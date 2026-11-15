using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Map;

/// <summary>The rotating body disc: draw the land silhouette rotated by <see cref="ScreenRotationRad"/>.</summary>
/// <param name="RotationRad">Inertial (world, counter-clockwise) angle of the prime meridian at the frame's MET.</param>
/// <param name="ScreenRotationRad">The same rotation for a Godot node or draw transform (clockwise-positive).</param>
/// <param name="AtmosphereRadiusPx">Radius of the thin atmosphere ring (entry interface altitude).</param>
public sealed record EarthDisc(
    MapPoint Centre,
    double RadiusPx,
    double AtmosphereRadiusPx,
    double RotationRad,
    double ScreenRotationRad,
    MissionTime Time);

/// <summary>A recovery zone drawn as an arc on the rim, with its label and the vehicle's next pass.</summary>
/// <param name="StartAngleRad">World angle of the west edge; the arc runs counter-clockwise (world) to <see cref="EndAngleRad"/>.</param>
/// <param name="StartScreenAngleRad">Screen angles for <c>draw_arc</c> (clockwise-positive).</param>
/// <param name="Rank">0 for the shortest recovery wait (primary), 1 for the next, ...</param>
/// <param name="NextPass">When the vehicle's ground track next enters the zone; null when no pass lies within the search horizon.</param>
/// <param name="EtaText">e.g. <c>in 34 min</c>, <c>now</c>, or the placeholder.</param>
/// <param name="DetailText">The rim label's second line: <c>pass in 6 min · recovery 0.5 h</c>, or <c>recovery 0.5 h</c> without a pass.</param>
public sealed record RecoveryZoneArc(
    string ZoneId,
    string Label,
    double StartAngleRad,
    double EndAngleRad,
    double StartScreenAngleRad,
    double EndScreenAngleRad,
    double RadiusPx,
    IReadOnlyList<MapPoint> Points,
    MapPoint LabelAnchor,
    double WaitHours,
    int Rank,
    MissionTime? NextPass,
    string EtaText,
    string DetailText);
