using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Map;

/// <summary>What the map shows at one moment. Everything comes from the ground's view of the flight.</summary>
/// <param name="CurrentConic">The tracked vehicle's coast conic; null hides the path (e.g. while landed).</param>
/// <param name="NavUncertaintyM">Position uncertainty at the vehicle; 0 draws no band.</param>
/// <param name="UncertaintyGrowth">The band's half width at the end of the path, as a multiple of its start.</param>
/// <param name="NodeTime">Planned burn start; null hides the maneuver node.</param>
/// <param name="Preview">Burn preview for the ghost path and landing marker.</param>
/// <param name="Orbit">Source, MET and state of <paramref name="CurrentConic"/>; null leaves the apsis labels without a meta line.</param>
/// <param name="Stations">Ground tracking stations, drawn as ticks on the rim (X8); null or empty draws none.</param>
/// <param name="NextFixAt">When the ground expects its next radar fix, for the <c>Next fix in 9 min</c> chip; null hides it.</param>
public sealed record OrbitMapInput(
    MissionTime Now,
    Conic? CurrentConic,
    IReadOnlyList<MapVehicleInput> Vehicles,
    double NavUncertaintyM = 0,
    double UncertaintyGrowth = 1,
    MissionTime? NodeTime = null,
    BurnDirection NodeDirection = BurnDirection.Prograde,
    double NodeCustomAngleDeg = 0,
    ManeuverPreview? Preview = null,
    OrbitFix? Orbit = null,
    IReadOnlyList<TrackingStation>? Stations = null,
    MissionTime? NextFixAt = null);

/// <summary>Everything a Godot <c>_Draw</c> needs for one frame of the orbit map, in screen pixels.</summary>
/// <param name="Zones">Empty when the mission has no recovery zones.</param>
/// <param name="Corridor">Null unless the current path descends through the entry interface; the preview's band is <see cref="GhostPath.Corridor"/>.</param>
/// <param name="Orbit">The orbit chip (apsides, period, fix meta and state); null without a current conic.</param>
/// <param name="CorridorPeriapsisKm">Periapsis that enters the corridor's centre from the estimated orbit (<see cref="CorridorPeriapsis"/>); null when none.</param>
public sealed record OrbitMapFrame(
    MapTransform Transform,
    EarthDisc Earth,
    IReadOnlyList<RecoveryZoneArc> Zones,
    MapPolyline? CurrentPath,
    ArcEnd CurrentPathEnd,
    IReadOnlyList<ApsisMarker> Apsides,
    EntryCorridorBand? Corridor,
    UncertaintyBand? Uncertainty,
    ManeuverNodeView? Node,
    GhostPath? Ghost,
    IReadOnlyList<MapVehicleMarker> Vehicles,
    bool LabelsVisible,
    IReadOnlyList<StationTick>? Stations = null,
    string? NextFixText = null,
    OrbitChip? Orbit = null,
    double? CorridorPeriapsisKm = null);

/// <summary>A tracking station on the rim: a short radial tick from the surface outwards, turning with the body.</summary>
/// <param name="Label">Display name (station names come from text, keyed <c>map.station.&lt;id&gt;</c> by the host's label function).</param>
public sealed record StationTick(string Id, string Label, MapPoint Surface, MapPoint Outer, double LongitudeDeg);
