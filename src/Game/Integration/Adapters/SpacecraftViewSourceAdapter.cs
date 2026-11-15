using Usa.Game.Presentation.EntryHud;
using Usa.Game.Presentation.Rendering;
using Usa.Game.Presentation.Spacecraft;
using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;
using Usa.Simulation.Ports;
using Usa.Simulation.Session;
using Usa.Simulation.Vehicle.Definitions;
using SimComponentKnowledge = Usa.Simulation.Knowledge.ComponentKnowledge;
using SimComponentState = Usa.Simulation.Knowledge.ComponentKnowledgeState;

namespace Usa.Game.Integration.Adapters;

/// <summary>Tuning of the 3D view source that the data does not carry.</summary>
/// <param name="PeakHeatingProxy">Heat-rate proxy (√ρ·v³) that reads as full entry glow.</param>
/// <param name="SunInertial">Sun direction; the simulation has no Sun model yet, so it is fixed in inertial axes.</param>
public sealed record SpacecraftViewOptions(double PeakHeatingProxy, Vec3d SunInertial)
{
    public static SpacecraftViewOptions Default { get; } = new(7.0e9, new Vec3d(0.6, 0.8, 0).Normalized());
}

/// <summary>
/// <see cref="ISpacecraftViewSource"/> over the session. <see cref="Refresh()"/> (once per frame) caches everything; the
/// per-frame reads are dictionary lookups that never allocate.
/// <para>Poses, staging, chutes and heating are truth (what the external camera sees). Component looks are knowledge only.
/// Firing fractions pass the plume rule of <see cref="ThrusterPlumeGate"/> (GDD 4.4, Appendix D: an unknown part must not
/// leak through particle effects).</para>
/// </summary>
/// <remarks>
/// With a <see cref="SessionFrameReader"/> the refresh reads narrow inputs (attitudes, section state paths, the knowledge
/// revision) and is skipped while the reader's stamp has not moved; components and the target's activity word are re-read only
/// when the knowledge revision changed. <see cref="Refresh(SessionSnapshot)"/> is the older path for hosts that already hold a
/// snapshot (the playthrough and session test rigs).
/// </remarks>
public sealed class SpacecraftViewSourceAdapter : ISpacecraftViewSource
{
    private const double DegToRad = Math.PI / 180;
    private const string DockedPath = StatePaths.Flight + "docked";
    private const string ActiveTaskPath = StatePaths.Crew + "task.id";

    private readonly MissionSession _session;
    private readonly FlightTrack _track;
    private readonly SpacecraftViewOptions _options;
    private readonly SessionFrameReader? _frame;
    private readonly string _primaryVehicle;
    private readonly string[] _vehicleIds;
    private readonly HashSet<string> _vehicles = new(StringComparer.Ordinal);
    private readonly HashSet<string> _revealable = new(StringComparer.Ordinal);
    private readonly ThrusterPlumeGate _plumes;
    private readonly double[] _fractions;
    private readonly Dictionary<string, string> _chutePaths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SeparableSection[]> _separable = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SectionPath[]> _sectionPaths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SpacecraftVehicleView> _views = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, bool>> _sections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ChuteDeployState> _chutes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ComponentKnowledgeView> _knowledge = new(StringComparer.Ordinal);
    private readonly FactId? _acsActivityFact;
    private readonly string? _acsPowerPath;
    private readonly string[] _crewIds;
    private readonly string[] _crewLocationPaths;
    private readonly string? _packagePath;
    private readonly HashSet<string> _dockingTasks = new(StringComparer.Ordinal);
    private TaskSlotView? _dockingSlot;
    private MissionTime _dockingSlotReadAt;
    private int _dockingSlotEvents;
    private LodestarPanelView _lodestarPanel = LodestarPanelView.Unknown;
    private double? _acsActivity;
    private SessionFrameStamp _refreshedAt;
    private bool _refreshed;
    private int _knowledgeReadAt;

