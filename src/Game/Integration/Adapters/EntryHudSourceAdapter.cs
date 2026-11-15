using Usa.Game.Presentation.EntryHud;
using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;
using Usa.Simulation.Knowledge;
using Usa.Simulation.Ports;
using Usa.Simulation.Session;

namespace Usa.Game.Integration.Adapters;

/// <summary>
/// <see cref="IEntryHudSource"/> over the session (UI 4.2, 4.3), as the ground knows it (X8). The g gauge and the cabin
/// altimeter are telemetered instruments read from knowledge (<c>knowledge.fact.entry.*</c>); speed, flight-path angle,
/// heating and altitude above the altimeter's ceiling are the ground prediction (EST, with a band). The sequence strip
/// comes from mission events, the running crew task and chute deploy results; the checklist from
/// <c>session.entry_checklist.armed</c>; crew health is the biomedical telemetry. The HUD model derives the time-to-altitude
/// rows from these (always estimates). Step times are the public mission events' METs; the peak g is the highest accelerometer
/// reading received; the corridor badge is the predicted entry-interface angle against the flight's corridor; the splash point
/// and zone come from the recovery forces' report facts (<c>.recovery.zone</c>, <c>.recovery.splash_longitude_deg</c>). Call <see cref="Observe(ISimEvent)"/> with every drained event and
/// <see cref="Refresh()"/> at the UI cadence (10 Hz).
/// </summary>
public sealed class EntryHudSourceAdapter : IEntryHudSource
{
    /// <summary>
    /// Relative half-widths of the predicted speed, flight-path angle and heating bands (before scaling by difficulty); the
    /// altitude band is the estimate's own radial σ × the display multiple.
    /// </summary>
    public const double PredictedSpeedBandFraction = 0.015;

    public const double PredictedFlightPathAngleBandDeg = 0.05;

    public const double PredictedHeatingBandFraction = 0.1;

    private const int ChecklistSteps = 3;
    private const string FactPrefix = "knowledge.fact.";

    /// <summary>Report fields of the recovery forces: the zone they name and the splash longitude they report.</summary>
    public const string RecoveryZoneReport = "recovery.zone";

    public const string SplashLongitudeReport = "recovery.splash_longitude_deg";

    /// <summary>The zone word a report uses for a splash outside every recovery zone.</summary>
    public const string OutsideZones = "none";

    private static readonly (EntryStep Step, string Event)[] StepEvents =
    [
        (EntryStep.EquipmentSeparation, "equipment_sep"), (EntryStep.Retrofire, "retrofire"), (EntryStep.RetroSeparation, "retro_sep"),
        (EntryStep.EntryInterface, "entry_interface"), (EntryStep.Drogue, "drogue"), (EntryStep.Main, "main"), (EntryStep.Splashdown, "splashdown"),
    ];

    private readonly MissionSession _session;
    private readonly CrewNames _names;
    private readonly double _peakHeatingProxy;
    private readonly double _bandScale;
    private readonly double _displaySigma;
    private readonly CelestialBody _body;
    private readonly string _crewedId;
    private readonly Dictionary<ChuteKind, EntryEventProgress> _chuteResults = [];
    private readonly PeakReadingTracker _peakG = new(HudSources.Accelerometer);
    private readonly EntryCorridor _corridor;
    private readonly RecoveryZones _zones;
    private readonly IReadOnlyDictionary<string, string>? _zoneNames;
    private readonly FactId? _recoveryZoneFact;
    private readonly FactId? _splashLongitudeFact;
    private readonly (string Event, string Path)[] _stepPaths;
    private double _entryAngleDeg = double.NaN;
    private double _entryAngleMetS;
    private MissionTime? _splashdownAt;

