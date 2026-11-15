using Usa.Simulation.Attitude;
using Usa.Simulation.Data;
using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;
using Usa.Simulation.Knowledge.Ledger;

namespace Usa.Simulation.Session;

/// <summary>
/// Engine burns flight does not schedule itself (SESSION_EFFECTS_NEEDED.md), and attitude during every burn. The engines are
/// the mission's <c>session.json</c> <c>burns</c>; each is served under <c>session.burn.&lt;id&gt;.</c> (<see cref="SessionPaths"/>).
/// <list type="bullet">
/// <item><b>Translation burn</b> (<see cref="BurnEngineKind.Translation"/>): the engine's thrusters fire at full valve for the
/// staged duration, through <see cref="Vehicle.Propulsion.PropulsionModel.Fire"/> (propellant, valve activity), and their delivered
/// thrust goes to flight as <see cref="IFlightInputs.SampleThrust"/> along the staged inertial direction (the
/// <c>attitude.&lt;v&gt;.pointing_deg</c> convention). It stops early when no thruster delivers thrust (master off, tank dry) or
/// while docked.</item>
/// <item><b>Docked main-engine burn</b> (<see cref="BurnEngineKind.DockedMainEngine"/>): the engine fires prograde while docked,
/// after the stack turns it prograde; the prograde Δv it adds is served as <c>docked_dv_mps</c> (mission events compare it).</item>
/// <item><b>Attitude</b>: during a burn the thrusting vehicle holds attitude on the burn direction (attitude hold on its current
/// control set; the docked stack is flown by the crewed vehicle when its control is active, else by the engine's vehicle) and
/// returns to its previous mode afterwards. Flight's programmed burns (the retrofire) are held the same way by the crewed
/// vehicle; flight receives the signed in-plane error of the thrust axis.</item>
/// <item>Fired retro rockets burn down their propellant mass every flight step.</item>
/// </list>
/// Runs right after flight (100 ms); <see cref="Sample"/> is called by flight at the start of its step.
/// </summary>
internal sealed class BurnController : ISimSystem, IHashContributor
{
    /// <summary>A docked engine's axis must be this close to prograde before its burn ignites.</summary>
    private const double AlignToleranceDeg = 2;

    /// <summary>A docked burn ignites anyway after this long aligning.</summary>
    private const double MaxAlignS = 180;

    /// <summary>A commanded retrofire starts anyway after this long turning to retro attitude.</summary>
    private const double MaxRetroAlignS = 60;

    private readonly VehicleSet _vehicles;
    private readonly AttitudeSystem _attitude;
    private readonly string _crewedId;
    private readonly int _crewed;
    private readonly TranslationBurn[] _translations;
    private readonly DockedBurn[] _docked;
    private readonly Dictionary<string, TranslationBurn> _translationById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DockedBurn> _dockedById = new(StringComparer.Ordinal);
    private readonly List<KnownDraw> _knownDraws = [];
    private FlightSystem? _flight;
    private long _retroAlignDeadlineMs = -1;
    private string? _holdVehicle;
    private AttitudeMode _modeBefore;