    /// <param name="frame">The runtime's per-frame reader; without it only <see cref="Refresh(SessionSnapshot)"/> works.</param>
    public SpacecraftViewSourceAdapter(MissionSession session, FlightTrack track, SpacecraftViewOptions? options = null, SessionFrameReader? frame = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(track);
        _session = session;
        _track = track;
        _options = options ?? SpacecraftViewOptions.Default;
        _frame = frame;
        _primaryVehicle = track.CrewedId;
        if (track.TargetId is { } target)
        {
            _acsActivityFact = new FactId($"{target}.acs.activity");
            _acsPowerPath = $"{target}/acs/power.closed";
        }
        var ids = new List<string>();
        foreach (var vehicle in session.Data.Vehicles)
        {
            ids.Add(vehicle.Id);
            _vehicles.Add(vehicle.Id);
            var separable = new List<SeparableSection>();
            var sectionPaths = new SectionPath[vehicle.Sections.Count];
            var sections = new Dictionary<string, bool>(StringComparer.Ordinal);
            for (var s = 0; s < vehicle.Sections.Count; s++)
            {
                var section = vehicle.Sections[s];
                if (section.Jettison is { Length: > 0 } separation) separable.Add(new SeparableSection(section.Id, separation));
                sectionPaths[s] = new SectionPath(section.Id, $"{StatePaths.Vehicle}{vehicle.Id}.section.{section.Id}.attached");
                sections[section.Id] = false;
            }
            _separable[vehicle.Id] = [.. separable];
            _sectionPaths[vehicle.Id] = sectionPaths;
            _sections[vehicle.Id] = sections;
            foreach (var component in vehicle.Components)
            {
                var id = component.Id.Value;
                if (component.Revealable) _revealable.Add(id);
                if (component.Family == ComponentFamily.Parachute) _chutePaths[id] = $"{id}.state";
                if (vehicle.Id == track.TargetId && component.Sensor is { } sensor &&
                    sensor.Channels.Any(c => c.Measures.EndsWith(".activity_level", StringComparison.Ordinal)))
                {
                    StatusPanelComponentId ??= id;
                }
            }
        }
        _vehicleIds = [.. ids];
        var crew = session.Data.Scenario.Mission.Crew;
        _crewIds = new string[crew.Count];
        _crewLocationPaths = new string[crew.Count];
        for (var i = 0; i < crew.Count; i++)
        {
            _crewIds[i] = crew[i].Value;
            _crewLocationPaths[i] = $"{StatePaths.Crew}{crew[i].Value}.location";
        }
        const string packagePath = "mission.event.eva_package_retrieved.fired";
        _packagePath = session.State.IsKnownPath(packagePath) ? packagePath : null;
        // A docking task is one whose success docks the vehicles (flight.docked := true); the data names no task id here.
        foreach (var task in session.Data.Tasks.Tasks)
        {
            foreach (var effect in task.OnSuccess)
            {
                if (effect.Target == DockedPath && effect.Operation == EffectOperation.Set && effect.Value.Trim() == "true") _dockingTasks.Add(task.Id.Value);
            }
        }
        _plumes = new ThrusterPlumeGate(session.Data, session.State);
        _fractions = new double[_plumes.ComponentIds.Count];
        _knowledgeReadAt = frame is null ? 0 : frame.KnowledgeRevision - 1;
    }

    public string PrimaryVehicleId => _primaryVehicle;

    public string? TargetVehicleId => _track.TargetId;

    /// <summary>The target's sensor that shows its attitude-thruster activity (mission 1: <c>lodestar/status_panel</c>).</summary>
    public string? StatusPanelComponentId { get; }

    public OrbitalBackdropView Backdrop { get; private set; }

    public int KnowledgeRevision { get; private set; }

    /// <summary>Whoever the public crew location puts outside (<c>eva</c>), and whether the package event has fired.</summary>
    public EvaCrewView? EvaCrew { get; private set; }

    public double MetSec => _session.Now.Seconds;

