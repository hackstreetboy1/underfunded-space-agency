using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;
using Usa.Simulation.Knowledge;
using Usa.Simulation.Knowledge.Catalog;
using Usa.Simulation.Knowledge.Ledger;
using Usa.Simulation.Ports;
using Usa.Simulation.Session;
using Usa.Simulation.Vehicle.Definitions;

namespace Usa.Game.Integration.Adapters;

/// <summary>
/// The ground's tracking picture of the flight (X8 stage 2): the session's ground-tracking estimate of each vehicle
/// (<see cref="FlightTrackView.CrewedEstimate"/>: the last radar fix propagated with the commanded burns), its fix, station,
/// σ and staleness, the ground's entry prediction from it, and the ground mass band (<c>nav.&lt;vehicle&gt;.mass_low_kg</c>
/// and <c>mass_high_kg</c>, or the local ledger when the session serves none). Call <see cref="Update"/> once per frame.
/// </summary>
/// <remarks>
/// The map, the planner previews and the entry HUD use only the estimate. <see cref="Crewed"/>, <see cref="Target"/> and
/// <see cref="AltitudeKm"/> are the rendered scene of the 3D view (what the camera shows), never a readout.
/// </remarks>
public sealed class FlightTrack
{
    /// <summary>Mission event after which the retro rockets are spent.</summary>
    public const string RetrofireEvent = "retrofire";

    /// <summary>Difficulty key scaling σ and the ledger tolerance (Easy 0.5, Normal 1.0).</summary>
    public const string NavUncertaintyScaleKey = "nav_uncertainty_scale";

    /// <summary>Read-only ground estimate paths, <c>nav.&lt;vehicle&gt;.&lt;field&gt;</c>.</summary>
    public const string NavPrefix = "nav.";

    private readonly MissionSession _session;
    private readonly MissionFlight _flight;
    private readonly IStateQuery _state;
    private readonly VehicleLedger _crewedLedger;
    private readonly VehicleLedger? _targetLedger;
    private readonly CommandedBurn[] _burns;
    private readonly List<KnownDraw> _draws = [];
    private readonly string _massLowPath;
    private readonly string _massHighPath;
    private OrbitPropagator? _preview;
    private bool _previewBuilt;
    private MissionTime? _updatedAt;
    private NavSigma? _sigma;
    private double _sigmaAlongM;
    private double _sigmaRadialM;
    private MassBand? _mass;

    public FlightTrack(MissionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _flight = session.Data.Flight;
        _state = session.State;
        var navigation = session.Data.Navigation;
        UncertaintyScale = session.Data.Difficulty.GetNumber(NavUncertaintyScaleKey, 1.0);
        DisplaySigma = navigation.DisplaySigma;
        StaleAfterS = navigation.StaleAfterS;
        LedgerTolerance = navigation.LedgerFraction * UncertaintyScale;

        var crewed = Vehicle(session, _flight.Vehicles.CrewedId)
            ?? throw new InvalidOperationException($"No vehicle data for the crewed vehicle '{_flight.Vehicles.CrewedId}'.");
        _crewedLedger = new VehicleLedger(crewed, session.Data.Facts, LedgerTolerance);
        _targetLedger = _flight.Vehicles.TargetId is { } targetId && Vehicle(session, targetId) is { } target
            ? new VehicleLedger(target, session.Data.Facts, LedgerTolerance)
            : null;
        _burns = CommandedBurn.From(session);
        _massLowPath = NavPrefix + CrewedId + ".mass_low_kg";
        _massHighPath = NavPrefix + CrewedId + ".mass_high_kg";

        FixAt = session.Now;
        Update(session.Now);
    }

    public CelestialBody Body => _flight.Body;

    public MissionFlight Flight => _flight;

    /// <summary>The crewed vehicle's id (<see cref="FlightVehicles.CrewedId"/>).</summary>
    public string CrewedId => _flight.Vehicles.CrewedId;

    /// <summary>The target vehicle's id; null when the mission has none.</summary>
    public string? TargetId => _flight.Vehicles.TargetId;

    /// <summary>Difficulty <c>nav_uncertainty_scale</c> (1 when the profile has no such key).</summary>
    public double UncertaintyScale { get; }

