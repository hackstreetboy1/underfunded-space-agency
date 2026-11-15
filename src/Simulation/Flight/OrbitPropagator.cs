using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Flight;

/// <summary>
/// One vehicle's trajectory as a sequence of segments: analytic coast, powered RK4 and atmospheric RK4.
/// Coast checks events every second (sphere-of-influence exit/entry, atmosphere interface, surface impact)
/// and bisects crossings to 1 ms. Integrated segments step 0.1 s on a grid anchored at the segment start, so
/// a preview from a clone reproduces the real flight exactly.
/// </summary>
public sealed class OrbitPropagator : IHashContributor
{
    public const long CoastCheckMs = 1000;
    public const long IntegrationStepMs = 100;

    private const int AltitudeGuard = 0;
    private const int SoiExitGuard = 1;
    private const int FirstChildGuard = 2;

    private readonly RecoveryZones? _zones;
    private readonly EventBus? _events;

    private CelestialBody _body;
    private FlightPhase _phase;
    private MissionTime _time;
    private StateVector _state;
    private Conic _conic;
    private double _massKg;
    private double _ballisticCoefficient;
    private double _gLoad;
    private double _heatingProxy;
    private double _burnPropellantKg;
    private double _burnProgrammedPropellantKg;
    private double _burnDeltaVMps;
    private LandingSite? _landing;
    private InterfaceCrossing? _lastCrossing;
    private MissionTime? _lastDecayPass;

    public OrbitPropagator(
        string vehicleId, CelestialBody body, MissionTime time, StateVector state, double massKg,
        double ballisticCoefficientKgPerM2, RecoveryZones? zones = null, EventBus? events = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(vehicleId);
        ArgumentNullException.ThrowIfNull(body);
        RequirePositive(massKg, nameof(massKg));
        RequirePositive(ballisticCoefficientKgPerM2, nameof(ballisticCoefficientKgPerM2));
        VehicleId = vehicleId;
        _body = body;
        _time = time;
        _state = state;
        _massKg = massKg;
        _ballisticCoefficient = ballisticCoefficientKgPerM2;
        _zones = zones;
        _events = events;
        if (IsInsideAtmosphere())
        {
            _phase = FlightPhase.Atmospheric;
            UpdateOutputs(Vec2d.Zero);
        }
        else
        {
            _phase = FlightPhase.Coast;
            _conic = Conic.FromState(_state, _body.MuM3PerS2, _time);
        }
    }

    private OrbitPropagator(OrbitPropagator source)
    {
        VehicleId = source.VehicleId;
        _zones = source._zones;
        _events = null;
        _body = source._body;
        _phase = source._phase;
        _time = source._time;
        _state = source._state;
        _conic = source._conic;
        _massKg = source._massKg;
        _ballisticCoefficient = source._ballisticCoefficient;
        _gLoad = source._gLoad;
        _heatingProxy = source._heatingProxy;
        _burnPropellantKg = source._burnPropellantKg;
        _burnProgrammedPropellantKg = source._burnProgrammedPropellantKg;
        _burnDeltaVMps = source._burnDeltaVMps;
        _landing = source._landing;
        _lastCrossing = source._lastCrossing;
        _lastDecayPass = source._lastDecayPass;
    }

    private enum SegmentResult
    {
        ReachedTarget,
        Transition,
        Lagging,
    }

    public string VehicleId { get; }

    public CelestialBody Body => _body;

    public FlightPhase Phase => _phase;

    /// <summary>Time of <see cref="State"/>. Integrated segments may trail the clock by less than one step.</summary>
    public MissionTime Time => _time;

    public StateVector State => _state;

    public double MassKg => _massKg;

    public double BallisticCoefficientKgPerM2 => _ballisticCoefficient;

    public double AltitudeM => _state.RadiusM - _body.RadiusM;

    public double GLoad => _gLoad;

    public double HeatingProxy => _heatingProxy;