    /// <summary>
    /// Caches the frame's view from the frame reader; does nothing while its stamp has not moved (frozen, nothing happened).
    /// Call after the simulation advanced, the events were observed and <see cref="FlightTrack.Update"/> ran. Never allocates
    /// once warm, except when knowledge changed (the known components are re-read) and the reader's attitude fallback.
    /// </summary>
    /// <exception cref="InvalidOperationException">Built without a <see cref="SessionFrameReader"/>.</exception>
    public void Refresh()
    {
        var frame = _frame ?? throw new InvalidOperationException("This view source was built without a frame reader; call Refresh(snapshot).");
        var stamp = frame.Stamp;
        if (_refreshed && stamp == _refreshedAt) return;
        _refreshed = true;
        _refreshedAt = stamp;

        if (frame.KnowledgeRevision != _knowledgeReadAt)
        {
            _knowledgeReadAt = frame.KnowledgeRevision;
            RefreshKnowledge(_session.Knowledge.KnownComponents());
            _acsActivity = AcsActivity();
        }
        RefreshPlumesAndChutes();

        var state = _session.State;
        foreach (var (vehicleId, paths) in _sectionPaths)
        {
            var sections = _sections[vehicleId];
            for (var i = 0; i < paths.Length; i++)
            {
                sections[paths[i].SectionId] = state.TryGet(paths[i].AttachedPath, out var attached) && attached.Kind == StateValueKind.Bool &&
                    !attached.AsBool();
            }
        }

        var primaryPosition = _track.Crewed.PositionM;
        for (var i = 0; i < _vehicleIds.Length; i++)
        {
            if (frame.TryGetAttitude(_vehicleIds[i], out var attitude)) SetView(_vehicleIds[i], attitude, primaryPosition);
        }
        DockingApproach01 = FrameDockingProgress(frame, stamp);
        FinishRefresh();
    }

    /// <summary>Caches the frame's view from a snapshot the host already built. Call after <see cref="FlightTrack.Update"/> ran.</summary>
    public void Refresh(SessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        RefreshKnowledge(snapshot.Components);
        _acsActivity = AcsActivity();
        RefreshPlumesAndChutes();

        var vehicles = snapshot.Vehicles;
        for (var v = 0; v < vehicles.Count; v++)
        {
            var vehicle = vehicles[v];
            if (!_sections.TryGetValue(vehicle.Id, out var sections)) _sections[vehicle.Id] = sections = new Dictionary<string, bool>(StringComparer.Ordinal);
            var views = vehicle.Sections;
            for (var s = 0; s < views.Count; s++) sections[views[s].Id] = !views[s].Attached;
        }

        var primaryPosition = _track.Crewed.PositionM;
        var attitudes = snapshot.Attitude;
        for (var i = 0; i < attitudes.Count; i++)
        {
            var a = attitudes[i];
            SetView(a.VehicleId, new VehicleAttitude(a.Orientation, a.RollDps, a.PitchDps, a.YawDps, a.TotalDps, a.DockedWith), primaryPosition);
        }
        DockingApproach01 = snapshot.Task is { } slot && _dockingTasks.Contains(slot.Task.Value) ? Math.Clamp(slot.Progress, 0, 1) : null;
        FinishRefresh();
    }

    public bool TryGetVehicle(string vehicleId, out SpacecraftVehicleView vehicle)
    {
        if (vehicleId is not null && _vehicles.Contains(vehicleId) && _views.TryGetValue(vehicleId, out vehicle)) return true;
        vehicle = default;
        return false;
    }

    public double GetFiringFraction(string vehicleId, string thrusterComponentId) =>
        _plumes.IndexOf(thrusterComponentId) is var index and >= 0 ? _fractions[index] : 0;

    public PlumeKind GetPlumeKind(string componentId) => _plumes.KindOf(componentId);