    /// <summary>Displayed bands are this many σ (<c>navigation.json</c> <c>displaySigma</c>).</summary>
    public double DisplaySigma { get; }

    /// <summary>Fractional tolerance of the design masses (<c>ledgerFraction</c> × <see cref="UncertaintyScale"/>).</summary>
    public double LedgerTolerance { get; }

    /// <summary>The ledger spec of the crewed vehicle, built once from mission data.</summary>
    public GroundMassLedgerSpec CrewedLedgerSpec => _crewedLedger.Ledger.Spec;

    /// <summary>The ledger spec of the target vehicle; null without one.</summary>
    public GroundMassLedgerSpec? TargetLedgerSpec => _targetLedger?.Ledger.Spec;

    /// <summary>The latest view, as of <see cref="Time"/>.</summary>
    public FlightTrackView View { get; private set; } = null!;

    public MissionTime Time { get; private set; }

    /// <summary>The flight phase the ground commands and hears confirmed (coast, powered, atmospheric, landed).</summary>
    public FlightPhase Phase => View.Crewed.Phase;

    public bool Docked => View.Docked;

    /// <summary>The ground's estimate of the crewed vehicle (the docked stack while docked); null without tracking.</summary>
    public NavEstimate? Estimate => View.CrewedEstimate;

    /// <summary>The estimated crewed state, where the map draws the marker; null without an estimate.</summary>
    public StateVector? EstimatedCrewed => Estimate?.State;

    /// <summary>The estimated conic the map draws; null once landed or without an estimate.</summary>
    public Conic? EstimatedConic => Phase == FlightPhase.Landed ? null : Estimate?.Conic;

    /// <summary>The ground's estimate of the target while it flies free; null while docked, without a target or untracked.</summary>
    public StateVector? EstimatedTarget => View.Docked ? null : View.TargetEstimate?.State;

    /// <summary>The 3D view's crewed vehicle (Tandem, or the docked stack): the rendered scene, never a readout.</summary>
    public StateVector Crewed => View.Crewed.State;

    /// <summary>The 3D view's target position while apart; null while docked or without a target.</summary>
    public StateVector? Target => View.Docked ? null : View.Target?.State;

    /// <summary>The 3D view's camera altitude for the Earth backdrop.</summary>
    public double AltitudeKm => View.Crewed.AltitudeM / 1000.0;

    /// <summary>The ground's entry prediction from <see cref="Estimate"/> (EST); NaN values without one.</summary>
    public GroundEntryPrediction Prediction { get; private set; } = GroundEntryPrediction.None;

    /// <summary>The ground's predicted heat-rate proxy (√ρ·v³) for the entry glow and audio; 0 without a prediction.</summary>
    public double HeatingProxy => double.IsFinite(Prediction.HeatingProxy) ? Prediction.HeatingProxy : 0;

    /// <summary>MET of the ground's latest orbit fix.</summary>
    public MissionTime FixAt { get; private set; }

    /// <summary>A commanded burn has been applied since the fix: the orbit is shown as EST · ground predict.</summary>
    public bool FixEstimated { get; private set; }

    /// <summary>The station that made the latest fix; null for the launch fix.</summary>
    public string? FixStation { get; private set; }

    /// <summary>MET of the next expected fix; null while it cannot be predicted (burning, in the atmosphere).</summary>
    public MissionTime? NextFixAt { get; private set; }

    /// <summary>An estimate with no fix for this long is stale (<c>navigation.json</c> <c>staleAfterS</c>).</summary>
    public double StaleAfterS { get; }

    /// <summary>Seconds since the latest fix, as of <see cref="Time"/>.</summary>
    public double FixAgeS => Math.Max(0, (Time.Milliseconds - FixAt.Milliseconds) / 1000.0);

    /// <summary>No fix for longer than <see cref="StaleAfterS"/> while still flying: the orbit chip shows STALE.</summary>
    public bool Stale { get; private set; }

    /// <summary>The ground's 1σ position uncertainty of the crewed vehicle (difficulty-scaled); built on first read after a change.</summary>
    public NavSigma Sigma => _sigma ??= new NavSigma(_sigmaAlongM, _sigmaRadialM);

