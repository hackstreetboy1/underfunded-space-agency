using Usa.Simulation.Data;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Flight;

/// <summary>Root of <c>missions/&lt;id&gt;/flight.json</c> (schema <c>mission_flight/1</c>).</summary>
/// <param name="Body">The body the mission starts around.</param>
/// <param name="Vehicles">The vehicles flight propagates, by role (one crewed, at most one target).</param>
/// <param name="UndockSeparationSpeedMps">Relative speed the docking springs give the two vehicles when undocking.</param>
/// <param name="EarthRotationAngleAtMetZeroDeg">θ0 in θ(t) = θ0 + ω t, the prime meridian's inertial angle at MET 0.</param>
/// <param name="OutsideZonesWaitHours">Recovery wait when landing outside every zone.</param>
/// <param name="DockingPair">The crewed and target vehicle ids when they can dock; null when they can't.</param>
/// <param name="RetroRockets">
/// The crewed vehicle's retro-rocket component ids, in firing order. Thrust, burn time, propellant and ignition
/// offsets come from the vehicle definition (the single source), supplied by the caller of <see cref="MissionFlight.Load"/>.
/// </param>
/// <param name="RetroSequence">
/// Deprecated: the retro rockets spelled out in the mission file. Still accepted (with a warning in
/// <see cref="MissionFlight.Warnings"/>) while the caller supplies no vehicle retro-rocket data; remove once it does.
/// </param>
/// <param name="Tracking">Ground tracking stations (X8); null when the mission has none, so the ground never gets a fix.</param>
/// <param name="Sun">The Sun line and shadow (<see cref="SunModel"/>); null: no Sun (mission 1). A mission's <c>session.json</c> <c>sun</c> overrides it.</param>
public sealed record MissionFlightDefinition(
    string Schema,
    string Body,
    StartOrbitDefinition StartOrbit,
    IReadOnlyList<FlightVehicleDefinition> Vehicles,
    double UndockSeparationSpeedMps,
    double EarthRotationAngleAtMetZeroDeg,
    IReadOnlyList<RecoveryZone> RecoveryZones,
    double OutsideZonesWaitHours,
    EntryCorridor EntryCorridor,
    CapsuleAerodynamics Capsule,
    PlanningSettings Planning,
    IReadOnlyList<string>? DockingPair = null,
    IReadOnlyList<ComponentId>? RetroRockets = null,
    RetroSequence? RetroSequence = null,
    TrackingDefinition? Tracking = null,
    SunDefinition? Sun = null) : IVersionedDefinition
{
    public const string SchemaTag = "mission_flight/1";
}

/// <summary>The crewed vehicle's orbit at mission start (true anomaly at the start time).</summary>
public sealed record StartOrbitDefinition(
    double PeriapsisKm,
    double ApoapsisKm,
    double ArgPeriapsisDeg,
    double TrueAnomalyDeg,
    OrbitDirection Direction);

/// <param name="PreviewHorizonS">How far past the burn a preview follows the trajectory looking for entry and landing.</param>
/// <param name="ZoneScanStepS">Start-time sampling step when the proposer searches for a recovery zone.</param>
/// <param name="ZoneSearchWindowOrbits">How many orbital periods after the earliest start the zone search covers.</param>
/// <param name="MaxBurnDurationS">Upper bound for proposed burn durations.</param>
/// <param name="StartTimeToleranceS">Resolution of the proposer's start-time bisection.</param>
public sealed record PlanningSettings(
    double PreviewHorizonS,
    double ZoneScanStepS,
    double ZoneSearchWindowOrbits,
    double MaxBurnDurationS,
    double StartTimeToleranceS);

/// <summary>Ground radar stations on the rotating body (X8 stage 2).</summary>
/// <param name="MinElevationDeg">A station tracks the vehicle above this elevation.</param>
public sealed record TrackingDefinition(double MinElevationDeg, IReadOnlyList<TrackingStation> Stations);

/// <summary>A tracking station at a longitude on the rim (the map is the orbit plane).</summary>
public sealed record TrackingStation(string Id, double LongitudeDeg);