    public IReadOnlyList<SeparableSection> GetSeparableSections(string vehicleId) =>
        vehicleId is not null && _separable.TryGetValue(vehicleId, out var sections) ? sections : [];

    public bool IsSectionJettisoned(string vehicleId, string sectionId) =>
        vehicleId is not null && sectionId is not null && _sections.TryGetValue(vehicleId, out var sections) &&
        sections.TryGetValue(sectionId, out var jettisoned) && jettisoned;

    public ChuteDeployState GetChuteState(string vehicleId, string chuteComponentId) =>
        chuteComponentId is not null && _chutes.TryGetValue(chuteComponentId, out var chute) ? chute : ChuteDeployState.Stowed;

    public bool IsRevealable(string componentId) => componentId is not null && _revealable.Contains(componentId);

    public ComponentKnowledgeView GetComponentKnowledge(string componentId) =>
        componentId is not null && _knowledge.TryGetValue(componentId, out var view) ? view : ComponentKnowledgeView.Unknown;

    /// <summary>
    /// The Lodestar status panel as the player knows it: ACS activity from the crew's latest glance at the panel or, without
    /// one, the downlinked activity word; the latch from the docking the crew flew; ACS power from the ground-commanded
    /// relay read-back. Engine arming has no fact or telemetry, so it stays unknown.
    /// </summary>
    public LodestarPanelView GetLodestarPanel() => _lodestarPanel;

    /// <summary>The recovery card's elapsed share of the wait after splashdown, so the scene and the card agree; null before.</summary>
    public double? RecoveryProgress01 { get; private set; }

    /// <summary>
    /// The crew's docking task as the console's task slot shows it: its progress while the slot holds a task that docks the
    /// vehicles, null otherwise. Read from the public task slot (<c>crew.task.id</c> and the slot's start, end and progress).
    /// </summary>
    public double? DockingApproach01 { get; private set; }

    /// <summary>Follows the recovery card the runtime rebuilt (10 Hz).</summary>
    public void SetRecovery(SplashdownCardModel card)
    {
        ArgumentNullException.ThrowIfNull(card);
        RecoveryProgress01 = card.Visible ? card.RecoveryProgress01 : null;
    }

    private void RefreshPlumesAndChutes()
    {
        var state = _session.State;
        for (var i = 0; i < _fractions.Length; i++) _fractions[i] = _plumes.VisibleFraction(i);
        foreach (var (id, path) in _chutePaths)
        {
            _chutes[id] = state.TryGet(path, out var word) && word.Kind == StateValueKind.Text ? AdapterMaps.Chute(word.AsText()) : ChuteDeployState.Stowed;
        }
    }

    /// <summary>
    /// The task slot changes only with a simulation event, so the slot is re-read (one shared snapshot) only when the stamp's
    /// event count moved while a docking task runs; between events its work is linear up to the slot's end time.
    /// </summary>
    private double? FrameDockingProgress(SessionFrameReader frame, SessionFrameStamp stamp)
    {
        if (_dockingTasks.Count == 0 || !_session.State.TryGet(ActiveTaskPath, out var task) || task.Kind != StateValueKind.Text ||
            !_dockingTasks.Contains(task.AsText()))
        {
            _dockingSlot = null;
            return null;
        }
        if (_dockingSlot is null || stamp.Events != _dockingSlotEvents)
        {
            _dockingSlotEvents = stamp.Events;
            _dockingSlotReadAt = stamp.Now;
            _dockingSlot = frame.Snapshot().Task;
        }
        if (_dockingSlot is not { } slot || !_dockingTasks.Contains(slot.Task.Value)) return null;
        var remainingMs = slot.EndsAt.Milliseconds - _dockingSlotReadAt.Milliseconds;
        if (remainingMs <= 0) return 1;
        var done = (stamp.Now.Milliseconds - _dockingSlotReadAt.Milliseconds) / (double)remainingMs;
        return Math.Clamp(slot.Progress + (1 - slot.Progress) * done, 0, 1);
    }