    /// <exception cref="DefinitionException">An engine names a thruster or engine its vehicle does not have.</exception>
    public BurnController(VehicleSet vehicles, AttitudeSystem attitude, string crewedId, IReadOnlyList<BurnEngineDefinition> engines, string file)
    {
        _vehicles = vehicles;
        _attitude = attitude;
        _crewedId = crewedId;
        _crewed = vehicles.IndexOf(crewedId);
        var errors = new DefinitionErrors();
        var translations = new List<TranslationBurn>();
        var docked = new List<DockedBurn>();
        for (var i = 0; i < engines.Count; i++)
        {
            var engine = engines[i];
            var path = $"$.burns[{i}]";
            if (!vehicles.Contains(engine.Vehicle))
            {
                errors.Add(file, path + ".vehicle", $"Unknown vehicle '{engine.Vehicle}'.");
                continue;
            }
            var vehicle = vehicles.IndexOf(engine.Vehicle);
            switch (engine.Kind)
            {
                case BurnEngineKind.Translation:
                    var burn = new TranslationBurn(engine.Vehicle, vehicle, TranslationThrusters(engine, vehicle, file, path, errors));
                    translations.Add(burn);
                    _translationById[engine.Id] = burn;
                    break;
                case BurnEngineKind.DockedMainEngine:
                    var main = new DockedBurn(engine.Vehicle, vehicle, MainEngine(engine, vehicle, file, path, errors));
                    docked.Add(main);
                    _dockedById[engine.Id] = main;
                    break;
            }
        }
        errors.ThrowIfAny();
        _translations = [.. translations];
        _docked = [.. docked];
    }

    public int PeriodTicks => 2;

    /// <summary>Translation engine ids, in data order.</summary>
    public IEnumerable<string> TranslationIds => _translationById.Keys;

    /// <summary>Docked main-engine ids, in data order.</summary>
    public IEnumerable<string> DockedIds => _dockedById.Keys;

    /// <summary>A commanded retrofire is waiting for the capsule to reach retro attitude.</summary>
    public bool RetrofirePending => _retroAlignDeadlineMs >= 0;

    public bool AnyBurn
    {
        get
        {
            foreach (var burn in _translations)
            {
                if (burn.EndMs >= 0) return true;
            }
            foreach (var burn in _docked)
            {
                if (burn.Active) return true;
            }
            return RetrofirePending || _flight?.ScheduledBurn is not null;
        }
    }

    public void Attach(FlightSystem flight) => _flight = flight;

    /// <summary>
    /// Propellant each commanded burn draws by its engine spec (thrust ÷ (Isp·g0) × commanded seconds per tank), recorded at
    /// ignition: what the ground knows was used, as opposed to the measured tank contents (X8 ground mass ledger).
    /// </summary>
    public IReadOnlyList<KnownDraw> KnownDraws => _knownDraws;

    /// <summary>
    /// Told at ignition of every burn the ground commands through the session (a translation burn on its vehicle, a docked
    /// burn on the crewed vehicle's stack), e.g. <see cref="GroundTracking.RecordCommandedBurn"/>.
    /// </summary>
    public Action<string, ManeuverPlan>? CommandedBurn { get; set; }

    public bool IsTranslation(string engineId) => _translationById.ContainsKey(engineId);

    public bool IsDocked(string engineId) => _dockedById.ContainsKey(engineId);

    /// <summary>Staged in-plane inertial direction of the engine's next translation burn.</summary>
    public double DirectionDeg(string engineId) => _translationById[engineId].DirectionDeg;

    public void SetDirectionDeg(string engineId, double degrees) => _translationById[engineId].DirectionDeg = degrees;

    /// <summary>Staged duration of the engine's next translation burn.</summary>
    public double DurationS(string engineId) => _translationById[engineId].DurationS;

    public void SetDurationS(string engineId, double seconds) => _translationById[engineId].DurationS = seconds;

    public bool TranslationBurning(string engineId) => _translationById[engineId].EndMs >= 0;

    /// <summary>Prograde Δv the docked engine has added while docked.</summary>
    public double DockedDeltaVMps(string engineId) => _dockedById[engineId].DeltaVMps;

    /// <summary>Seconds left of the docked engine's burn (the commanded duration while it is still aligning).</summary>
    public double DockedRemainingS(string engineId, MissionTime now)
    {
        var burn = _dockedById[engineId];
        return burn.PendingS > 0 ? burn.PendingS : burn.EndMs < 0 ? 0 : Math.Max(0, (burn.EndMs - now.Milliseconds) / 1000.0);
    }