    /// <summary>
    /// The ground's mass band of the crewed vehicle (the docked stack while docked): <c>nav.&lt;vehicle&gt;.mass_low_kg</c> and
    /// <c>mass_high_kg</c>, or the local ledger when not served. Previews fly on its <see cref="MassBand.BestKg"/>.
    /// </summary>
    public MassBand Mass => _mass ??= NavMass(Time) ?? LedgerMass(Time);

    /// <summary>Commanded-burn draws the ledger applies (engine spec × commanded duration), in command order.</summary>
    public IReadOnlyList<KnownDraw> KnownDraws => _draws;

    public void Update(MissionTime now)
    {
        if (_updatedAt == now) return;
        _updatedAt = now;
        Time = now;
        View = _session.FlightTrack();
        _previewBuilt = false;
        _preview = null;
        ObserveCommandedBurns(now);

        var estimate = Estimate;
        Prediction = GroundEntryPrediction.Of(estimate, Body);
        if (estimate is not null)
        {
            FixEstimated = estimate.Estimated;
            if (estimate.FixAt is { } fixAt) FixAt = fixAt;
            FixStation = estimate.FixStationId is { Length: > 0 } station ? station : null;
            NextFixAt = estimate.NextFixAt;
            Stale = estimate.Stale && Phase != FlightPhase.Landed;
            if (_sigmaAlongM != estimate.SigmaAlongM || _sigmaRadialM != estimate.SigmaRadialM)
            {
                _sigmaAlongM = estimate.SigmaAlongM;
                _sigmaRadialM = estimate.SigmaRadialM;
                _sigma = null;
            }
        }
        // Read on demand (planner preview, mass line): the ledger and nav paths are not per-frame work.
        _mass = null;
    }

    /// <summary>
    /// A detached propagator at the ground's estimate with the ground's mass, for burn previews and proposals; null while
    /// landed or mid-burn (<see cref="ManeuverPlanner.CanPreview"/>) or without an estimate.
    /// </summary>
    public OrbitPropagator? PreviewState()
    {
        if (_previewBuilt) return _preview;
        _previewBuilt = true;
        _preview = null;
        if (Phase is FlightPhase.Landed or FlightPhase.Powered || Estimate is not { } estimate) return null;
        if (estimate.Phase is FlightPhase.Landed or FlightPhase.Powered) return null;
        var mass = Mass.BestKg;
        if (!(mass > 0) || !double.IsFinite(mass)) return null;
        var capsule = _flight.Capsule;
        var beta = Bool("flight.main_deployed") ? capsule.Main.BallisticCoefficientKgPerM2
            : Bool("flight.drogue_deployed") ? capsule.Drogue.BallisticCoefficientKgPerM2
            : capsule.BallisticCoefficientKgPerM2;
        _preview = new OrbitPropagator(CrewedId, Body, estimate.Time, estimate.State, mass, beta, _flight.Zones);
        return _preview;
    }

    /// <summary>The ledger's estimate for <paramref name="vehicleId"/> (crewed or target) as of now; null for another id.</summary>
    public GroundMassEstimate? LedgerEstimate(string vehicleId)
    {
        var ledger = vehicleId == CrewedId ? _crewedLedger : vehicleId == TargetId ? _targetLedger : null;
        return ledger?.Ledger.Estimate(_session.Knowledge, KnownEvents());
    }

    /// <summary>The session's ground mass band (<c>nav.&lt;crewed&gt;.mass_low_kg/mass_high_kg</c>); null when not served.</summary>
    private MassBand? NavMass(MissionTime now)
    {
        var low = NumberOrNaN(_massLowPath);
        var high = NumberOrNaN(_massHighPath);
        if (!(low > 0) || !(high >= low) || !double.IsFinite(high)) return null;
        return new MassBand(low, (low + high) / 2, high, LedgerSources.GroundLedger, now, KnowledgeState.Estimated);
    }

    /// <summary>The crewed vehicle's ledger band, plus the target's while docked.</summary>
    private MassBand LedgerMass(MissionTime now)
    {
        var events = KnownEvents();
        var (low, best, high) = _crewedLedger.Estimate(_session.Knowledge, events, _draws);
        if (Docked && _targetLedger is { } target)
        {
            var (targetLow, targetBest, targetHigh) = target.Estimate(_session.Knowledge, events, _draws);
            low += targetLow;
            best += targetBest;
            high += targetHigh;
        }
        return new MassBand(low, best, high, LedgerSources.GroundLedger, now, KnowledgeState.Estimated);
    }

