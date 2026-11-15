using Usa.Game.Presentation.Rendering;
using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Game.Presentation.Spacecraft.Demo;

/// <summary>
/// Scripted stand-in for the session adapter (diagnostics only). Timeline, repeating every <see cref="LoopSec"/>:
/// docked stack in a slow roll that builds up while OAMS T8 is stuck at 40 % and the other thrusters and Lodestar's
/// ACS fight it; at 2 s a crew glance reveals the Lodestar status panel (its lights start showing what the crew saw:
/// ACS high activity, latched, engine armed, power); at 5 s the crew's inspection reveals
/// <c>tandem/oams/thruster_08</c> as damaged (<c>stuck_open</c>); at 10 s the crew undocks (engine disarmed), Lodestar
/// drifts off and Tandem's roll speeds up. Values change only on 50 ms "ticks" like the real attitude model, so the
/// view's extrapolation is exercised. Keys in the demo scene toggle staging, chutes, entry heating, Lodestar power, a
/// fast orbit (Sun glow setting behind the Earth) and reveal further components (outline flash).
/// <see cref="StartDockingApproach"/> replays the docking: the stack apart, Lodestar 50 m ahead and a docking task whose
/// progress runs 0 → 1 over the given seconds (pre-incident: no stuck firing, slow rates), then docked and the timeline resumes.
/// <see cref="StartEntry"/> fast-forwards an entry from the 120 km interface to drogue deploy (see there).
/// <para>It obeys the port's knowledge rules like the session adapter: attitude-thruster plumes show only once the
/// thruster is confirmed (T8 from the 5 s reveal; the healthy ones with <c>--demo-reveal-all</c> or key 8), and the
/// status panel shows only what the crew's glance saw.</para>
/// </summary>
public sealed class DemoSpacecraftSource : ISpacecraftViewSource
{
    public const double LoopSec = 40.0;
    public const double PanelGlanceAtSec = 2.0;
    public const double RevealAtSec = 5.0;
    public const double UndockAtSec = 10.0;

    private const double TickSec = 0.05;

    // The stack shows apart for a moment before the crew starts the docking task.
    private const double ApproachPreRollSec = 1.5;

    // The crew starts the task half-way through the pre-roll; it shows 0 % until the pre-roll ends.
    private const double ApproachStartedAtSec = 0.75;
    private const double DegToRad = Math.PI / 180.0;
    private const double OrbitPeriodSec = 5400.0;
    private const double FastOrbitPeriodSec = 60.0;
    private const double RecoveryWaitSec = 30.0;
    private const string Tandem = "tandem";
    private const string Lodestar = "lodestar";
    private const string StuckThruster = "tandem/oams/thruster_08";
    private const string StatusPanel = "lodestar/status_panel";

    // Revealed one by one on demand, to exercise the outline flash on parts of different sizes.
    private static readonly string[] RevealOnDemand =
    [
        "tandem/nose/radar", "lodestar/docking_cone", "tandem/rcs/ring_a", "tandem/adapter/radiator", "lodestar/engine",
        "tandem/retro/rocket_2", "tandem/rm/hatch_1",
    ];

    // Mated Tandem → Lodestar origin offset in Tandem's body frame, real metres (the scene's mated 1.834 m toy / 0.75).
    private static readonly Vec3d DockOffsetBodyM = new(1.834 / SpacecraftAxes.ToyScale, 0, 0);
    private static readonly Quatd HalfTurnAboutYaw = new(0, 0, 0, 1);

    private static readonly string[] AcsThrusters = ["lodestar/acs/t01", "lodestar/acs/t02", "lodestar/acs/t03", "lodestar/acs/t04"];

    private static readonly SeparableSection[] TandemSections = [new("adapter", "equipment_sep"), new("retro", "retro_sep")];

    private static readonly string[] NonRevealable =
    [
        "tandem/rm/windows", "tandem/adapter/handrail", "tandem/chute/drogue", "tandem/chute/main", "lodestar/aft_body",
    ];

