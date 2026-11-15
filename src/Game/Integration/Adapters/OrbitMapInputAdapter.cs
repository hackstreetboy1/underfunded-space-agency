using Usa.Game.Content;
using Usa.Game.Presentation.Map;
using Usa.Game.Presentation.Map.Planning;
using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge.Ledger;

namespace Usa.Game.Integration.Adapters;

/// <summary>
/// The orbit map's inputs over the live flight (UI.md 4.1): the body, recovery zones and entry corridor for
/// <see cref="OrbitMapModel"/>, an <see cref="OrbitMapInput"/> per frame from the <see cref="FlightTrack"/>, and the burn
/// planner's preview/proposal port over a detached copy of the ground's estimate (the session itself is never advanced) flown
/// on the ground's ledger mass (X8).
/// </summary>
public sealed class OrbitMapInputAdapter
{
    /// <summary>Icon key prefix; the vehicle id completes it (<c>map.vehicle.tandem</c>).</summary>
    public const string IconPrefix = "map.vehicle.";

    /// <summary>Default fix source (content string <c>map.sourceGroundTracking</c> replaces it in the game).</summary>
    public const string GroundTrackingSource = "ground tracking";

    /// <summary>The estimate badge on mass and other banded values (UI 2.3).</summary>
    public const string EstimateBadge = "EST";

    /// <summary>String key prefix of station names; the station id completes it (<c>map.station.pacific_island</c>).</summary>
    public const string StationKeyPrefix = "map.station.";

    private readonly FlightTrack _track;

    public OrbitMapInputAdapter(FlightTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        _track = track;
        Planner = new PlannerManeuverPort(new ManeuverPlanner(track.Flight), track.PreviewState, () => track.Mass, GroundUncertainty);
    }

    public CelestialBody Body => _track.Body;

    public RecoveryZones Zones => _track.Flight.Zones;

    public EntryCorridor Corridor => _track.Flight.Corridor;

    /// <summary>Preview and Easy-mode proposal port for the burn planner; previews fly on <see cref="PreviewMass"/>'s best value.</summary>
    public PlannerManeuverPort Planner { get; }

    /// <summary>
    /// Radial position uncertainty drawn around the path: <see cref="FlightTrack.DisplaySigma"/> × the ground estimate's radial σ.
    /// </summary>
    public double NavUncertaintyM => _track.DisplaySigma * _track.Sigma.RadialM;

    /// <summary>Along-track uncertainty for the marker's tick, same σ multiple as <see cref="NavUncertaintyM"/>.</summary>
    public double NavAlongTrackUncertaintyM => _track.DisplaySigma * _track.Sigma.AlongM;

    /// <summary>Where the ground's orbit comes from, shown with the apsis labels; null shows none.</summary>
    public string? FixSource { get; set; } = GroundTrackingSource;

    /// <summary>A tracking station's display name from its id (map rim ticks and the fix source); defaults to the id.</summary>
    public Func<string, string> StationLabel { get; set; } = static id => id;

    /// <summary>A vehicle's display name from its id (map marker label, e.g. "Gemini 8" for <c>tandem</c>); null keeps the id.</summary>
    public Func<string, string?> VehicleLabel { get; set; } = static _ => null;

    /// <summary>
    /// The ground's orbit fix (MET, and EST while a burn or entry has not been re-tracked); null without <see cref="FixSource"/> and once
    /// landed. The station is appended once ground tracking names one (<c>ground tracking, Pacific Island</c>); the orbit goes
    /// STALE after <see cref="FlightTrack.StaleAfterS"/> without a fix.
    /// </summary>
    public OrbitFix? Fix => FixSource is { } source && _track.Phase != FlightPhase.Landed
        ? new OrbitFix(_track.FixStation is { } station ? source + ", " + StationLabel(station) : source, _track.FixAt, _track.FixEstimated, _track.StaleAfterS)
        : null;