    /// <summary>Fires the staged translation burn.</summary>
    /// <returns>False (no-op) while docked, with nothing staged or when the engine has no thrusters.</returns>
    public bool StartTranslation(string engineId, MissionTime now)
    {
        var burn = _translationById[engineId];
        if (_flight is null || _flight.Docked || burn.DurationS <= 0 || burn.Thrusters.Length == 0) return false;
        foreach (var docked in _docked) docked.EndMs = -1;
        foreach (var other in _translations) other.EndMs = -1;
        burn.EndMs = now.Milliseconds + MissionTime.FromSeconds(burn.DurationS).Milliseconds;
        foreach (var thruster in burn.Thrusters) RecordDraw(burn.Vehicle, thruster, burn.DurationS, now);
        if (CommandedBurn is { } commanded && EngineOf(burn.Vehicle, burn.Thrusters) is { } engine)
        {
            var state = _flight.Kinematics.State;
            var localRad = state.OrbitSense * Angles.Wrap(state.VelocityMps.Angle - burn.DirectionDeg * Angles.DegToRad);
            commanded(burn.VehicleId, new ManeuverPlan(engine, BurnDirection.Custom, burn.DurationS, now, localRad * Angles.RadToDeg));
        }
        Hold(burn.VehicleId, burn.DirectionDeg, now);
        return true;
    }

    /// <returns>False (no-op) when not docked or the engine is missing or has failed.</returns>
    public bool StartDocked(string engineId, double durationS, MissionTime now)
    {
        var burn = _dockedById[engineId];
        if (_flight is null || !_flight.Docked || burn.Engine < 0 || durationS <= 0) return false;
        if (_vehicles[burn.Vehicle][burn.Engine].Condition == Vehicle.ComponentCondition.Failed) return false;
        foreach (var translation in _translations) translation.EndMs = -1;
        foreach (var other in _docked)
        {
            other.EndMs = -1;
            if (other != burn) other.PendingS = 0;
        }
        burn.PendingS = durationS;
        burn.AlignDeadlineMs = now.Milliseconds + MissionTime.FromSeconds(MaxAlignS).Milliseconds;
        HoldStackPrograde(burn, now);
        return true;
    }

    /// <summary>
    /// The <c>flight.retrofire</c> command. With the crewed vehicle's attitude control active, the capsule first turns to retro
    /// attitude and the sequence starts once it is within <see cref="AlignToleranceDeg"/> (or after <see cref="MaxRetroAlignS"/>);
    /// without attitude control it starts at once, wherever the capsule points (GDD 12.7: off-axis retrofire).
    /// </summary>
    public void RequestRetrofire(MissionTime now)
    {
        if (_flight is null || _flight.ScheduledBurn is not null || RetrofirePending) return;
        var crewed = _attitude.GetVehicle(_crewedId);
        if (crewed.Mode == AttitudeMode.Off || crewed.ControlSetId.Length == 0)
        {
            _flight.Retrofire(now);
            return;
        }
        _retroAlignDeadlineMs = now.Milliseconds + MissionTime.FromSeconds(MaxRetroAlignS).Milliseconds;
        Hold(_crewedId, RetroDeg(), now);
    }

    /// <summary>Stops session burns (e.g. before undocking); flight leaves its powered phase on its next step.</summary>
    public void Cancel(MissionTime now)
    {
        foreach (var burn in _translations) burn.EndMs = -1;
        foreach (var burn in _docked)
        {
            burn.EndMs = -1;
            burn.PendingS = 0;
        }
        ReleaseHold(now);
    }

    /// <summary>Thrust for flight's coming step, firing the engines for that step.</summary>
    public PoweredInput Sample(in StepContext context, in FlightKinematics kinematics)
    {
        var stepStartMs = context.Now.Milliseconds - PeriodTicks * SimulationClock.BaseTickMs;
        foreach (var burn in _translations)
        {
            if (burn.EndMs >= 0) return SampleTranslation(burn, context, stepStartMs);
        }
        foreach (var burn in _docked)
        {
            if (burn.EndMs >= 0) return SampleDocked(burn, context, kinematics, stepStartMs);
        }
        return PoweredInput.Idle;
    }