    private void SetView(string vehicleId, in VehicleAttitude attitude, Vec2d primaryPosition)
    {
        var relative = Vec3d.Zero;
        if (vehicleId != _primaryVehicle && attitude.DockedWith is null && _track.Target is { } other && vehicleId == _track.TargetId)
        {
            var offset = other.PositionM - primaryPosition;
            relative = new Vec3d(offset.X, offset.Y, 0);
        }
        var primary = vehicleId == _primaryVehicle;
        _views[vehicleId] = new SpacecraftVehicleView(
            attitude.Orientation,
            new Vec3d(attitude.RollDps * DegToRad, attitude.PitchDps * DegToRad, attitude.YawDps * DegToRad),
            relative,
            attitude.DockedWith,
            primary ? Math.Clamp(_track.HeatingProxy / _options.PeakHeatingProxy, 0, 1) : 0,
            primary && _track.Phase == FlightPhase.Landed);
    }

    private void FinishRefresh()
    {
        EvaCrew = ReadEvaCrew();
        _lodestarPanel = LodestarPanel();
        var radius = _track.Crewed.PositionM;
        var nadir = radius.Length > 0 ? -radius.Normalized() : Vec2d.Zero;
        Backdrop = new OrbitalBackdropView(new Vec3d(nadir.X, nadir.Y, 0), _options.SunInertial, _track.AltitudeKm);
    }

    private EvaCrewView? ReadEvaCrew()
    {
        var state = _session.State;
        for (var i = 0; i < _crewLocationPaths.Length; i++)
        {
            if (!state.TryGet(_crewLocationPaths[i], out var location) || location.Kind != StateValueKind.Text ||
                location.AsText() != "eva")
            {
                continue;
            }
            var retrieved = _packagePath is not null && state.TryGet(_packagePath, out var fired) && fired.Kind == StateValueKind.Bool &&
                fired.AsBool();
            return new EvaCrewView(_crewIds[i], retrieved);
        }
        return null;
    }

    private LodestarPanelView LodestarPanel()
    {
        if (_track.TargetId is not { } target || !_vehicles.Contains(target) || _acsPowerPath is null) return LodestarPanelView.Unknown;
        bool? power = _session.State.TryGet(_acsPowerPath, out var closed) && closed.Kind == StateValueKind.Bool ? closed.AsBool() : null;
        return new LodestarPanelView(_acsActivity, _track.Docked, null, power);
    }

    /// <summary>The panel's activity word as known (allocates: <c>KnowledgeBase.Describe</c>); only re-read when knowledge changed.</summary>
    private double? AcsActivity()
    {
        if (_acsActivityFact is not { } acsActivity || !_session.Data.Facts.TryResolve(acsActivity, out _)) return null;
        var known = _session.Knowledge.Describe(acsActivity);
        return ActivityOf(known.Latest?.Qualitative) ?? ActivityOf(known.GroundBand?.Qualitative);
    }

    /// <summary>Panel or downlink word → the panel's activity level (the status lights blink from 0.2).</summary>
    private static double? ActivityOf(string? word) => word switch
    {
        "idle" or "IDLE" => 0,
        "low" or "LOW ACTIVITY" => 0.1,
        "high" or "HIGH ACTIVITY" => 0.5,
        _ => null,
    };

    private void RefreshKnowledge(IReadOnlyList<SimComponentKnowledge> components)
    {
        var changed = false;
        for (var i = 0; i < components.Count; i++)
        {
            var known = components[i];
            var view = new ComponentKnowledgeView(
                AdapterMaps.Look(known.State),
                known.State == SimComponentState.ConfirmedDamaged ? known.Variant : null,
                known.IsStale);
            var id = known.Component.Value;
            if (_knowledge.TryGetValue(id, out var previous) && previous == view) continue;
            _knowledge[id] = view;
            changed = true;
        }
        if (changed) KnowledgeRevision++;
    }

    private readonly record struct SectionPath(string SectionId, string AttachedPath);
}