    /// <summary>The coast conic, or the osculating conic of the current state in other phases.</summary>
    public Conic Conic => _phase == FlightPhase.Coast ? _conic : Conic.FromState(_state, _body.MuM3PerS2, _time);

    public double BurnPropellantUsedKg => _burnPropellantKg;

    /// <summary>Part of <see cref="BurnPropellantUsedKg"/> drawn by programmed burns (see <see cref="ThrustSample.ProgrammedMassFlowKgPerS"/>).</summary>
    public double BurnProgrammedPropellantUsedKg => _burnProgrammedPropellantKg;

    public double BurnDeltaVMps => _burnDeltaVMps;

    public LandingSite? Landing => _landing;

    public InterfaceCrossing? LastInterfaceCrossing => _lastCrossing;

    /// <summary>A copy that publishes no events, for previews.</summary>
    public OrbitPropagator Clone() => new(this);

    /// <summary>A <see cref="Clone"/> moved to <paramref name="state"/> at the same time (an estimate, or a what-if for a preview).</summary>
    internal OrbitPropagator Displaced(StateVector state)
    {
        var copy = new OrbitPropagator(this) { _state = state, _lastDecayPass = null };
        if (copy._phase == FlightPhase.Coast) copy._conic = Conic.FromState(state, _body.MuM3PerS2, _time);
        return copy;
    }

    /// <summary>Kinematics at <paramref name="time"/>: exact on a coast, the latest integrated state otherwise.</summary>
    public FlightKinematics Sample(MissionTime time)
    {
        var state = _phase == FlightPhase.Coast && time > _time ? _conic.StateAt(time) : _state;
        var landed = _phase == FlightPhase.Landed;
        var airspeed = landed ? 0 : _body.AirVelocityOf(state).Length;
        return new FlightKinematics(
            _phase == FlightPhase.Coast && time > _time ? time : _time,
            _phase, _body.Id, state, landed ? 0 : state.RadiusM - _body.RadiusM, airspeed, _gLoad, _heatingProxy);
    }

    public void SetMass(double massKg)
    {
        RequirePositive(massKg, nameof(massKg));
        _massKg = massKg;
    }

    public void SetBallisticCoefficient(double ballisticCoefficientKgPerM2)
    {
        RequirePositive(ballisticCoefficientKgPerM2, nameof(ballisticCoefficientKgPerM2));
        _ballisticCoefficient = ballisticCoefficientKgPerM2;
    }

    /// <summary>Changes the velocity instantly (separation springs, docking impulses).</summary>
    public void ApplyImpulse(Vec2d deltaVMps)
    {
        if (!double.IsFinite(deltaVMps.X) || !double.IsFinite(deltaVMps.Y))
            throw new ArgumentOutOfRangeException(nameof(deltaVMps), deltaVMps, $"Impulse on {VehicleId} must be finite.");
        if (_phase is FlightPhase.Landed) return;
        _state = _state with { VelocityMps = _state.VelocityMps + deltaVMps };
        if (_phase == FlightPhase.Coast) _conic = Conic.FromState(_state, _body.MuM3PerS2, _time);
        _lastDecayPass = null;
    }

    /// <summary>Advances a coasting, atmospheric or landed vehicle towards <paramref name="target"/>.</summary>
    public void Advance(MissionTime target)
    {
        RequireUnpowered();
        AdvanceCore(target, stopAtTransition: false, allowPartialStep: false);
    }

    /// <summary>Like <see cref="Advance"/> but stops at the first phase or body change.</summary>
    /// <returns>True when it stopped at a transition before <paramref name="target"/>.</returns>
    public bool AdvanceToNextTransition(MissionTime target)
    {
        RequireUnpowered();
        return AdvanceCore(target, stopAtTransition: true, allowPartialStep: false);
    }