    /// <summary>Signed in-plane error of the thrust axis for flight's programmed burn.</summary>
    public double ProgrammedPointingErrorRad()
    {
        if (_flight?.ScheduledBurn is not { } burn) return 0;
        var desired = BurnFrames.InertialAngleRad(burn.Plan.LocalAngleRad, _flight.Kinematics.State);
        return Angles.Wrap(_attitude.GetVehicle(_crewedId).PointingDeg * Angles.DegToRad - desired);
    }

    public void Step(in StepContext context)
    {
        BurnRetroRockets(context.DeltaSeconds);
        if (_flight is null) return;
        IgniteRetroRockets(context.Now);

        if (RetrofirePending)
        {
            AlignRetro(context.Now);
            if (RetrofirePending) return;
        }
        if (_flight.ScheduledBurn is { } programmed)
        {
            var desiredDeg = BurnFrames.InertialAngleRad(programmed.Plan.LocalAngleRad, _flight.Kinematics.State) * Angles.RadToDeg;
            Hold(_crewedId, desiredDeg, context.Now);
            return;
        }
        foreach (var burn in _translations)
        {
            if (burn.EndMs < 0) continue;
            Hold(burn.VehicleId, burn.DirectionDeg, context.Now);
            return;
        }
        foreach (var burn in _docked)
        {
            if (!burn.Active) continue;
            AlignAndIgnite(burn, context.Now);
            return;
        }
        ReleaseHold(context.Now);
    }

    /// <remarks>Order: translation burns, docked burns, then shared state, then docked Δv (mission 1 hashes as before).</remarks>
    public void WriteHash(StateHashWriter writer)
    {
        foreach (var burn in _translations)
        {
            writer.Write(burn.DirectionDeg);
            writer.Write(burn.DurationS);
            writer.Write(burn.EndMs);
        }
        foreach (var burn in _docked)
        {
            writer.Write(burn.EndMs);
            writer.Write(burn.PendingS);
            writer.Write(burn.AlignDeadlineMs);
        }
        writer.Write(_retroAlignDeadlineMs);
        writer.Write(_holdVehicle ?? "");
        writer.Write((long)_modeBefore);
        foreach (var burn in _docked) writer.Write(burn.DeltaVMps);
        writer.Write((long)_knownDraws.Count);
        foreach (var draw in _knownDraws)
        {
            writer.Write(draw.TankId);
            writer.Write(draw.Time.Milliseconds);
            writer.Write(draw.MassKg);
        }
    }

    /// <summary>The engines as one constant-thrust bank (thrust-weighted Isp, propellant in their tanks now); null without thrust.</summary>
    private ConstantThrustEngine? EngineOf(int vehicle, IReadOnlyList<int> engines)
    {
        var state = _vehicles[vehicle];
        double thrustN = 0, flowKgPerS = 0, propellantKg = 0;
        var tanks = new List<ComponentId>();
        foreach (var index in engines)
        {
            if (state.Layout.ThrusterSpecOf(index) is not { IspS: > 0 } spec) continue;
            thrustN += spec.ThrustN;
            flowKgPerS += spec.ThrustN / (spec.IspS * PhysicalConstants.StandardGravity);
            if (tanks.Contains(spec.Tank)) continue;
            tanks.Add(spec.Tank);
            if (state.Layout.TryGetIndex(spec.Tank.Value, out var tank)) propellantKg += state[tank].QuantityKg;
        }
        return thrustN > 0 ? new ConstantThrustEngine(thrustN, thrustN / flowKgPerS / PhysicalConstants.StandardGravity, propellantKg) : null;
    }