    private readonly Dictionary<string, double> _firing = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ComponentKnowledgeView> _knowledge = new(StringComparer.Ordinal);
    private readonly HashSet<string> _jettisoned = new(StringComparer.Ordinal);
    private readonly HashSet<string> _alwaysConfirmed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ChuteDeployState> _chutes = new(StringComparer.Ordinal);
    private readonly Pcg32 _random = new(20261114, 7);
    private double _accumulatorSec;
    private Quatd _tandemOrientation = Quatd.Identity;
    private Quatd _lodestarOrientation = HalfTurnAboutYaw;
    private Vec3d _tandemRate;
    private Vec3d _lodestarPositionM;
    private Vec3d _lodestarVelocityMps;
    private bool _docked = true;
    private bool _heating;
    private double _heating01;
    private bool _lodestarPowered = true;
    private bool _fastOrbit;
    private bool _splashedDown;
    private double _recoveryProgress01;
    private double _orbitAngleRad;
    private int _nextOnDemand;
    private double? _approach01;
    private double _approachSec = 8;
    private double _approachPreRollSec;

    /// <summary>Starts at the beginning of the timeline with the opening knowledge.</summary>
    public DemoSpacecraftSource()
    {
        Restart();
        Backdrop = new OrbitalBackdropView(new Vec3d(-1, 0, 0), new Vec3d(0.35, 0.80, 0.48).Normalized(), 300.0);
        _lodestarPositionM = DockOffsetBodyM;
    }

    public string PrimaryVehicleId => Tandem;

    public string? TargetVehicleId => Lodestar;

    public string? StatusPanelComponentId => StatusPanel;

    public double MetSec { get; private set; }

    /// <summary>Seconds since the current loop started.</summary>
    public double LoopTimeSec { get; private set; }

    public bool Docked => _docked;

    /// <summary>The docking task's progress while <see cref="StartDockingApproach"/> plays, null otherwise.</summary>
    public double? DockingApproach01 => _approachPreRollSec > ApproachStartedAtSec ? null : _approach01;

    public double RollRateDegPerSec => _tandemRate.X / DegToRad;

    public OrbitalBackdropView Backdrop { get; private set; }

    public int KnowledgeRevision { get; private set; }

    /// <summary>Null in orbit; after splashdown the demo's recovery wait runs 0 → 1 in 30 s.</summary>
    public double? RecoveryProgress01 => _splashedDown ? _recoveryProgress01 : null;

    public void Advance(double deltaSec)
    {
        _accumulatorSec += deltaSec;
        while (_accumulatorSec >= TickSec)
        {
            _accumulatorSec -= TickSec;
            Tick();
        }
    }

    /// <summary>
    /// Undocks the stack where it is, puts Lodestar 50 m ahead slightly off the docking axis and, after a 1.5 s pre-roll,
    /// runs a docking task over <paramref name="durationSec"/> seconds (the view flies the approach from its progress); docked at the end.
    /// </summary>
    public void StartDockingApproach(double durationSec)
    {
        _approachSec = Math.Max(durationSec, 0.5);
        _approach01 = 0;
        _approachPreRollSec = ApproachPreRollSec;
        _docked = false;
        _lodestarVelocityMps = Vec3d.Zero;
        // Nose along the local horizontal (prograde), Lodestar leading on the orbit, as for the real rendezvous.
        _tandemOrientation = Quatd.FromAxisAngle(new Vec3d(0, 0, 1), _orbitAngleRad + Math.PI / 2);
        _lodestarPositionM = _tandemOrientation.Rotate(new Vec3d(50, 6, -4));
        _lodestarOrientation = _tandemOrientation * HalfTurnAboutYaw * Quatd.FromAxisAngle(new Vec3d(0, 0, 1), 0.2);
        _knowledge.Remove(StuckThruster);
        KnowledgeRevision++;
    }

    public void ToggleJettison(string sectionId)
    {
        if (!_jettisoned.Add(sectionId)) _jettisoned.Remove(sectionId);
    }

