using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Flight;

/// <summary>
/// Flight of a mission's vehicles as <c>flight.json</c> assigns them: the crewed vehicle (alone, or heading the stack
/// while the target is docked) is the one the <c>flight.</c> paths describe; the optional target co-orbits it.
/// Runs every 2 ticks; while coasting, events are checked once per second.
/// </summary>
public sealed class FlightSystem : ISimSystem, IHashContributor
{
    /// <summary>
    /// Deprecated: mission 1's crewed vehicle id. Use <see cref="MissionFlight.Vehicles"/> (<see cref="CrewedId"/>).
    /// Kept only until Session and the game adapters read roles from data (generalization phase 2); not marked
    /// <see cref="ObsoleteAttribute"/> because warnings are errors and those callers are not in Flight.
    /// </summary>
    [Obsolete("Mission 1's id; read the crewed vehicle from MissionFlight.Vehicles / FlightSystem.CrewedId.")]
    public const string TandemId = "tandem";

    /// <summary>Deprecated: mission 1's target vehicle id. Use <see cref="TargetId"/>; see <see cref="TandemId"/>.</summary>
    [Obsolete("Mission 1's id; read the target vehicle from MissionFlight.Vehicles / FlightSystem.TargetId.")]
    public const string LodestarId = "lodestar";

    private readonly MissionFlight _mission;
    private readonly FlightVehicles _vehicles;
    private readonly IFlightInputs _inputs;
    private readonly EventBus _events;
    private readonly ManeuverPlanner _planner;
    private readonly CombinedThrust _thrust = new();
    private readonly OrbitPropagator _crewed;
    private OrbitPropagator? _target;
    private ProgrammedBurn? _programmed;
    private bool _docked;
    private ChuteState _drogue;
    private ChuteState _main;
    private MissionTime _now;
    private FlightKinematics _kinematics;
    private Conic _conic;
    private double _rangeM;

    /// <summary>Places the vehicles on the mission's start orbit, the target (if any) leading by the configured distance.</summary>
    public FlightSystem(MissionFlight mission, IFlightInputs inputs, EventBus events, MissionTime start)
    {
        _mission = mission ?? throw new ArgumentNullException(nameof(mission));
        _inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _vehicles = mission.Vehicles;
        _planner = new ManeuverPlanner(mission);
        _now = start;

        var conic = mission.StartConic(start);
        var beta = mission.Capsule.BallisticCoefficientKgPerM2;
        var crewedState = conic.StateAt(start);
        _crewed = new OrbitPropagator(CrewedId, mission.Body, start, crewedState, inputs.MassKg(CrewedId), beta, mission.Zones, events);

        if (_vehicles.TargetId is { } targetId)
        {
            var leadRad = _vehicles.TargetLeadM / crewedState.RadiusM;
            var targetConic = conic with
            {
                MeanAnomalyAtEpochRad = Kepler.MeanFromTrue(conic.TrueAnomalyAt(start) + leadRad, conic.Eccentricity),
            };
            _target = new OrbitPropagator(targetId, mission.Body, start, targetConic.StateAt(start), inputs.MassKg(targetId),
                beta, mission.Zones, events);
        }
        RefreshOutputs();
    }

    /// <summary>Base ticks per flight step; thrust is sampled once at the start of each step.</summary>
    public const int StepTicks = 2;

    /// <summary>Length of one flight step. Retro ignition offsets and burn times must be whole multiples of it.</summary>
    public const long StepMs = StepTicks * SimulationClock.BaseTickMs;

    public int PeriodTicks => StepTicks;

    public MissionFlight Mission => _mission;

    public ManeuverPlanner Planner => _planner;

    public MissionTime Now => _now;

    /// <summary>Id of the crewed vehicle, from the mission data.</summary>
    public string CrewedId => _vehicles.CrewedId;

    /// <summary>Id of the target vehicle, from the mission data; null when the mission has none.</summary>
    public string? TargetId => _vehicles.TargetId;

    /// <summary>The crewed vehicle, or the docked stack.</summary>
    public OrbitPropagator Crewed => _crewed;

    /// <summary>The target's own trajectory; null while docked or when the mission has no target.</summary>
    public OrbitPropagator? Target => _target;

    /// <summary>Deprecated alias of <see cref="Target"/>, kept for Session and the game adapters until phase 2.</summary>
    [Obsolete("Use Target.")]
    public OrbitPropagator? Lodestar => _target;

    public bool Docked => _docked;

    public ChuteState Drogue => _drogue;

    public ChuteState Main => _main;

    public ProgrammedBurn? ScheduledBurn => _programmed;

    /// <summary>Crewed vehicle kinematics at <see cref="Now"/>.</summary>
    public FlightKinematics Kinematics => _kinematics;