    private void RecordDraw(int vehicle, int engine, double durationS, MissionTime now)
    {
        if (_vehicles[vehicle].Layout.ThrusterSpecOf(engine) is not { IspS: > 0 } spec) return;
        var massKg = spec.ThrustN / (spec.IspS * PhysicalConstants.StandardGravity) * durationS;
        var tank = spec.Tank.Value;
        for (var i = 0; i < _knownDraws.Count; i++)
        {
            if (_knownDraws[i].TankId != tank || _knownDraws[i].Time != now) continue;
            _knownDraws[i] = _knownDraws[i] with { MassKg = _knownDraws[i].MassKg + massKg };
            return;
        }
        _knownDraws.Add(new KnownDraw(tank, now, massKg));
    }

    private int[] TranslationThrusters(BurnEngineDefinition engine, int vehicle, string file, string path, DefinitionErrors errors)
    {
        var layout = _vehicles[vehicle].Layout;
        var thrusters = new List<int>();
        if (engine.Thrusters is { Count: > 0 } listed)
        {
            for (var t = 0; t < listed.Count; t++)
            {
                if (layout.TryGetIndex(listed[t].Value, out var index) && layout.ThrusterSpecOf(index) is not null) thrusters.Add(index);
                else errors.Add(file, $"{path}.thrusters[{t}]", $"'{listed[t]}' is not a thruster of '{engine.Vehicle}'.");
            }
            return [.. thrusters];
        }
        foreach (var thruster in layout.Thrusters)
        {
            if (layout.GroupOf(thruster) < 0 && layout.DirectionUnit(thruster).X > 0.99) thrusters.Add(thruster);
        }
        if (thrusters.Count == 0) errors.Add(file, path, $"'{engine.Vehicle}' has no ungrouped thruster pushing along +X; list the thrusters.");
        return [.. thrusters];
    }

    private int MainEngine(BurnEngineDefinition engine, int vehicle, string file, string path, DefinitionErrors errors)
    {
        var layout = _vehicles[vehicle].Layout;
        if (engine.Engine is { } id)
        {
            foreach (var candidate in layout.Engines)
            {
                if (layout.IdOf(candidate) == id) return candidate;
            }
            errors.Add(file, path + ".engine", $"'{id}' is not an engine of '{engine.Vehicle}'.");
            return -1;
        }
        if (layout.Engines.Length > 0) return layout.Engines[0];
        errors.Add(file, path, $"'{engine.Vehicle}' has no engine.");
        return -1;
    }

    private PoweredInput SampleTranslation(TranslationBurn burn, in StepContext context, long stepStartMs)
    {
        if (stepStartMs >= burn.EndMs || _flight!.Docked) return Stop(burn, context.Now);
        var propulsion = _vehicles.Propulsion(burn.Vehicle);
        var layout = _vehicles[burn.Vehicle].Layout;
        var thrustN = 0.0;
        var flowKgPerS = 0.0;
        foreach (var thruster in burn.Thrusters)
        {
            var delivered = propulsion.Fire(thruster, 1, context.DeltaSeconds, context.Now);
            if (delivered <= 0) continue;
            var spec = layout.ThrusterSpecOf(thruster)!;
            thrustN += delivered;
            flowKgPerS += delivered / (spec.IspS * PhysicalConstants.StandardGravity);
        }
        if (thrustN <= 0) return Stop(burn, context.Now);

        var directionRad = burn.DirectionDeg * Angles.DegToRad;
        var errorRad = Angles.Wrap(_attitude.GetVehicle(burn.VehicleId).PointingDeg * Angles.DegToRad - directionRad);
        return new PoweredInput(thrustN, thrustN / (flowKgPerS * PhysicalConstants.StandardGravity), directionRad, errorRad);
    }