    /// <param name="zoneNames">Display name per recovery zone id (<c>debrief.json</c> <c>zoneNames</c>) for the splashdown card; null uses the ids.</param>
    public EntryHudSourceAdapter(MissionSession session, SpacecraftViewOptions? options = null, IReadOnlyDictionary<string, string>? zoneNames = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _zoneNames = zoneNames;
        _session = session;
        _names = new CrewNames(session.Data.Crew);
        _peakHeatingProxy = (options ?? SpacecraftViewOptions.Default).PeakHeatingProxy;
        _bandScale = session.Data.Difficulty.GetNumber(FlightTrack.NavUncertaintyScaleKey, 1.0);
        _displaySigma = session.Data.Navigation.DisplaySigma;
        _body = session.Data.Flight.Body;
        _crewedId = session.Data.Flight.Vehicles.CrewedId;
        _corridor = session.Data.Flight.Corridor;
        _zones = session.Data.Flight.Zones;
        _recoveryZoneFact = ReportFacts.Resolve(session.Data.Facts, RecoveryZoneReport, RecoveryZoneReport);
        _splashLongitudeFact = ReportFacts.Resolve(session.Data.Facts, SplashLongitudeReport, SplashLongitudeReport);
        _stepPaths = [.. StepEvents.Select(s => (s.Event, $"mission.event.{s.Event}.met_s"))];
        var capsule = session.Data.Flight.Capsule;
        Limits = EntryHudLimits.Default with
        {
            DrogueDeployAltitudeM = capsule.Drogue.NominalDeployAltitudeM,
            MainDeployAltitudeM = capsule.Main.NominalDeployAltitudeM,
            AltimeterCeilingM = EntryFacts.AltimeterCeilingKm * 1000,
        };
        Refresh();
    }

    public EntryHudLimits Limits { get; }

    public EntryFlightInputs Flight { get; private set; }

    public EntrySequenceInputs Sequence { get; private set; } = EntrySequenceInputs.NotStarted;

    public EntryChecklistInputs Checklist { get; private set; }

    public IReadOnlyList<EntryCrewInputs> Crew { get; private set; } = [];

    public SplashdownInputs Splashdown { get; private set; } = SplashdownInputs.None;

    public void Observe(ISimEvent simEvent)
    {
        switch (simEvent)
        {
            case ChuteDeployed chute:
                var progress = chute.Result switch
                {
                    ChuteDeployResult.Deployed => EntryEventProgress.Done,
                    ChuteDeployResult.TooHigh or ChuteDeployResult.TooFast => EntryEventProgress.Failed,
                    _ => (EntryEventProgress?)null,
                };
                if (progress is { } value) _chuteResults[chute.Chute] = value;
                break;
            case Usa.Simulation.Flight.Splashdown landed:
                _splashdownAt ??= landed.Time;
                break;
        }
    }

    public void Refresh()
    {
        var now = _session.Now;
        Flight = FlightInputs(now);

        var task = Text("crew.task.id");
        Sequence = new EntrySequenceInputs(
            Step("equipment_sep", task == "jettison_equipment"),
            Step("retrofire", task == "retrofire" || AnyRetroBurning()),
            Step("retro_sep", task == "jettison_retro"),
            Step("entry_interface", false),
            Chute(ChuteKind.Drogue, "drogue", task == "deploy_drogue"),
            Chute(ChuteKind.Main, "main", task == "deploy_main"),
            Step("splashdown", false)) { Times = StepTimes() };

        var armed = Bool("session.entry_checklist.armed");
        var completed = (armed ? 1 : 0) + (Sequence.Drogue == EntryEventProgress.Done ? 1 : 0) + (Sequence.Main == EntryEventProgress.Done ? 1 : 0);
        Checklist = new EntryChecklistInputs(armed || task == "entry_checklist", armed ? completed : 0, ChecklistSteps);

        var crew = new List<EntryCrewInputs>();
        foreach (var id in _session.Data.Scenario.Mission.Crew)
            crew.Add(new EntryCrewInputs(_names.ShortName(id), Bool($"crew.{id.Value}.alive") ? Number($"crew.{id.Value}.health") : 0));
        Crew = crew;

        var landed = Bool("flight.landed");
        if (landed && _splashdownAt is null && Number("mission.event.splashdown.met_s") is var met && met >= 0)
            _splashdownAt = MissionTime.FromSeconds(met);
        Splashdown = landed ? SplashdownReport(now) : SplashdownInputs.None;
    }