    /// <summary>Crewed vehicle's (osculating) conic.</summary>
    public Conic Conic => _conic;

    /// <summary>Distance between the crewed vehicle and the target; zero while docked or without a target.</summary>
    public double RangeM => _rangeM;

    public LandingSite? Landing => _crewed.Landing;

    public void Step(in StepContext context)
    {
        _now = context.Now;
        var stepStart = new MissionTime(context.Now.Milliseconds - PeriodTicks * SimulationClock.BaseTickMs);
        var coastCheck = context.TickIndex % SimulationClock.TicksPerSecond == 0;

        if (_crewed.Phase != FlightPhase.Landed)
        {
            var external = _inputs.SampleThrust(context, _kinematics);
            RequireFinite(external);
            if (_programmed is not null) _programmed.PointingErrorRad = RequireFinite(_inputs.PointingErrorRad(context), "Pointing error");
            if (external.IsFiring || (_programmed is not null && _programmed.StartTime <= _now))
            {
                RunPowered(stepStart, external);
            }
            else
            {
                if (_crewed.Phase == FlightPhase.Powered) _crewed.EndBurn();
                if (_crewed.Phase != FlightPhase.Coast || coastCheck) _crewed.Advance(_now);
            }
        }
        if (_target is not null && coastCheck) _target.Advance(_now);
        RefreshOutputs();
    }

    /// <summary>
    /// Joins the target to the crewed vehicle; the stack keeps the crewed vehicle's trajectory (the crew flew the
    /// approach). Refused unless the data names the two as the docking pair.
    /// </summary>
    public bool Dock(MissionTime now)
    {
        if (_docked || !_vehicles.TargetDocks || _target is null || _crewed.Phase != FlightPhase.Coast || _target.Phase != FlightPhase.Coast)
            return false;
        var targetId = _target.VehicleId;
        _crewed.Advance(now);
        _docked = true;
        _target = null;
        _crewed.SetMass(StackMassKg());
        _events.Publish(new DockingChanged(now, true, CrewedId, targetId));
        RefreshOutputs();
        return true;
    }

    public bool Undock(MissionTime now) => Undock(now, new Vec2d(_mission.Definition.UndockSeparationSpeedMps, 0));

    /// <summary>Separates the stack. Momentum is shared by mass.</summary>
    /// <param name="separationLocalMps">The target's velocity relative to the crewed vehicle as (prograde, radial-out).</param>
    public bool Undock(MissionTime now, Vec2d separationLocalMps)
    {
        if (!double.IsFinite(separationLocalMps.X) || !double.IsFinite(separationLocalMps.Y))
            throw new ArgumentOutOfRangeException(nameof(separationLocalMps), separationLocalMps, "Separation velocity must be finite.");
        if (!_docked || _crewed.Phase is FlightPhase.Powered or FlightPhase.Landed) return false;
        var targetId = _vehicles.TargetId!;
        _crewed.Advance(now);
        var crewedMass = _inputs.MassKg(CrewedId);
        var targetMass = _inputs.MassKg(targetId);
        var state = _crewed.State;
        var relative = BurnFrames.LocalToInertial(separationLocalMps, state);
        var total = crewedMass + targetMass;
        _target = new OrbitPropagator(targetId, _crewed.Body, _crewed.Time, state, targetMass,
            _mission.Capsule.BallisticCoefficientKgPerM2, _mission.Zones, _events);
        _target.ApplyImpulse(relative * (crewedMass / total));
        _crewed.ApplyImpulse(relative * (-targetMass / total));
        _crewed.SetMass(crewedMass);
        _docked = false;
        _events.Publish(new DockingChanged(now, false, CrewedId, targetId));
        RefreshOutputs();
        return true;
    }

    /// <summary>Queues a burn the flight model flies itself (attitude held on the plan's direction).</summary>
    /// <returns>False while another programmed burn is pending or running, or after landing.</returns>
    public bool ScheduleBurn(ManeuverPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        plan.RequireFinite();
        if (_programmed is not null || _crewed.Phase == FlightPhase.Landed) return false;
        _programmed = new ProgrammedBurn(plan);
        return true;
    }

    /// <summary>Fires the retro sequence now, held retrograde.</summary>
    public bool Retrofire(MissionTime now) =>
        ScheduleBurn(new ManeuverPlan(new RetroSequenceEngine(_mission.RetroSequence), BurnDirection.Retrograde, 0, now));