    private PoweredInput SampleDocked(DockedBurn burn, in StepContext context, in FlightKinematics kinematics, long stepStartMs)
    {
        if (stepStartMs >= burn.EndMs || !_flight!.Docked) return Stop(burn, context.Now);
        var thrustN = _vehicles.Propulsion(burn.Vehicle).Fire(burn.Engine, 1, context.DeltaSeconds, context.Now);
        if (thrustN <= 0) return Stop(burn, context.Now);

        var spec = _vehicles[burn.Vehicle].Layout.ThrusterSpecOf(burn.Engine)!;
        var progradeRad = BurnFrames.InertialAngleRad(0, kinematics.State);
        var errorRad = Angles.Wrap(_attitude.GetVehicle(burn.VehicleId).PointingDeg * Angles.DegToRad - progradeRad);
        burn.DeltaVMps += thrustN / StackMassKg() * context.DeltaSeconds * Math.Cos(errorRad);
        return new PoweredInput(thrustN, spec.IspS, progradeRad, errorRad);
    }

    /// <summary>The docked stack: the crewed vehicle and the flight target.</summary>
    private double StackMassKg()
    {
        var massKg = _vehicles[_crewed].ComputeMassProperties().MassKg;
        if (_flight!.TargetId is { } target) massKg += _vehicles.Get(target).ComputeMassProperties().MassKg;
        return massKg;
    }

    private PoweredInput Stop(TranslationBurn burn, MissionTime now)
    {
        burn.EndMs = -1;
        ReleaseHold(now);
        return PoweredInput.Idle;
    }

    private PoweredInput Stop(DockedBurn burn, MissionTime now)
    {
        burn.EndMs = -1;
        ReleaseHold(now);
        return PoweredInput.Idle;
    }

    /// <summary>
    /// Turns the stack until the engine's axis is within <see cref="AlignToleranceDeg"/> of prograde (or <see cref="MaxAlignS"/>
    /// have passed), then ignites for the commanded duration.
    /// </summary>
    private void AlignAndIgnite(DockedBurn burn, MissionTime now)
    {
        if (_flight is null || !_flight.Docked)
        {
            Cancel(now);
            return;
        }
        HoldStackPrograde(burn, now);
        if (burn.PendingS <= 0) return;
        var errorDeg = Math.Abs(Angles.Wrap((_attitude.GetVehicle(burn.VehicleId).PointingDeg - ProgradeDeg()) * Angles.DegToRad)) * Angles.RadToDeg;
        if (errorDeg > AlignToleranceDeg && now.Milliseconds < burn.AlignDeadlineMs) return;
        burn.EndMs = now.Milliseconds + MissionTime.FromSeconds(burn.PendingS).Milliseconds;
        RecordDraw(burn.Vehicle, burn.Engine, burn.PendingS, now);
        if (CommandedBurn is { } commanded && EngineOf(burn.Vehicle, [burn.Engine]) is { } engine)
            commanded(_crewedId, new ManeuverPlan(engine, BurnDirection.Prograde, burn.PendingS, now));
        burn.PendingS = 0;
    }

    /// <summary>
    /// Holds the docked stack with whichever member is flying it (the crewed vehicle when its own control is active, else the
    /// engine's vehicle), aiming the engine's axis prograde.
    /// </summary>
    private void HoldStackPrograde(DockedBurn burn, MissionTime now)
    {
        var crewed = _attitude.GetVehicle(_crewedId);
        var engineVehicle = _attitude.GetVehicle(burn.VehicleId);
        var holder = _holdVehicle ?? (crewed.Mode != AttitudeMode.Off ? _crewedId : burn.VehicleId);
        var offsetDeg = holder == _crewedId ? engineVehicle.PointingDeg - crewed.PointingDeg : 0;
        Hold(holder, ProgradeDeg() - offsetDeg, now);
    }

    private void AlignRetro(MissionTime now)
    {
        var target = RetroDeg();
        Hold(_crewedId, target, now);
        var errorDeg = Math.Abs(Angles.Wrap((_attitude.GetVehicle(_crewedId).PointingDeg - target) * Angles.DegToRad)) * Angles.RadToDeg;
        if (errorDeg > AlignToleranceDeg && now.Milliseconds < _retroAlignDeadlineMs) return;
        _retroAlignDeadlineMs = -1;
        _flight!.Retrofire(now);
    }