    private EntryFlightInputs FlightInputs(MissionTime now)
    {
        var met = now.Seconds;
        var estimate = _session.NavEstimate(_crewedId);
        var predicted = GroundEntryPrediction.Of(estimate, _body);
        var g = Instrument(EntryFacts.GLoad, HudSources.Accelerometer, unitScale: 1);
        var heating01 = Math.Clamp(predicted.HeatingProxy / _peakHeatingProxy, 0, 1);
        var heatingBand = heating01 * PredictedHeatingBandFraction * _bandScale;
        return new EntryFlightInputs(
            Prediction(predicted.AltitudeM, predicted.AltitudeSigmaM * _displaySigma, met, floor: 0),
            Instrument(EntryFacts.AltitudeKm, HudSources.CabinAltimeter, unitScale: 1000),
            Prediction(predicted.SpeedMps, Math.Abs(predicted.SpeedMps) * PredictedSpeedBandFraction * _bandScale, met, floor: 0),
            Prediction(predicted.FlightPathAngleDeg, PredictedFlightPathAngleBandDeg * _bandScale, met),
            g,
            double.IsFinite(predicted.HeatingProxy)
                ? HudReadout.GroundPrediction(heating01, Math.Max(0, heating01 - heatingBand), Math.Min(1, heating01 + heatingBand), met)
                : HudReadout.Unknown(HudSources.GroundPredict),
            met,
            _peakG.Observe(g),
            Corridor(estimate, met));
    }

    /// <summary>
    /// The ground's predicted flight-path angle at the entry interface on its estimated conic, with the γ band, against the
    /// corridor; held at its last value once the estimate is inside the atmosphere. Null while no entry is predicted.
    /// </summary>
    private EntryCorridorInputs? Corridor(NavEstimate? estimate, double met)
    {
        if (estimate is not null && estimate.Phase is FlightPhase.Coast or FlightPhase.Powered)
        {
            var conic = estimate.Conic;
            var interfaceRadiusM = _body.RadiusM + _corridor.InterfaceAltitudeM;
            _entryAngleDeg = double.NaN;
            if (!conic.IsHyperbolic && conic.PeriapsisM < interfaceRadiusM && conic.ApoapsisM > interfaceRadiusM)
            {
                // The descending crossing lies before periapsis (negative true anomaly).
                _entryAngleDeg = conic.FlightPathAngleAtTrueAnomaly(-conic.TrueAnomalyAtRadius(interfaceRadiusM)) * Angles.RadToDeg;
                _entryAngleMetS = met;
            }
        }
        if (!double.IsFinite(_entryAngleDeg)) return null;
        var band = PredictedFlightPathAngleBandDeg * _bandScale;
        return new EntryCorridorInputs(
            HudReadout.GroundPrediction(_entryAngleDeg, _entryAngleDeg - band, _entryAngleDeg + band, _entryAngleMetS),
            _corridor.SteepestFlightPathAngleDeg,
            _corridor.ShallowestFlightPathAngleDeg);
    }

    /// <summary>When each return event happened (<c>mission.event.&lt;id&gt;.met_s</c>, public mission events); unfired steps have none.</summary>
    private Dictionary<EntryStep, EntryStepTime>? StepTimes()
    {
        Dictionary<EntryStep, EntryStepTime>? times = null;
        for (var i = 0; i < _stepPaths.Length; i++)
        {
            var metS = Number(_stepPaths[i].Path);
            if (double.IsFinite(metS) && metS >= 0) (times ??= [])[StepEvents[i].Step] = new EntryStepTime(metS);
        }
        return times;
    }