    public ChuteDeployResult DeployChute(ChuteKind kind, MissionTime now)
    {
        var current = kind == ChuteKind.Drogue ? _drogue : _main;
        if (current == ChuteState.Deployed) return ChuteDeployResult.AlreadyDeployed;
        if (current == ChuteState.Failed) return ChuteDeployResult.AlreadyFailed;
        if (_crewed.Phase == FlightPhase.Landed) return ChuteDeployResult.Landed;

        var kinematics = _crewed.Sample(now);
        var result = _mission.Capsule.Chute(kind).Evaluate(kinematics.AltitudeM, kinematics.AirspeedMps);
        var next = result == ChuteDeployResult.Deployed ? ChuteState.Deployed : ChuteState.Failed;
        if (kind == ChuteKind.Drogue) _drogue = next;
        else _main = next;

        var capsule = _mission.Capsule;
        _crewed.SetBallisticCoefficient(
            _main == ChuteState.Deployed ? capsule.Main.BallisticCoefficientKgPerM2
            : _drogue == ChuteState.Deployed ? capsule.Drogue.BallisticCoefficientKgPerM2
            : capsule.BallisticCoefficientKgPerM2);
        _events.Publish(new ChuteDeployed(now, CrewedId, kind, result, kinematics.AltitudeM, kinematics.AirspeedMps));
        return result;
    }

    /// <summary>A detached copy of the crewed vehicle with its current mass, for previews and proposals.</summary>
    public OrbitPropagator CreatePreviewState()
    {
        var clone = _crewed.Clone();
        clone.SetMass(StackMassKg());
        return clone;
    }

    public ManeuverPreview Preview(ManeuverPlan plan) => _planner.Preview(CreatePreviewState(), plan, _drogue, _main);

    public void WriteHash(StateHashWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(_docked);
        writer.Write((long)_drogue);
        writer.Write((long)_main);
        writer.Write(_programmed is not null);
        if (_programmed is not null)
        {
            writer.Write(_programmed.StartTime.Milliseconds);
            writer.Write(_programmed.EndTime.Milliseconds);
            writer.Write(_programmed.PropellantUsedKg);
            writer.Write(_programmed.PointingErrorRad);
        }
        _crewed.WriteHash(writer);
        writer.Write(_target is not null);
        _target?.WriteHash(writer);
    }

    private void RunPowered(MissionTime stepStart, PoweredInput external)
    {
        _thrust.External = external.ToSample();
        _thrust.Programmed = _programmed;
        if (_crewed.Phase != FlightPhase.Powered)
        {
            var begin = external.IsFiring || _programmed is null ? stepStart : _programmed.StartTime;
            if (begin < _crewed.Time) begin = _crewed.Time;
            if (!_crewed.BeginBurn(begin, StackMassKg()))
            {
                _programmed = null;
                return;
            }
        }

        if (_programmed is not null && !external.IsFiring && _programmed.EndTime <= _now)
        {
            _crewed.AdvancePowered(_programmed.EndTime, _thrust, allowPartialStep: true);
            _programmed = null;
            if (_crewed.Phase == FlightPhase.Powered) _crewed.EndBurn();
            if (_crewed.Phase != FlightPhase.Landed) _crewed.Advance(_now);
            return;
        }

        _crewed.AdvancePowered(_now, _thrust, allowPartialStep: false);
        if (_programmed is not null && (_crewed.Time >= _programmed.EndTime || _crewed.Phase == FlightPhase.Landed)) _programmed = null;
    }

    private double StackMassKg() =>
        _docked ? _inputs.MassKg(CrewedId) + _inputs.MassKg(_vehicles.TargetId!) : _inputs.MassKg(CrewedId);

    private static double RequireFinite(double value, string what) =>
        double.IsFinite(value) ? value : throw new InvalidOperationException($"{what} from the flight inputs is {value}; it must be finite.");

    private static void RequireFinite(in PoweredInput input)
    {
        RequireFinite(input.ThrustN, "Thrust");
        RequireFinite(input.IspS, "Isp");
        RequireFinite(input.PointingAngleRad, "Thrust pointing angle");
        RequireFinite(input.AttitudeErrorRad, "Thrust attitude error");
    }

    private void RefreshOutputs()
    {
        _kinematics = _crewed.Sample(_now);
        _conic = _crewed.Conic;
        _rangeM = _target is null ? 0 : (_target.Sample(_now).State.PositionM - _kinematics.State.PositionM).Length;
    }

    /// <summary>Vector sum of the caller's thrust (constant for the step) and the programmed burn.</summary>
    private sealed class CombinedThrust : IThrustLaw
    {
        public ThrustSample External { get; set; }

        public ProgrammedBurn? Programmed { get; set; }

        public ThrustSample Sample(MissionTime time, double stepSeconds, in StateVector state, double massKg) =>
            Programmed is null
                ? External
                : ThrustSample.Combine(External, Programmed.Sample(time, stepSeconds, state, massKg));
    }
}