    /// <summary>Brings the vehicle to <paramref name="start"/> and switches to powered flight.</summary>
    /// <returns>False if the vehicle landed before <paramref name="start"/>.</returns>
    public bool BeginBurn(MissionTime start, double massKg)
    {
        RequireUnpowered();
        AdvanceCore(start, stopAtTransition: false, allowPartialStep: true);
        if (_phase == FlightPhase.Landed) return false;
        SetMass(massKg);
        _phase = FlightPhase.Powered;
        _burnPropellantKg = 0;
        _burnProgrammedPropellantKg = 0;
        _burnDeltaVMps = 0;
        _events?.Publish(new BurnStarted(_time, VehicleId));
        return true;
    }

    /// <summary>Integrates powered flight to <paramref name="target"/>, sampling <paramref name="thrust"/> every step.</summary>
    /// <param name="allowPartialStep">Integrate a final step shorter than 0.1 s to land exactly on the target (burn end).</param>
    public void AdvancePowered(MissionTime target, IThrustLaw thrust, bool allowPartialStep)
    {
        ArgumentNullException.ThrowIfNull(thrust);
        if (_phase != FlightPhase.Powered) throw new InvalidOperationException($"{VehicleId} is not in powered flight.");
        while (_time < target)
        {
            var stepMs = Math.Min(IntegrationStepMs, target.Milliseconds - _time.Milliseconds);
            if (stepMs < IntegrationStepMs && !allowPartialStep) return;
            var dt = stepMs / 1000.0;
            var sample = thrust.Sample(_time, dt, _state, _massKg);
            var previous = _state;
            var previousMass = _massKg;
            FlightDynamics.Rk4Step(_body, _ballisticCoefficient, sample, dt, ref _state, ref _massKg);
            if (_state.RadiusM <= _body.RadiusM)
            {
                var usedMs = LandWithin(previous, previousMass, sample, stepMs);
                AccumulateBurn(sample, usedMs / 1000.0, previousMass);
                return;
            }
            AccumulateBurn(sample, dt, previousMass);
            _time += stepMs;
            UpdateOutputs(sample.ForceN);
        }
    }

    /// <summary>Ends powered flight; the vehicle coasts, or flies on through the atmosphere.</summary>
    public void EndBurn()
    {
        if (_phase != FlightPhase.Powered) throw new InvalidOperationException($"{VehicleId} is not in powered flight.");
        if (IsInsideAtmosphere())
        {
            _phase = FlightPhase.Atmospheric;
        }
        else
        {
            _phase = FlightPhase.Coast;
            _conic = Conic.FromState(_state, _body.MuM3PerS2, _time);
        }
        UpdateOutputs(Vec2d.Zero);
        _lastDecayPass = null;
        _events?.Publish(new BurnEnded(_time, VehicleId, _burnDeltaVMps, _burnPropellantKg, _burnProgrammedPropellantKg));
    }

    public void WriteHash(StateHashWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(VehicleId);
        writer.Write(_body.Id);
        writer.Write((long)_phase);
        writer.Write(_time.Milliseconds);
        writer.Write(_state.PositionM.X);
        writer.Write(_state.PositionM.Y);
        writer.Write(_state.VelocityMps.X);
        writer.Write(_state.VelocityMps.Y);
        writer.Write(_conic.SemiMajorAxisM);
        writer.Write(_conic.Eccentricity);
        writer.Write(_conic.ArgPeriapsisRad);
        writer.Write(_conic.MeanAnomalyAtEpochRad);
        writer.Write(_conic.Epoch.Milliseconds);
        writer.Write(_massKg);
        writer.Write(_ballisticCoefficient);
        writer.Write(_gLoad);
        writer.Write(_burnPropellantKg);
        writer.Write(_burnProgrammedPropellantKg);
        writer.Write(_burnDeltaVMps);
        writer.Write(_lastDecayPass.HasValue);
        writer.Write(_lastDecayPass?.Milliseconds ?? 0);
        writer.Write(_landing.HasValue);
        if (_landing is { } landing)
        {
            writer.Write(landing.Time.Milliseconds);
            writer.Write(landing.ImpactAngleRad);
            writer.Write(landing.ImpactSpeedMps);
        }
    }