    /// <summary>
    /// After splashdown: the recovery forces' report (zone word, splash longitude) when it has come in, the zone's authored wait
    /// for that zone, and the splashdown MET of the public mission event. Nothing here reads where the capsule truly landed.
    /// </summary>
    private SplashdownInputs SplashdownReport(MissionTime now)
    {
        var zoneWord = ReportFacts.Latest(_session, _recoveryZoneFact)?.Qualitative;
        var longitude = ReportFacts.Latest(_session, _splashLongitudeFact)?.Value;
        var zone = string.IsNullOrEmpty(zoneWord) || zoneWord == OutsideZones ? null : _zones.Find(zoneWord);
        bool? inZone = string.IsNullOrEmpty(zoneWord) ? null : zone is not null;
        var splashMet = Number("mission.event.splashdown.met_s");
        return new SplashdownInputs(
            true,
            zone is null ? null : ZoneName(zone.Id, _zoneNames),
            null,
            zone is null ? null : zone.WaitHours * 3600,
            _splashdownAt is { } at ? Math.Max(0, (now.Milliseconds - at.Milliseconds) / 1000.0) : 0,
            Bool("mission.event.recovered.fired"),
            inZone,
            longitude,
            double.IsFinite(splashMet) && splashMet >= 0 ? splashMet : null);
    }

    private static HudReadout Prediction(double value, double halfWidth, double met, double floor = double.NegativeInfinity) =>
        double.IsFinite(value)
            ? HudReadout.GroundPrediction(value, Math.Max(floor, value - halfWidth), value + halfWidth, met)
            : HudReadout.Unknown(HudSources.GroundPredict);

    /// <summary>
    /// A telemetered instrument fact (<c>knowledge.fact.&lt;id&gt;.state|value|met_s</c>): Known while fresh, Stale after the
    /// fact's stale time, Unknown before the first reading or when the mission's catalog has no such fact.
    /// </summary>
    private HudReadout Instrument(FactId fact, string source, double unitScale)
    {
        var prefix = FactPrefix + fact.Value + ".";
        var value = Number(prefix + "value") * unitScale;
        var met = Number(prefix + "met_s");
        if (!double.IsFinite(value) || !(met >= 0)) return HudReadout.Unknown(source);
        return Text(prefix + "state") switch
        {
            "known" or "conflicting" => HudReadout.Measured(value, source, met),
            "stale" => HudReadout.Measured(value, source, met, stale: true),
            "estimated" => HudReadout.Estimate(value, value, value, source, met),
            _ => HudReadout.Unknown(source),
        };
    }

    private EntryEventProgress Step(string missionEvent, bool running) =>
        Bool($"mission.event.{missionEvent}.fired") ? EntryEventProgress.Done
        : running ? EntryEventProgress.InProgress
        : EntryEventProgress.NotStarted;

    private EntryEventProgress Chute(ChuteKind kind, string missionEvent, bool running)
    {
        if (Bool($"mission.event.{missionEvent}.fired")) return EntryEventProgress.Done;
        if (_chuteResults.TryGetValue(kind, out var result)) return result;
        return running ? EntryEventProgress.InProgress : EntryEventProgress.NotStarted;
    }

    private bool AnyRetroBurning()
    {
        foreach (var vehicle in _session.Data.Vehicles)
        {
            foreach (var component in vehicle.Components)
            {
                if (component.RetroRocket is not null && Bool($"{component.Id.Value}.burning")) return true;
            }
        }
        return false;
    }

    /// <summary><c>the secondary West Pacific zone</c> → <c>Secondary West Pacific zone</c>; an id without a name reads <c>secondary west pacific</c>.</summary>
    public static string ZoneName(string zoneId, IReadOnlyDictionary<string, string>? zoneNames)
    {
        ArgumentNullException.ThrowIfNull(zoneId);
        if (zoneNames is null || !zoneNames.TryGetValue(zoneId, out var name) || string.IsNullOrWhiteSpace(name)) return zoneId.Replace('_', ' ');
        const string Article = "the ";
        var trimmed = name.Trim();
        if (trimmed.StartsWith(Article, StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[Article.Length..];
        return trimmed.Length == 0 ? zoneId : char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
    }

    private bool Bool(string path) => _session.State.TryGet(path, out var value) && value.Kind == StateValueKind.Bool && value.AsBool();

    private double Number(string path) => Number(_session.State, path);

    private string Text(string path) => Text(_session.State, path);

    private static double Number(IStateQuery state, string path) =>
        state.TryGet(path, out var value) && value.Kind == StateValueKind.Number ? value.AsNumber() : double.NaN;

    private static string Text(IStateQuery state, string path) =>
        state.TryGet(path, out var value) && value.Kind == StateValueKind.Text ? value.AsText() : string.Empty;
}