    /// <summary>
    /// Jettisoned sections (their staging event fired), spent retro rockets (after <see cref="RetrofireEvent"/>) and the
    /// commanded draws. Mission events are what the ground commanded and heard confirmed, not vehicle state.
    /// </summary>
    private KnownMassEvents KnownEvents()
    {
        var removed = new HashSet<string>(StringComparer.Ordinal);
        _crewedLedger.AddRemoved(removed, Fired);
        _targetLedger?.AddRemoved(removed, Fired);
        return new KnownMassEvents(removed, _draws);
    }

    /// <summary>Records each commanded burn once, when the session starts it: engine-spec mass flow × commanded duration.</summary>
    private void ObserveCommandedBurns(MissionTime now)
    {
        foreach (var burn in _burns)
        {
            var commandedS = burn.CommandedDurationS(_state);
            if (commandedS > 0 && !burn.Active)
            {
                foreach (var (tank, kgPerS) in burn.TankFlows) _draws.Add(new KnownDraw(tank, now, kgPerS * commandedS));
            }
            burn.Active = commandedS > 0;
        }
    }

    private bool Fired(string missionEvent) => Bool($"mission.event.{missionEvent}.fired");

    private bool Bool(string path) => _state.TryGet(path, out var value) && value.Kind == StateValueKind.Bool && value.AsBool();

    private double NumberOrNaN(string path) =>
        _state.TryGet(path, out var value) && value.Kind == StateValueKind.Number ? value.AsNumber() : double.NaN;

    private static VehicleDefinition? Vehicle(MissionSession session, string id)
    {
        foreach (var vehicle in session.Data.Vehicles)
        {
            if (vehicle.Id == id) return vehicle;
        }
        return null;
    }

    /// <summary>
    /// One vehicle's ledger: sections and retro rockets as design masses, each tank with a number fact sourced from it as a
    /// ledger tank. A tank no fact reports (no gauge the ground can read) counts at its design load less the commanded draws,
    /// with the design-mass tolerance.
    /// </summary>
    private sealed class VehicleLedger
    {
        private const double ReadingToleranceKg = 5;

        private readonly (string Id, string Section, double DesignKg)[] _unmonitored;
        private readonly (string Section, string Event)[] _jettisons;
        private readonly string[] _retroRockets;
        private readonly double _tolerance;

        public VehicleLedger(VehicleDefinition vehicle, FactCatalog facts, double tolerance)
        {
            _tolerance = tolerance;
            var dry = new List<LedgerDryMass>();
            var tanks = new List<LedgerTank>();
            var unmonitored = new List<(string, string, double)>();
            var rockets = new List<string>();
            foreach (var section in vehicle.Sections) dry.Add(new LedgerDryMass(section.Id, section.Id, section.DryMassKg));
            foreach (var component in vehicle.Components)
            {
                if (component.RetroRocket is { } retro)
                {
                    dry.Add(new LedgerDryMass(component.Id.Value, component.Section, retro.PropellantKg));
                    rockets.Add(component.Id.Value);
                }
                if (component.Tank is not { } tank) continue;
                if (TankFact(facts, component.Id) is { } fact)
                    tanks.Add(new LedgerTank(component.Id.Value, component.Section, fact, tank.CapacityKg, ReadingToleranceKg));
                else
                    unmonitored.Add((component.Id.Value, component.Section, tank.InitialKg));
            }
            Ledger = new GroundMassLedger(new GroundMassLedgerSpec(vehicle.Id, dry, tanks, tolerance), facts);
            _unmonitored = [.. unmonitored];
            _jettisons = [.. vehicle.Sections.Where(s => s.Jettison is not null).Select(s => (s.Id, s.Jettison!))];
            _retroRockets = [.. rockets];
        }

        public GroundMassLedger Ledger { get; }

        public void AddRemoved(HashSet<string> removed, Func<string, bool> fired)
        {
            foreach (var (section, missionEvent) in _jettisons)
            {
                if (fired(missionEvent)) removed.Add(section);
            }
            if (_retroRockets.Length > 0 && fired(RetrofireEvent))
            {
                foreach (var rocket in _retroRockets) removed.Add(rocket);
            }
        }