    private bool AdvanceCore(MissionTime target, bool stopAtTransition, bool allowPartialStep)
    {
        while (_time < target)
        {
            SegmentResult result;
            switch (_phase)
            {
                case FlightPhase.Coast:
                    result = CoastUntil(target);
                    break;
                case FlightPhase.Atmospheric:
                    result = AtmosphereUntil(target, allowPartialStep);
                    break;
                case FlightPhase.Landed:
                    _time = target;
                    return false;
                default:
                    throw new InvalidOperationException($"{VehicleId} cannot coast while {_phase}.");
            }
            if (result == SegmentResult.Lagging) return false;
            if (result == SegmentResult.Transition && stopAtTransition) return true;
        }
        return false;
    }

    private SegmentResult CoastUntil(MissionTime target)
    {
        var decay = NextDecay();
        var quiet = ConicClearOfGuards();
        while (_time < target)
        {
            // No guard can fire anywhere on this conic, so the 1 s checks would only overwrite the state with the same
            // analytic one: jump to the next decay pass (or the target) and get exactly what stepping would give.
            var nextMs = quiet ? target.Milliseconds : Math.Min(_time.Milliseconds + CoastCheckMs, target.Milliseconds);
            if (decay is { } pass && pass.Time.Milliseconds < nextMs) nextMs = pass.Time.Milliseconds;
            if (quiet)
            {
                _time = new MissionTime(nextMs);
                _state = _conic.StateAt(_time);
                if (decay is { } passed && passed.Time == _time)
                {
                    _state = OrbitDecay.Apply(_body, _state, passed.DeltaVMps);
                    _conic = Conic.FromState(_state, _body.MuM3PerS2, _time);
                    _lastDecayPass = _time;
                    decay = NextDecay();
                    quiet = ConicClearOfGuards();
                }
                continue;
            }
            var next = new MissionTime(nextMs);
            var nextState = _conic.StateAt(next);
            var hitGuard = -1;
            var hitTime = next;
            var guardCount = FirstChildGuard + _body.ChildArray.Length;
            for (var guard = 0; guard < guardCount; guard++)
            {
                if (Guard(guard, next, nextState) > 0 || Guard(guard, _time, _state) <= 0) continue;
                var crossing = BisectCoastGuard(guard, _time, next);
                if (hitGuard < 0 || crossing < hitTime)
                {
                    hitGuard = guard;
                    hitTime = crossing;
                }
            }

            if (hitGuard >= 0)
            {
                _time = hitTime;
                _state = _conic.StateAt(hitTime);
                HandleCoastEvent(hitGuard);
                return SegmentResult.Transition;
            }
            _time = next;
            _state = nextState;
            if (decay is { } due && due.Time == _time)
            {
                _state = OrbitDecay.Apply(_body, _state, due.DeltaVMps);
                _conic = Conic.FromState(_state, _body.MuM3PerS2, _time);
                _lastDecayPass = _time;
                decay = NextDecay();
                quiet = ConicClearOfGuards();
            }
        }
        return SegmentResult.ReachedTarget;
    }

    /// <summary>
    /// The whole closed conic stays above the altitude guard, inside the sphere of influence and clear of every child's
    /// sphere (by the triangle inequality), with a metre of margin for rounding between apsides and sampled radii.
    /// </summary>
    private bool ConicClearOfGuards()
    {
        const double MarginM = 1;
        if (_conic.IsHyperbolic || _conic.PeriapsisM - MarginM <= _body.RadiusM + _body.InterfaceAltitudeM) return false;
        var apoapsisM = _conic.ApoapsisM + MarginM;
        if (_body.Parent is not null && apoapsisM >= _body.SoiRadiusM) return false;
        var children = _body.ChildArray;
        for (var i = 0; i < children.Length; i++)
        {
            if (apoapsisM >= children[i].OrbitRadiusM - children[i].SoiRadiusM) return false;
        }
        return true;
    }