    private static readonly Quatd NoseDownTurn = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), Math.PI);
    private bool _noseDown;

    /// <summary>Turns Tandem end over end (or back), to check it still hangs nose-up under a chute.</summary>
    public bool ToggleNoseDown() => _noseDown = !_noseDown;

    public ChuteDeployState CycleChute(string chuteId)
    {
        var next = GetChuteState(Tandem, chuteId) switch
        {
            ChuteDeployState.Stowed => ChuteDeployState.Deploying,
            ChuteDeployState.Deploying => ChuteDeployState.Reefed,
            ChuteDeployState.Reefed => ChuteDeployState.Open,
            ChuteDeployState.Open => ChuteDeployState.Released,
            _ => ChuteDeployState.Stowed,
        };
        _chutes[chuteId] = next;
        return next;
    }

    public void ToggleHeating() => _heating = !_heating;

    private const double EntryInterfaceM = 120_000;
    private const double EntryDrogueM = 12_000;
    private const double EntrySec = 24;
    private const double EarthRadiusM = 6_371_000;
    private double? _entrySec;

    /// <summary>Seconds since <see cref="StartEntry"/> (Lodestar is gone, the timeline stops); null before.</summary>
    public double? EntryTimeSec => _entrySec;

    /// <summary>
    /// A fast-forwarded entry: adapter and retro pack dropped, Lodestar gone, the capsule left in the inertial attitude it had
    /// after retrofire (nose near the local horizontal, rate-damped), from 120 km down to drogue deploy at 12 km in
    /// <see cref="EntrySec"/> s. The flight path steepens from -1.5° to -60° (the track's nadir turns and its altitude
    /// drops in that ratio, all the view can see of the motion) and the heating peaks half-way. The drogue opens at the end.
    /// </summary>
    public void StartEntry()
    {
        _entrySec = 0;
        _docked = false;
        _approach01 = null;
        _jettisoned.Add("adapter");
        _jettisoned.Add("retro");
        _chutes["tandem/chute/drogue"] = ChuteDeployState.Stowed;
        _chutes["tandem/chute/main"] = ChuteDeployState.Stowed;
        _splashedDown = false;
        _noseDown = false;
        _heating = false;
        _tandemOrientation = Quatd.FromAxisAngle(new Vec3d(0, 0, 1), _orbitAngleRad + Math.PI / 2 + 0.35);
    }

    private const double MainDeployM = 3200;
    private const double DrogueFallMps = 2200;
    private const double MainFallMps = 640;
    private const double TouchdownTaperSec = 0.25;
    private double? _descentAltitudeM;

    /// <summary>
    /// The fast-forwarded fall under the chutes as the game paces it: from the drogue at 12 km (or wherever the entry got to) to
    /// splashdown in about 10 s, the main opening at 3.2 km and the fall easing out over the last half second, the sea rising to
    /// meet the capsule the whole way.
    /// </summary>
    public void StartDescent()
    {
        _descentAltitudeM = _entrySec is { } entrySec ? EntryAltitudeM(Math.Clamp(entrySec / EntrySec, 0, 1)) : EntryDrogueM;
        _chutes["tandem/chute/drogue"] = ChuteDeployState.Open;
        _chutes["tandem/chute/main"] = ChuteDeployState.Stowed;
        _splashedDown = false;
        _recoveryProgress01 = 0;
    }

    /// <summary>Splashes Tandem down (the view switches to the sea) or puts it back in orbit; the recovery wait restarts.</summary>
    public bool ToggleSplashdown()
    {
        _splashedDown = !_splashedDown;
        _recoveryProgress01 = 0;
        return _splashedDown;
    }

    public bool ToggleLodestarPower() => _lodestarPowered = !_lodestarPowered;

    /// <summary>A one-minute orbit, so the Sun sets behind the Earth and rises again within a demo loop.</summary>
    public bool ToggleFastOrbit() => _fastOrbit = !_fastOrbit;

    /// <summary>Jumps along the orbit (scripted shots); the Sun sets behind the Earth at about 175°.</summary>
    public void SetOrbitAngleDeg(double degrees) => _orbitAngleRad = degrees * DegToRad;

    /// <summary>Confirms the next part of <see cref="RevealOnDemand"/> nominal; null when all are revealed.</summary>
    public string? RevealNext()
    {
        while (_nextOnDemand < RevealOnDemand.Length)
        {
            var id = RevealOnDemand[_nextOnDemand++];
            if (_knowledge.ContainsKey(id)) continue;
            _knowledge[id] = new ComponentKnowledgeView(ComponentLookState.ConfirmedNominal, null, false);
            KnowledgeRevision++;
            return id;
        }
        return null;
    }

    /// <summary>
    /// Confirms a part nominal for good (it survives the loop restart), to judge the palette look. The parts the
    /// timeline reveals (status panel, T8) and non-revealable parts are left alone.
    /// </summary>
    public void ConfirmNominal(string componentId)
    {
        if (componentId is StuckThruster or StatusPanel || !IsRevealable(componentId)) return;
        if (!_alwaysConfirmed.Add(componentId)) return;
        _knowledge[componentId] = new ComponentKnowledgeView(ComponentLookState.ConfirmedNominal, null, false);
        KnowledgeRevision++;
    }

    /// <summary>
    /// Knowledge-gated panel: nothing is known until the crew has looked at it; then what they saw. Power off is
    /// reported by the ground (downlink), so it is known at any time.
    /// </summary>
    public LodestarPanelView GetLodestarPanel()
    {
        if (!_lodestarPowered) return new LodestarPanelView(null, null, null, PowerOn: false);
        if (!_knowledge.ContainsKey(StatusPanel)) return LodestarPanelView.Unknown;
        return new LodestarPanelView(AcsActivity(), _docked, EngineArmed: _docked, PowerOn: true);
    }

    /// <summary>What the crew reads off the panel: the mean ACS valve activity (the panel's own sensor, not the plumes).</summary>
    private double AcsActivity()
    {
        var sum = 0.0;
        foreach (var id in AcsThrusters) sum += _firing.GetValueOrDefault(id);
        return sum / AcsThrusters.Length;
    }

    public bool TryGetVehicle(string vehicleId, out SpacecraftVehicleView vehicle)
    {
        switch (vehicleId)
        {
            case Tandem:
                vehicle = new SpacecraftVehicleView(
                    _noseDown ? _tandemOrientation * NoseDownTurn : _tandemOrientation, _tandemRate, Vec3d.Zero, _docked ? Lodestar : null, _heating01, _splashedDown);
                return true;
            case Lodestar when _entrySec is null:
                vehicle = new SpacecraftVehicleView(
                    _lodestarOrientation, Vec3d.Zero, _lodestarPositionM, _docked ? Tandem : null, 0, false);
                return true;
            default:
                vehicle = default;
                return false;
        }
    }

    /// <summary>The plume rule of the port: attitude thrusters show only once confirmed; engines and retros always.</summary>
    public double GetFiringFraction(string vehicleId, string thrusterComponentId)
    {
        var fraction = _firing.GetValueOrDefault(thrusterComponentId);
        if (fraction <= 0 || GetPlumeKind(thrusterComponentId) != PlumeKind.AttitudeThruster) return fraction;
        return GetComponentKnowledge(thrusterComponentId).State is ComponentLookState.ConfirmedNominal or ComponentLookState.ConfirmedDamaged
            ? fraction
            : 0;
    }

    public PlumeKind GetPlumeKind(string componentId) => componentId switch
    {
        _ when componentId.StartsWith("tandem/retro/rocket", StringComparison.Ordinal) => PlumeKind.RetroRocket,
        "lodestar/engine" => PlumeKind.Engine,
        _ when componentId.StartsWith("tandem/oams/thruster", StringComparison.Ordinal) ||
               componentId.StartsWith("tandem/rcs/", StringComparison.Ordinal) ||
               componentId.StartsWith("lodestar/acs/t", StringComparison.Ordinal) => PlumeKind.AttitudeThruster,
        _ => PlumeKind.None,
    };

    public IReadOnlyList<SeparableSection> GetSeparableSections(string vehicleId) => vehicleId == Tandem ? TandemSections : [];

    public bool IsSectionJettisoned(string vehicleId, string sectionId) => _jettisoned.Contains(sectionId);

    public ChuteDeployState GetChuteState(string vehicleId, string chuteComponentId) =>
        _chutes.GetValueOrDefault(chuteComponentId, ChuteDeployState.Stowed);

    public bool IsRevealable(string componentId) => Array.IndexOf(NonRevealable, componentId) < 0;

    public ComponentKnowledgeView GetComponentKnowledge(string componentId) =>
        _knowledge.GetValueOrDefault(componentId, ComponentKnowledgeView.Unknown);

    private void Tick()
    {
        MetSec += TickSec;
        if (_approach01 is { } approach)
        {
            if (_approachPreRollSec > 0) _approachPreRollSec -= TickSec;
            else _approach01 = approach + TickSec / _approachSec;
            if (_approach01 >= 1)
            {
                // Contact: the task succeeds and the timeline resumes from the docked start.
                _approach01 = null;
                _docked = true;
                LoopTimeSec = 0;
            }
        }
        else if (_entrySec is null)
        {
            LoopTimeSec += TickSec;
        }
        if (LoopTimeSec >= LoopSec) Restart();
        var t = LoopTimeSec;

        if (_docked && t >= UndockAtSec) Undock();

        // Roll builds slowly while docked; undocking drops the inertia and the stuck thruster spins Tandem up fast.
        var rollDegPerSec = _entrySec is not null ? 0.2 : _approach01 is not null ? 0.3 : _docked ? 2.0 + 0.9 * t : Math.Min(11.0 + 9.0 * (t - UndockAtSec), 140.0);
        _tandemRate = new Vec3d(rollDegPerSec, 0.4 * Math.Sin(0.7 * t), 0.25 * Math.Cos(0.5 * t)) * DegToRad;
        _tandemOrientation = SpacecraftAxes.Extrapolate(_tandemOrientation, _tandemRate, TickSec);

        if (_docked)
        {
            _lodestarOrientation = _tandemOrientation * HalfTurnAboutYaw;
            _lodestarPositionM = _tandemOrientation.Rotate(DockOffsetBodyM);
        }
        else
        {
            _lodestarPositionM += _lodestarVelocityMps * TickSec;
        }

        UpdateFiring();
        UpdateKnowledge(t);

        _heating01 = Math.Clamp(_heating01 + (_heating ? 0.25 : -0.5) * TickSec, 0.0, 1.0);
        if (_entrySec is { } entrySec && _descentAltitudeM is null && !_splashedDown)
        {
            TickEntry(entrySec);
            return;
        }
        if (_descentAltitudeM is { } altitudeM)
        {
            var underMain = altitudeM <= MainDeployM;
            var share = altitudeM / MainFallMps / TouchdownTaperSec;
            var fallMps = underMain ? MainFallMps * Math.Max(0.2, 1 - Math.Exp(-share * share)) : DrogueFallMps;
            _descentAltitudeM = altitudeM - fallMps * TickSec;
            if (!underMain && _descentAltitudeM <= MainDeployM)
            {
                _chutes["tandem/chute/main"] = ChuteDeployState.Open;
                _chutes["tandem/chute/drogue"] = ChuteDeployState.Released;
            }
            if (_descentAltitudeM <= 0)
            {
                _descentAltitudeM = null;
                _splashedDown = true;
            }
        }
        if (_splashedDown) _recoveryProgress01 = Math.Min(1.0, _recoveryProgress01 + TickSec / RecoveryWaitSec);
        _orbitAngleRad += 2 * Math.PI * TickSec / (_fastOrbit ? FastOrbitPeriodSec : OrbitPeriodSec);
        Backdrop = new OrbitalBackdropView(
            new Vec3d(-Math.Cos(_orbitAngleRad), -Math.Sin(_orbitAngleRad), 0),
            new Vec3d(0.35, 0.80, 0.48).Normalized(),
            _descentAltitudeM is { } descentM ? descentM / 1000.0 : 300.0);
    }

    private void TickEntry(double entrySec)
    {
        var u = Math.Clamp(entrySec / EntrySec, 0, 1);
        if (u < 1)
        {
            _entrySec = entrySec + TickSec;
            var pathAngleRad = (1.5 + 58.5 * u * u) * DegToRad;
            var dropM = (EntryInterfaceM - EntryDrogueM) / EntrySec * TickSec;
            _orbitAngleRad += dropM / Math.Tan(pathAngleRad) / (EarthRadiusM + EntryAltitudeM(u));
            _heating01 = Math.Sin(Math.PI * u);
        }
        else
        {
            _heating01 = 0;
            if (GetChuteState(Tandem, "tandem/chute/drogue") == ChuteDeployState.Stowed) _chutes["tandem/chute/drogue"] = ChuteDeployState.Open;
        }
        Backdrop = new OrbitalBackdropView(
            new Vec3d(-Math.Cos(_orbitAngleRad), -Math.Sin(_orbitAngleRad), 0),
            Backdrop.SunInertial,
            EntryAltitudeM(Math.Clamp(_entrySec!.Value / EntrySec, 0, 1)) / 1000.0);
    }

    private static double EntryAltitudeM(double share01) => EntryInterfaceM - (EntryInterfaceM - EntryDrogueM) * share01;

    private void Undock()
    {
        _docked = false;
        _lodestarVelocityMps = _lodestarPositionM.Normalized() * 0.35;
    }

    private void UpdateFiring()
    {
        _firing.Clear();
        if (_entrySec is not null) return;
        if (_approach01 is not null)
        {
            // Before the incident: the crew flies the approach (the view's commanded pulses); Lodestar's ACS only trims.
            for (var i = 1; i <= 4; i++)
            {
                if (_lodestarPowered && _random.Chance(0.05)) _firing[$"lodestar/acs/t0{i}"] = 1.0;
            }
            return;
        }
        _firing[StuckThruster] = 0.4;
        // OAMS auto-hold pulses the healthy attitude thrusters against the roll.
        for (var i = 1; i <= 7; i++)
        {
            if (_random.Chance(0.12)) _firing[$"tandem/oams/thruster_0{i}"] = _random.Range(0.4, 1.0);
        }
        // Lodestar's ACS works hard while docked (the red herring), only trims once free, and stops without power.
        for (var i = 1; i <= 4; i++)
        {
            if (_lodestarPowered && _random.Chance(_docked ? 0.3 : 0.05)) _firing[$"lodestar/acs/t0{i}"] = 1.0;
        }
        if (_jettisoned.Contains("adapter") && !_jettisoned.Contains("retro") && LoopTimeSec % 8 < 4)
        {
            for (var i = 1; i <= 4; i++) _firing[$"tandem/retro/rocket_{i}"] = 1.0;
        }
    }

    private void UpdateKnowledge(double t)
    {
        if (t >= PanelGlanceAtSec && !_knowledge.ContainsKey(StatusPanel))
        {
            _knowledge[StatusPanel] =
                new ComponentKnowledgeView(ComponentLookState.ConfirmedNominal, null, false);
            KnowledgeRevision++;
        }
        if (_approach01 is null && t >= RevealAtSec && !_knowledge.ContainsKey(StuckThruster))
        {
            _knowledge[StuckThruster] = new ComponentKnowledgeView(ComponentLookState.ConfirmedDamaged, "stuck_open", false);
            KnowledgeRevision++;
        }
    }

    private void Restart()
    {
        LoopTimeSec = 0;
        _docked = true;
        _knowledge.Clear();
        _nextOnDemand = 0;
        // An earlier inspection, now old: drawn in colour with the STALE tag from the first frame.
        _knowledge["tandem/nose/docking_bar"] = new ComponentKnowledgeView(ComponentLookState.ConfirmedNominal, null, true);
        foreach (var id in _alwaysConfirmed) _knowledge[id] = new ComponentKnowledgeView(ComponentLookState.ConfirmedNominal, null, false);
        KnowledgeRevision++;
    }
}