    private double RetroDeg() =>
        _flight is null ? 0 : BurnFrames.InertialAngleRad(BurnFrames.LocalAngleRad(BurnDirection.Retrograde, 0), _flight.Kinematics.State) * Angles.RadToDeg;

    private double ProgradeDeg() => _flight is null ? 0 : BurnFrames.InertialAngleRad(0, _flight.Kinematics.State) * Angles.RadToDeg;

    private void Hold(string vehicleId, double targetDeg, MissionTime now)
    {
        if (_holdVehicle != vehicleId)
        {
            ReleaseHold(now);
            var vehicle = _attitude.GetVehicle(vehicleId);
            _holdVehicle = vehicleId;
            _modeBefore = vehicle.Mode;
            if (vehicle.ControlSetId.Length > 0) _attitude.SetControl(vehicleId, AttitudeMode.AttitudeHold, vehicle.ControlSetId, now);
        }
        _attitude.SetHoldTarget(vehicleId, Angles.WrapPositive(targetDeg * Angles.DegToRad) * Angles.RadToDeg);
    }

    private void ReleaseHold(MissionTime now)
    {
        if (_holdVehicle is null) return;
        var vehicle = _attitude.GetVehicle(_holdVehicle);
        if (vehicle.Mode == AttitudeMode.AttitudeHold) _attitude.SetControl(_holdVehicle, _modeBefore, vehicle.ControlSetId, now);
        _holdVehicle = null;
    }

    /// <summary>
    /// Ignites the crewed vehicle's retro rockets on flight's staggered schedule (SESSION_EFFECTS_NEEDED hook 3), so the vehicle
    /// mass and the <c>retrofire</c> mission event follow the burn flight actually flies.
    /// </summary>
    private void IgniteRetroRockets(MissionTime now)
    {
        if (_flight!.ScheduledBurn is not { Plan.Engine: RetroSequenceEngine retro } burn || now < burn.StartTime) return;
        var elapsedS = (now.Milliseconds - burn.StartTime.Milliseconds) / 1000.0;
        var crewed = _vehicles[_crewed];
        foreach (var rocket in retro.Sequence.Rockets)
        {
            if (elapsedS < rocket.IgnitionOffsetS || !crewed.Layout.TryGetIndex(rocket.Id.Value, out var index) || crewed[index].Fired) continue;
            _vehicles.Propulsion(_crewed).IgniteRetro(index, now);
        }
    }

    private void BurnRetroRockets(double deltaSeconds)
    {
        for (var v = 0; v < _vehicles.Count; v++)
        {
            var propulsion = _vehicles.Propulsion(v);
            foreach (var rocket in propulsion.RetroRockets)
            {
                if (_vehicles[v][rocket].Fired) propulsion.BurnRetro(rocket, deltaSeconds);
            }
        }
    }

    private sealed class TranslationBurn(string vehicleId, int vehicle, int[] thrusters)
    {
        public string VehicleId { get; } = vehicleId;
        public int Vehicle { get; } = vehicle;
        public int[] Thrusters { get; } = thrusters;
        public double DirectionDeg { get; set; }
        public double DurationS { get; set; }
        public long EndMs { get; set; } = -1;
    }

    private sealed class DockedBurn(string vehicleId, int vehicle, int engine)
    {
        public string VehicleId { get; } = vehicleId;
        public int Vehicle { get; } = vehicle;
        public int Engine { get; } = engine;
        public long EndMs { get; set; } = -1;
        public double PendingS { get; set; }
        public long AlignDeadlineMs { get; set; } = -1;
        public double DeltaVMps { get; set; }

        public bool Active => EndMs >= 0 || PendingS > 0;
    }
}