    /// <summary>The next apse (strictly after now) at which coasting drag is modelled, and its speed loss.</summary>
    private (MissionTime Time, double DeltaVMps)? NextDecay() => OrbitDecay.Next(_body, _conic, _ballisticCoefficient, _time + 1, _lastDecayPass);

    /// <summary>Positive while nothing has happened; the event fires when the value reaches zero or below.</summary>
    private double Guard(int guard, MissionTime time, in StateVector state)
    {
        switch (guard)
        {
            case AltitudeGuard:
                return state.RadiusM - (_body.RadiusM + _body.InterfaceAltitudeM);
            case SoiExitGuard:
                return _body.Parent is null ? double.PositiveInfinity : _body.SoiRadiusM - state.RadiusM;
            default:
                var child = _body.ChildArray[guard - FirstChildGuard];
                return (state.PositionM - child.PositionInParentAt(time)).Length - child.SoiRadiusM;
        }
    }

    private MissionTime BisectCoastGuard(int guard, MissionTime outside, MissionTime inside)
    {
        var lo = outside.Milliseconds;
        var hi = inside.Milliseconds;
        while (hi - lo > 1)
        {
            var mid = lo + (hi - lo) / 2;
            var time = new MissionTime(mid);
            if (Guard(guard, time, _conic.StateAt(time)) <= 0) hi = mid;
            else lo = mid;
        }
        return new MissionTime(hi);
    }

    private void HandleCoastEvent(int guard)
    {
        switch (guard)
        {
            case AltitudeGuard when _body.Atmosphere is not null:
                _phase = FlightPhase.Atmospheric;
                UpdateOutputs(Vec2d.Zero);
                RecordCrossing(entering: true);
                break;
            case AltitudeGuard:
                Land();
                break;
            case SoiExitGuard:
                ChangeBody(_body.Parent!, _state.PositionM + _body.PositionInParentAt(_time), _state.VelocityMps + _body.VelocityInParentAt(_time));
                break;
            default:
                var child = _body.ChildArray[guard - FirstChildGuard];
                ChangeBody(child, _state.PositionM - child.PositionInParentAt(_time), _state.VelocityMps - child.VelocityInParentAt(_time));
                break;
        }
    }

    private void ChangeBody(CelestialBody body, Vec2d positionM, Vec2d velocityMps)
    {
        var from = _body;
        _body = body;
        _state = new StateVector(positionM, velocityMps);
        _conic = Conic.FromState(_state, body.MuM3PerS2, _time);
        _lastDecayPass = null;
        _events?.Publish(new SoiChanged(_time, VehicleId, from.Id, body.Id));
    }

    private SegmentResult AtmosphereUntil(MissionTime target, bool allowPartialStep)
    {
        while (_time < target)
        {
            var stepMs = Math.Min(IntegrationStepMs, target.Milliseconds - _time.Milliseconds);
            if (stepMs < IntegrationStepMs && !allowPartialStep) return SegmentResult.Lagging;
            var previous = _state;
            var previousMass = _massKg;
            FlightDynamics.Rk4Step(_body, _ballisticCoefficient, ThrustSample.None, stepMs / 1000.0, ref _state, ref _massKg);
            if (_state.RadiusM <= _body.RadiusM)
            {
                LandWithin(previous, previousMass, ThrustSample.None, stepMs);
                return SegmentResult.Transition;
            }
            _time += stepMs;
            UpdateOutputs(Vec2d.Zero);
            if (!IsInsideAtmosphere() && _state.PositionM.Dot(_state.VelocityMps) > 0)
            {
                _phase = FlightPhase.Coast;
                _conic = Conic.FromState(_state, _body.MuM3PerS2, _time);
                _gLoad = 0;
                _lastDecayPass = null;
                _heatingProxy = 0;
                RecordCrossing(entering: false);
                return SegmentResult.Transition;
            }
        }
        return SegmentResult.ReachedTarget;
    }