        public (double Low, double Best, double High) Estimate(KnowledgeBase knowledge, KnownMassEvents events, IReadOnlyList<KnownDraw> draws)
        {
            var mass = Ledger.Estimate(knowledge, events).Mass;
            double low = mass.LowKg, best = mass.BestKg, high = mass.HighKg;
            foreach (var (id, section, designKg) in _unmonitored)
            {
                if (events.Removed.Contains(id) || events.Removed.Contains(section)) continue;
                var kg = designKg;
                foreach (var draw in draws)
                {
                    if (string.Equals(draw.TankId, id, StringComparison.Ordinal)) kg -= draw.MassKg;
                }
                kg = Math.Max(0, kg);
                low += kg * (1 - _tolerance);
                best += kg;
                high += kg * (1 + _tolerance);
            }
            return (low, best, high);
        }

        /// <summary>The first number fact (by id) whose source is the tank.</summary>
        private static FactId? TankFact(FactCatalog facts, ComponentId tank)
        {
            foreach (var fact in facts.Facts)
            {
                if (fact.SourceComponent == tank && fact.Kind == FactValueKind.Number) return fact.Id;
            }
            return null;
        }
    }

    /// <summary>
    /// A session-fired engine (<c>session.json</c> <c>burns</c>) and the propellant its specification uses per second, per tank.
    /// </summary>
    private sealed class CommandedBurn(string id, BurnEngineKind kind, (string Tank, double KgPerS)[] tankFlows)
    {
        /// <summary><c>session.burn.&lt;engine&gt;.&lt;field&gt;</c> (STATE_PATHS): the commands the ground sent, not vehicle state.</summary>
        private readonly string _startPath = $"session.burn.{id}.start";
        private readonly string _durationPath = $"session.burn.{id}.duration_s";

        public (string Tank, double KgPerS)[] TankFlows { get; } = tankFlows;

        public bool Active { get; set; }

        /// <summary>The commanded duration while the burn runs (staged duration of a translation, remaining time of a docked burn), else 0.</summary>
        public double CommandedDurationS(IStateQuery state)
        {
            if (kind == BurnEngineKind.Translation)
            {
                var running = state.TryGet(_startPath, out var start) && start.Kind == StateValueKind.Bool && start.AsBool();
                return running ? Number(state, _durationPath) : 0;
            }
            return Number(state, _durationPath);
        }

        public static CommandedBurn[] From(MissionSession session)
        {
            var burns = new List<CommandedBurn>();
            foreach (var engine in session.Data.SessionConfig.BurnEngines)
            {
                if (Vehicle(session, engine.Vehicle) is not { } vehicle) continue;
                var flows = new SortedDictionary<string, double>(StringComparer.Ordinal);
                foreach (var component in vehicle.Components)
                {
                    var spec = engine.Kind == BurnEngineKind.Translation ? TranslationThruster(engine, component) : null;
                    if (engine.Kind == BurnEngineKind.DockedMainEngine && component.Engine is { } main
                        && (engine.Engine is { } named ? named == component.Id : flows.Count == 0))
                        spec = main;
                    if (spec is null || !(spec.IspS > 0)) continue;
                    flows[spec.Tank.Value] = flows.GetValueOrDefault(spec.Tank.Value) + new ConstantThrustEngine(spec.ThrustN, spec.IspS, 0).MassFlowKgPerS;
                }
                if (flows.Count > 0) burns.Add(new CommandedBurn(engine.Id, engine.Kind, [.. flows.Select(f => (f.Key, f.Value))]));
            }
            return [.. burns];
        }

        /// <summary>The engine's listed thrusters, or by default every ungrouped thruster pushing along +X.</summary>
        private static ThrusterSpec? TranslationThruster(BurnEngineDefinition engine, ComponentDefinition component)
        {
            if (component.Thruster is not { } thruster) return null;
            if (engine.Thrusters is { } listed) return listed.Contains(component.Id) ? thruster : null;
            return thruster.Group is null && thruster.DirectionUnit.Count > 0 && thruster.DirectionUnit[0] > 0.99 ? thruster : null;
        }

        private static double Number(IStateQuery state, string path) =>
            state.TryGet(path, out var value) && value.Kind == StateValueKind.Number && double.IsFinite(value.AsNumber()) ? Math.Max(0, value.AsNumber()) : 0;
    }
}