    /// <summary>
    /// Station names from <c>map.station.&lt;id&gt;</c> strings, resolved once; a station without one is labelled by its id.
    /// </summary>
    public static Func<string, string> StationLabels(ITextSource text, IReadOnlyList<TrackingStation> stations)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(stations);
        var names = new Dictionary<string, string>(stations.Count, StringComparer.Ordinal);
        foreach (var station in stations)
        {
            var key = StationKeyPrefix + station.Id;
            if (text is StringTable table)
            {
                if (table.TryGet(key, out var name)) names[station.Id] = name;
                continue;
            }
            try
            {
                names[station.Id] = text.Get(key);
            }
            catch (KeyNotFoundException)
            {
                // A text source without station names (tests, a mission whose strings predate tracking) keeps the id.
            }
        }
        return id => names.GetValueOrDefault(id, id);
    }

    /// <summary>MET of the next expected fix; null until ground tracking predicts one.</summary>
    public MissionTime? NextFixAt => _track.NextFixAt;

    /// <summary>The ground's mass band of the vehicle the planner flies (the docked stack while docked).</summary>
    public MassBand PreviewMass => _track.Mass;

    /// <summary>The planner's mass line: <c>≈ 2330–2350 kg EST · ground ledger</c>.</summary>
    public string MassText => FormatMass(_track.Mass);

    /// <summary><c>≈ low–high kg EST · source</c>; a single value when both ends format the same.</summary>
    public static string FormatMass(in MassBand mass) =>
        double.IsFinite(mass.LowKg) && double.IsFinite(mass.HighKg) && mass.HighKg > 0
            ? $"≈ {FlightFormat.MassRange(mass.LowKg, mass.HighKg)} {EstimateBadge} · {mass.Source}"
            : FlightFormat.Placeholder;

    /// <param name="zoneLabel">Rim label per zone id (<see cref="RecoveryZoneGeometry.Labels"/> over the debrief's zone names); null uses the ids.</param>
    public OrbitMapModel CreateModel(ITextSource text, OrbitMapSettings? settings = null, Func<string, string>? zoneLabel = null) =>
        new(Body, Zones, Corridor, text, settings, zoneLabel, StationLabel);

    /// <param name="plan">The burn being planned: places the maneuver node.</param>
    /// <param name="preview">Its preview, for the ghost path and landing marker.</param>
    public OrbitMapInput Build(ManeuverPlan? plan = null, ManeuverPreview? preview = null)
    {
        var vehicles = new List<MapVehicleInput>(2);
        if (_track.EstimatedCrewed is { } crewed)
            vehicles.Add(new MapVehicleInput(_track.CrewedId, crewed, IconPrefix + _track.CrewedId, Label: VehicleLabel(_track.CrewedId)));
        if (_track.EstimatedTarget is { } target && _track.TargetId is { } targetId && _track.Phase != FlightPhase.Landed)
            vehicles.Add(new MapVehicleInput(targetId, target, IconPrefix + targetId, Label: VehicleLabel(targetId)));

        return new OrbitMapInput(
            _track.Time,
            _track.EstimatedConic,
            vehicles,
            NavUncertaintyM,
            UncertaintyGrowth: 1,
            NodeTime: plan?.StartTime,
            NodeDirection: plan?.Direction ?? BurnDirection.Prograde,
            NodeCustomAngleDeg: plan?.CustomAngleDeg ?? 0,
            Preview: preview,
            Orbit: Fix,
            Stations: _track.Flight.Stations,
            NextFixAt: NextFixAt);
    }

    /// <summary>What the ground knows of the start state for the planner (X8): tracking σ and the ledger band.</summary>
    private PreviewUncertainty? GroundUncertainty()
    {
        var mass = _track.Mass;
        if (!(mass.LowKg > 0) || !(mass.HighKg >= mass.LowKg) || !double.IsFinite(mass.HighKg)) return null;
        var sigma = _track.Sigma;
        return new PreviewUncertainty(Finite(sigma.RadialM), Finite(sigma.AlongM), mass.LowKg, mass.HighKg, _track.DisplaySigma);

        static double Finite(double sigmaM) => double.IsFinite(sigmaM) && sigmaM > 0 ? sigmaM : 0;
    }

    /// <summary>Convenience for the current MET (the planner's lower bound for a node).</summary>
    public MissionTime Now => _track.Time;
}