    /// <summary>Finds the first millisecond of the step at which the vehicle reaches the surface, then lands there.</summary>
    /// <returns>Milliseconds of the step flown before impact.</returns>
    private long LandWithin(StateVector previous, double previousMass, in ThrustSample thrust, long stepMs)
    {
        long lo = 0;
        var hi = stepMs;
        var state = previous;
        var mass = previousMass;
        while (hi - lo > 1)
        {
            var mid = lo + (hi - lo) / 2;
            state = previous;
            mass = previousMass;
            FlightDynamics.Rk4Step(_body, _ballisticCoefficient, thrust, mid / 1000.0, ref state, ref mass);
            if (state.RadiusM <= _body.RadiusM) hi = mid;
            else lo = mid;
        }
        state = previous;
        mass = previousMass;
        FlightDynamics.Rk4Step(_body, _ballisticCoefficient, thrust, hi / 1000.0, ref state, ref mass);
        _state = state;
        _massKg = mass;
        _time += hi;
        Land();
        return hi;
    }

    private void Land()
    {
        _phase = FlightPhase.Landed;
        var impactAngle = _state.PositionM.Angle;
        var impactSpeed = _body.AirVelocityOf(_state).Length;
        var zone = _zones is not null && _zones.BodyId == _body.Id
            ? _zones.EvaluateImpact(impactAngle, _time)
            : new ZoneEvaluation(Angles.Wrap(impactAngle - _body.RotationRateRadPerS * _time.Seconds) * Angles.RadToDeg, null, 0);
        _landing = new LandingSite(_time, _body.Id, impactAngle, impactSpeed, zone);
        _gLoad = 1;
        _heatingProxy = 0;
        _events?.Publish(new Splashdown(_time, VehicleId, _body.Id, zone.LandingLongitudeDeg, zone.ZoneId, zone.RecoveryWaitHours, impactSpeed));
    }

    private void RecordCrossing(bool entering)
    {
        var crossing = new InterfaceCrossing(_time, _state.FlightPathAngleRad, _state.SpeedMps, entering);
        _lastCrossing = crossing;
        _events?.Publish(new AtmosphereInterfaceCrossed(
            _time, VehicleId, entering, crossing.FlightPathAngleRad * Angles.RadToDeg, crossing.SpeedMps));
    }

    private void AccumulateBurn(in ThrustSample sample, double seconds, double massBeforeKg)
    {
        if (sample.ThrustN <= 0 || seconds <= 0) return;
        var propellant = sample.MassFlowKgPerS * seconds;
        _burnPropellantKg += propellant;
        _burnProgrammedPropellantKg += sample.ProgrammedMassFlowKgPerS * seconds;
        _burnDeltaVMps += sample.MassFlowKgPerS > 0
            ? sample.ThrustN / sample.MassFlowKgPerS * Math.Log(massBeforeKg / (massBeforeKg - propellant))
            : sample.ThrustN / massBeforeKg * seconds;
    }

    private void UpdateOutputs(Vec2d forceN)
    {
        var acceleration = FlightDynamics.NonGravitational(_body, _ballisticCoefficient, forceN, _state.PositionM, _state.VelocityMps, _massKg);
        _gLoad = acceleration.Length / PhysicalConstants.StandardGravity;
        _heatingProxy = FlightDynamics.HeatingProxy(_body, _state);
    }

    private bool IsInsideAtmosphere() =>
        _body.Atmosphere is not null && _state.RadiusM < _body.RadiusM + _body.InterfaceAltitudeM;

    private static void RequirePositive(double value, string name)
    {
        if (!(double.IsFinite(value) && value > 0))
            throw new ArgumentOutOfRangeException(name, value, "Must be positive and finite.");
    }

    private void RequireUnpowered()
    {
        if (_phase == FlightPhase.Powered)
            throw new InvalidOperationException($"{VehicleId} is in powered flight; use AdvancePowered and EndBurn.");
    }
}
