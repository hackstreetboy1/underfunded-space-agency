using Godot;
using Usa.Game.Input;
using Usa.Game.Presentation.Rendering;
using Usa.Game.Presentation.Spacecraft.Vfx;

namespace Usa.Game.Presentation.Spacecraft;

/// <summary>
/// Root of <c>res://scenes/SpacecraftView.tscn</c> (SCRIPTING 12.3): vehicles, Earth layer, camera rig, VFX and the
/// component presenters. Meant to sit under a <c>SubViewport</c> with <c>own_world_3d</c> (the External tab wraps it);
/// it only uses its own viewport. Call <see cref="Initialize"/> with the session adapter; until then it shows the
/// models grey and still. Each frame it reads the source and pushes poses, plumes, staging, the target's status lights
/// and knowledge (with the reveal outline flash) into the scene; it never changes the camera except through the
/// focus/reset calls. Vehicle roles (which rig is centred, which carries the status panel) come from the source, never
/// from mission ids. <see cref="SetFrozen"/> holds plumes and lights while the app is frozen; <see cref="Detach"/> lets
/// go of the source when the mission stops.
/// </summary>
public sealed partial class SpacecraftView : Node3D
{
    [Signal]
    public delegate void ComponentPickedEventHandler(string componentId);

    [Signal]
    public delegate void PickClearedEventHandler();

    [Signal]
    public delegate void ComponentRevealedEventHandler(string componentId);

    /// <summary>The entry/landing HUD (UI 4.2) should overlay the view: heating, or chutes out before splashdown.</summary>
    [Signal]
    public delegate void EntryHudVisibilityChangedEventHandler(bool visible);

    [Signal]
    public delegate void SplashdownChangedEventHandler(string vehicleId, bool splashedDown);

    [Signal]
    public delegate void SectionJettisonedEventHandler(string vehicleId, string sectionId);

    /// <summary>The view switched between orbit (Earth backdrop) and the sea after the crewed vehicle's splashdown.</summary>
    [Signal]
    public delegate void SeaViewChangedEventHandler(bool atSea);

    private readonly List<VehicleRig> _rigs = [];
    private readonly Dictionary<string, VehicleRig> _rigsById = new(StringComparer.Ordinal);
    private readonly Dictionary<VehicleRig, string?> _dockedWith = [];
    private readonly Dictionary<VehicleRig, UndockDeparture> _departures = [];
    private OrbitCameraRig _cameraRig = null!;
    private EarthBackdrop _backdrop = null!;
    private RevealController _reveal = null!;
    private KnowledgeBinding _knowledge = null!;
    private ComponentPicker _picker = null!;
    private Node3D _vehicles = null!;
    private RevealOutlineFlash _outlineFlash = null!;
    private LodestarStatusLights _statusLights = null!;
    private WorldEnvironment _worldEnvironment = null!;
    private Godot.Environment? _orbitEnvironment;
    private RecoveryDressing? _recovery;
    private EvaRig? _eva;
    private SplashVfx? _splash;
    private bool _glowCalm;
    private VehicleRig? _primaryRig;
    private VehicleRig? _targetRig;
    private VehicleRig? _dockingTargetRig;
    private Node3D? _pairFocus;
    private DockingApproach? _approach;
    private double? _approach01;
    private Transform3D _approachTargetPose;
    private Transform3D _approachChaserBase;
    private CameraShot? _cameraBeforeApproach;
    private int _cameraInputAtSideView;
    private double _cameraRestoreInSec = -1;
    private Vector3 _latchAxis;
    private double _latchSec = -1;
    private ISpacecraftViewSource? _source;
    private string _primaryVehicleId = string.Empty;
    private double _lastMetSec;
    private bool _primed;
    private bool _frozen;
    private bool _entryHudVisible;
    private bool _reducedMotion;
    private bool _atSea;
    private bool _splashedView;
    private bool _floatsItself;
    private bool _pendingSideView;
    private Vector3 _primaryCenterLocal;
    private Vector3 _targetCenterLocal;
    private float _pairRadiiM;

    /// <summary>The vehicle the scene is centred on (the source's crewed vehicle); empty until <see cref="Initialize"/>.</summary>
    public string PrimaryVehicleId => _primaryVehicleId;

    [Export] public double EntryHudHeatingThreshold { get; set; } = 0.02;

    /// <summary>
    /// Under a chute below this altitude the view shows the sea rising to meet the capsule: from the drogue's deploy (its highest
    /// working altitude is 21 km) all the way down, so the fall is one continuous shot.
    /// </summary>
    [Export] public double SeaViewBelowKm { get; set; } = 25.0;

    /// <summary>
    /// How far below the capsule the sea is drawn at <see cref="SeaViewBelowKm"/>, metres (the scene is toy-sized): just short of the
    /// sea haze (it begins at 100 m and ends at about 200 m), so high up the water reads as a hazy sea far below and is never lost in it.
    /// </summary>
    [Export] public float DescentDropMaxM { get; set; } = 90f;

    /// <summary>
    /// Knee of the logarithmic altitude-to-drop map, metres of altitude: above it the drawn drop grows with the log of the
    /// altitude (the kilometres up high pass steadily), below it almost linearly, so the water closes in smoothly to touchdown.
    /// </summary>
    [Export] public float DescentDropKneeM { get; set; } = 400f;

    /// <summary>Focus distance as a multiple of the focused part's bounding radius.</summary>
    [Export] public float FocusFraming { get; set; } = 3.5f;

    /// <summary>Draws June outside during the EVA (<c>EvaRig</c>); turn off to fall back to the crew card and event log only.</summary>
    [Export] public bool ShowEva { get; set; } = true;

    /// <summary>Focus distance as a multiple of a whole vehicle's bounding radius.</summary>
    [Export] public float VehicleFocusFraming { get; set; } = 2.2f;

    /// <summary>Camera distance when "Focus vehicle" frames the spacewalker.</summary>
    [Export] public float EvaFocusDistanceM { get; set; } = 2.6f;

    /// <summary>At sea the camera looks down at least this steeply, so it never dips under the water.</summary>
    [Export] public float SeaMaxPitchDeg { get; set; } = -4f;

    /// <summary>Share of the view's width the docking pair (both hulls and the gap at the standoff) fills.</summary>
    [Export] public float SideViewFill { get; set; } = 0.65f;

    /// <summary>How far above the docking axis the side view looks from (0 = level, 1 = 45°).</summary>
    [Export] public float SideViewLift { get; set; } = 0.3f;

    /// <summary>Easing rate (1/s) of the camera into the side view and back.</summary>
    [Export] public float SideViewGlideRate { get; set; } = 2.2f;

    /// <summary>Real seconds the side view holds on the docked stack before the camera eases back to where it was.</summary>
    [Export] public double SideViewHoldAfterDockSec { get; set; } = 1.6;

    /// <summary>Camera jolt when the latches catch at the end of a docking approach (none under reduced motion).</summary>
    [Export] public float LatchCameraBumpM { get; set; } = 0.12f;

    /// <summary>The docking approach being flown (diagnostics), or null.</summary>
    public DockingApproach? Approach => _approach;

    public string? LastRevealedComponentId => _knowledge.LastRevealedComponentId;

    public bool EntryHudVisible => _entryHudVisible;

    public IReadOnlyDictionary<string, ComponentPresenter> Presenters => _knowledge.Presenters;

    /// <summary>The target vehicle's status panel lights; read their states for diagnostics.</summary>
    public LodestarStatusLights StatusLights => _statusLights;

    /// <summary>The Earth layer, Sun and fills (the runtime binding sets the Earth's angle at MET zero on it).</summary>
    public EarthBackdrop Backdrop => _backdrop;

    /// <summary>True while the crewed vehicle floats after splashdown and the view shows the sea instead of orbit.</summary>
    public bool IsAtSea => _atSea;

    /// <summary>
    /// Accessibility setting (UI 2.5), applied to the reveal transition, the reveal outline flash and the status-light
    /// blinking. The host sets it from <c>GameSettings.Display.ReducedMotion</c> and again on every settings change; it may
    /// change at any time, before or after <see cref="Initialize"/>.
    /// </summary>
    public bool ReducedMotion
    {
        get => _reducedMotion;
        set
        {
            _reducedMotion = value;
            if (_reveal is not null) _reveal.ReducedMotion = value;
            if (_outlineFlash is not null) _outlineFlash.ReducedMotion = value;
            if (_statusLights is not null) _statusLights.ReducedMotion = value;
            if (_eva is not null) _eva.ReducedMotion = value;
        }
    }

    public override void _Ready()
    {
        _cameraRig = GetNode<OrbitCameraRig>("%CameraRig");
        _backdrop = GetNode<EarthBackdrop>("%Backdrop");
        _reveal = GetNode<RevealController>("%RevealController");
        _knowledge = GetNode<KnowledgeBinding>("%KnowledgeBinding");
        _picker = GetNode<ComponentPicker>("%ComponentPicker");
        _vehicles = GetNode<Node3D>("%Vehicles");
        _outlineFlash = GetNode<RevealOutlineFlash>("%RevealOutlineFlash");
        _statusLights = GetNode<LodestarStatusLights>("%LodestarStatusLights");
        _worldEnvironment = GetNode<WorldEnvironment>("%WorldEnvironment");
        _orbitEnvironment = _worldEnvironment.Environment;
        _recovery = GetNodeOrNull<RecoveryDressing>("%Recovery");
        _eva = ShowEva ? GetNodeOrNull<EvaRig>("%EvaRig") : null;
        _splash = GetNodeOrNull<SplashVfx>("%SplashVfx");
        _pairFocus = GetNodeOrNull<Node3D>("%PairFocus");
        ReducedMotion = _reducedMotion || _reveal.ReducedMotion;

        foreach (var child in _vehicles.GetChildren())
        {
            if (child is not VehicleRig rig) continue;
            _rigs.Add(rig);
            _rigsById[rig.VehicleId] = rig;
            _dockedWith[rig] = null;
            if (rig.Model is not null) _knowledge.Bind(ComponentPresenter.BindAll(rig.Model, _reveal));
            rig.BindThrusters();
            var vehicleId = rig.VehicleId;
            rig.SectionJettisoned += section => EmitSignal(SignalName.SectionJettisoned, vehicleId, section);
            rig.SplashdownChanged += splashed => EmitSignal(SignalName.SplashdownChanged, vehicleId, splashed);
        }

        _picker.Bind(_cameraRig.Camera ?? throw new InvalidOperationException("CameraRig has no Camera."), _vehicles);
        _picker.ComponentPicked += (id, _) => EmitSignal(SignalName.ComponentPicked, id);
        _picker.PickCleared += () => EmitSignal(SignalName.PickCleared);
        _knowledge.ComponentRevealed += OnComponentRevealed;
    }

    /// <summary>
    /// Attaches the session adapter and resets everything a previous run left (staging, chutes, splashdown, plumes, lights,
    /// knowledge looks, flashes). The first frame applies state as it is (no reveals, no separations).
    /// </summary>
    public void Initialize(ISpacecraftViewSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _primaryVehicleId = source.PrimaryVehicleId;
        _primaryRig = _rigsById.GetValueOrDefault(_primaryVehicleId);
        _primed = false;
        SetAtSea(false);
        _lastMetSec = source.MetSec;
        foreach (var rig in _rigs)
        {
            rig.Configure(source, rig.VehicleId == _primaryVehicleId);
            _dockedWith[rig] = null;
        }
        _departures.Clear();
        // Primary first, so docked partners can mate to its final pose in the same frame.
        _rigs.Sort((a, b) => (b.VehicleId == _primaryVehicleId).CompareTo(a.VehicleId == _primaryVehicleId));

        _targetRig = null;
        EndApproach();
        _latchSec = -1;
        _cameraBeforeApproach = null;
        _cameraRestoreInSec = -1;
        _dockingTargetRig = source.TargetVehicleId is { } dockingTarget ? _rigsById.GetValueOrDefault(dockingTarget) : null;
        _statusLights.ResetLights();
        if (source.TargetVehicleId is { } target && source.StatusPanelComponentId is { } panel &&
            _rigsById.TryGetValue(target, out var targetRig) && targetRig.Model is not null && _statusLights.Bind(targetRig.Model, panel))
        {
            _targetRig = targetRig;
        }
        _knowledge.Reset();
        _outlineFlash.StopAll();
        _eva?.Apply(null, 0);
    }

    /// <summary>Lets go of the source (mission stopped): plumes out, lights dark, flashes stopped; the scene stops updating.</summary>
    public void Detach()
    {
        _source = null;
        _targetRig = null;
        EndApproach();
        _latchSec = -1;
        foreach (var rig in _rigs) rig.Thrusters?.Extinguish();
        _statusLights.ResetLights();
        _outlineFlash.StopAll();
        _eva?.Apply(null, 0);
    }

    /// <summary>
    /// The app-wide freeze (GDD 3.2): while frozen the plumes and status lights hold their last look instead of animating
    /// on, so a held pulse never turns into the stuck look behind the freeze overlay. Poses and knowledge keep reading the
    /// source, which does not change while the simulation is frozen.
    /// </summary>
    public void SetFrozen(bool frozen)
    {
        _frozen = frozen;
        _eva?.SetFrozen(frozen);
        _splash?.SetFrozen(frozen);
    }

    public override void _Process(double delta)
    {
        if (_source is not { } source) return;

        var backdrop = source.Backdrop;
        var frame = SpacecraftAxes.DisplayFrame(backdrop.NadirInertial);
        var metSec = source.MetSec;
        var metDeltaSec = Math.Max(0, metSec - _lastMetSec);
        _lastMetSec = metSec;
        var animate = _primed;
        var hasPrimary = source.TryGetVehicle(_primaryVehicleId, out var primary);
        var splashed = _recovery is not null && hasPrimary && primary.SplashedDown;
        // Under the chutes the view shows the sea, far below in the haze, rising as the capsule comes down; the orbit backdrop
        // can't draw a few kilometres of altitude, and a later cut to the water (at the main, or at splashdown) reads as a teleport.
        var descending = _recovery is not null && hasPrimary && !splashed && _primaryRig is { } descender && ChutesOut(descender) &&
                         backdrop.AltitudeKm > 0 && backdrop.AltitudeKm < SeaViewBelowKm;
        var atSea = splashed || descending;
        if (atSea != _atSea) SetAtSea(atSea);
        if (splashed != _splashedView) SetSplashedView(splashed, animate);
        if (splashed)
        {
            _recovery!.SetProgress(source.RecoveryProgress01);
        }
        else if (descending)
        {
            _recovery!.SetDrop(DescentDrop(backdrop.AltitudeKm * 1000.0));
        }
        else
        {
            _backdrop.SetMissionTime(metSec);
            _backdrop.Apply(frame, backdrop);
        }

        var entryActive = false;
        var glowCalm = _reducedMotion || _frozen;
        var calmChanged = glowCalm != _glowCalm;
        _glowCalm = glowCalm;
        if (calmChanged)
        {
            foreach (var rig in _rigs) rig.SetGlowCalm(glowCalm);
        }
        UpdateApproach(source, atSea, animate);
        _primaryRig?.Entry.Observe(backdrop.NadirInertial, backdrop.AltitudeKm, _frozen ? 0 : delta);
        for (var i = 0; i < _rigs.Count; i++)
        {
            var rig = _rigs[i];
            if (!source.TryGetVehicle(rig.VehicleId, out var vehicle) || (atSea && rig != _primaryRig))
            {
                rig.Visible = false;
                continue;
            }
            rig.Visible = true;
            if (splashed && rig == _primaryRig) rig.ApplyPose(_floatsItself ? Transform3D.Identity : _recovery!.FloatPose(), delta);
            else ApplyPose(rig, vehicle, frame, delta, animate);
            if (!_frozen) rig.Thrusters?.Update(source, rig.VehicleId, delta, metDeltaSec);
            entryActive |= UpdateStaging(source, rig, vehicle, animate);
        }
        ApplyLatch(delta);
        UpdatePairFocus();
        RestoreCameraAfterApproach(delta);

        if (!_frozen && _targetRig is { Visible: true }) _statusLights.Update(source, delta);
        _eva?.Apply(atSea ? null : source.EvaCrew, delta);
        _knowledge.Refresh(source);
        _primed = true;

        if (entryActive != _entryHudVisible)
        {
            _entryHudVisible = entryActive;
            EmitSignal(SignalName.EntryHudVisibilityChanged, entryActive);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (InputMap.HasAction(InputBindings.FocusVehicle) && @event.IsActionPressed(InputBindings.FocusVehicle, allowEcho: false, exactMatch: true))
        {
            FocusVehicle(_primaryVehicleId);
            GetViewport().SetInputAsHandled();
        }
    }

    public void ResetCamera() => _cameraRig.ResetView(_rigsById.GetValueOrDefault(_primaryVehicleId));

    /// <summary>Turns the camera to an explicit angle around its current pivot (review captures, scripted shots).</summary>
    public void OrbitCamera(float yawDeg, float pitchDeg, float? distanceM = null) =>
        _cameraRig.SetAngles(yawDeg, pitchDeg, distanceM);

    public bool FocusVehicle(string vehicleId)
    {
        if (!_rigsById.TryGetValue(vehicleId, out var rig)) return false;
        // During the spacewalk "Focus vehicle" frames the spacewalker at the hatch rather than the whole stack.
        if (vehicleId == _primaryVehicleId && _eva?.FocusTarget is { } walker)
        {
            _cameraRig.Focus(walker, EvaFocusDistanceM);
            return true;
        }
        // While the crew docks, "Focus vehicle" frames the pair: the chaser and the target it closes on.
        if (vehicleId == _primaryVehicleId && _approach is not null && FrameSideView()) return true;
        var bounds = rig.Model is null ? null : MeshBounds(rig.Model);
        _cameraRig.Focus(rig, bounds is { } box ? box.Size.Length() * 0.5f * VehicleFocusFraming : null);
        return true;
    }

    /// <summary>Viewport pixels per UI unit when the view renders at screen density, so camera drags keep their feel.</summary>
    public void SetInputPixelScale(float pixelsPerUnit) => _cameraRig.InputPixelScale = pixelsPerUnit;

    public bool FocusComponent(string componentId)
    {
        if (!_knowledge.Presenters.TryGetValue(componentId, out var presenter)) return false;
        var component = presenter.GetParent<Node3D>();
        if (MeshBounds(component) is not { } bounds)
        {
            _cameraRig.Focus(component.GlobalPosition);
            return true;
        }
        _cameraRig.Focus(bounds.GetCenter(), Math.Max(bounds.Size.Length() * 0.5f, 0.2f) * FocusFraming);
        return true;
    }

    /// <summary>"Focus last revealed" (UI 4.4); false when nothing has been revealed since the source was attached.</summary>
    public bool FocusLastRevealed() => LastRevealedComponentId is { } id && FocusComponent(id);

    /// <summary>
    /// Screen position (this view's viewport) of a component's tag anchor, the top centre where the STALE tag and
    /// inspector callouts attach; false when unknown or behind the camera.
    /// </summary>
    public bool TryGetTagAnchor(string componentId, out Vector2 screenPosition)
    {
        screenPosition = Vector2.Zero;
        var camera = _cameraRig.Camera;
        if (camera is null || !_knowledge.Presenters.TryGetValue(componentId, out var presenter)) return false;
        if (!presenter.IsVisibleInTree() || camera.IsPositionBehind(presenter.GlobalPosition)) return false;
        screenPosition = camera.UnprojectPosition(presenter.GlobalPosition);
        return true;
    }

    // The low descent and splashdown swap orbit for the sea: Earth layer and its lights off, sea sky and Sun on, the camera kept
    // above the water. The other vehicles stay in orbit.
    private void SetAtSea(bool atSea)
    {
        if (atSea == _atSea || _recovery is null) return;
        _atSea = atSea;
        _recovery.SetActive(atSea);
        if (!atSea) SetSplashedView(false, animate: false);
        _backdrop.Visible = !atSea;
        _worldEnvironment.Environment = atSea && _recovery.SeaEnvironment is { } sea ? sea : _orbitEnvironment;
        if (atSea) _cameraRig.LimitPitch(_cameraRig.MinPitchDeg, SeaMaxPitchDeg);
        else _cameraRig.ResetPitchLimits();
        EmitSignal(SignalName.SeaViewChanged, atSea);
    }

    /// <summary>
    /// Drawn drop of the sea below the capsule for an altitude under the chutes: <c>max · ln(1 + h/knee) / ln(1 + top/knee)</c>,
    /// monotonic, continuous and 0 at touchdown, so no frame jumps the water from far below to just under the capsule.
    /// </summary>
    public float DescentDrop(double altitudeM)
    {
        var knee = Math.Max(1.0, DescentDropKneeM);
        var top = Math.Max(knee, SeaViewBelowKm * 1000.0);
        var h = Math.Clamp(altitudeM, 0.0, top);
        return (float)(DescentDropMaxM * Math.Log(1 + (h / knee)) / Math.Log(1 + (top / knee)));
    }

    // Splashdown itself: the sea reaches the capsule, the splash plays and the capsule floats (its own float action when the
    // model has one), easing over from hanging under the canopy.
    private void SetSplashedView(bool splashed, bool animate)
    {
        if (splashed == _splashedView) return;
        _splashedView = splashed;
        if (splashed)
        {
            _recovery?.SetDrop(0f);
            _splash?.Play(_reducedMotion);
            if (animate) _primaryRig?.BeginPoseBlend();
        }
        else
        {
            _splash?.Stop();
        }
        _floatsItself = _primaryRig?.SetFloating(splashed) ?? false;
    }

    private void OnComponentRevealed(string componentId)
    {
        if (_knowledge.Presenters.TryGetValue(componentId, out var presenter) && presenter.GetParent() is Node3D component)
        {
            _outlineFlash.Play(component);
        }
        EmitSignal(SignalName.ComponentRevealed, componentId);
    }

    private void ApplyPose(VehicleRig rig, in SpacecraftVehicleView vehicle, Basis frame, double delta, bool animate)
    {
        var partner = vehicle.DockedWithId is { } id && rig.VehicleId != _primaryVehicleId
            ? _rigsById.GetValueOrDefault(id)
            : null;
        var formerPartner = _dockedWith[rig] is { } formerId ? _rigsById.GetValueOrDefault(formerId) : null;
        var changed = _dockedWith[rig] != vehicle.DockedWithId;
        _dockedWith[rig] = vehicle.DockedWithId;
        if (_approach is { } approach && _approach01 is { } progress && (rig == _primaryRig || rig == _dockingTargetRig))
        {
            _departures.Remove(rig);
            if (rig == _primaryRig)
            {
                var chaserBase = new Transform3D(SpacecraftAxes.ToDisplay(frame, rig.SmoothedOrientation(vehicle, delta)), Vector3.Zero);
                var (chaserPose, targetPose) = approach.Resolve(chaserBase, MatedPose(chaserBase, rig, _dockingTargetRig!), progress);
                _approachTargetPose = targetPose;
                _approachChaserBase = chaserBase;
                rig.ApplyPose(chaserPose, delta);
            }
            else
            {
                rig.ApplyPose(_approachTargetPose, delta);
            }
            return;
        }
        if (partner is not null && partner.Visible)
        {
            _departures.Remove(rig);
            if (changed && animate) rig.BeginPoseBlend();
            rig.ApplyPose(MatedPose(partner, rig), delta);
            return;
        }

        var orientation = SpacecraftAxes.ToDisplay(frame, rig.SmoothedOrientation(vehicle, delta));
        var chutesOut = ChutesOut(rig);
        // Through entry the capsule flies heat shield first, whatever attitude the simulation (no aerodynamics) still holds;
        // the pose stays on under the chutes so the hang below swings out of it, not out of the free attitude.
        if (rig == _primaryRig)
        {
            orientation = rig.Entry.Resolve(orientation, Entering(rig, vehicle, chutesOut), _frozen ? 0 : delta, snap: !animate, _reducedMotion);
        }
        // Under a chute the capsule hangs nose-up below the canopy, whatever attitude it carried out of orbit.
        orientation = rig.Hang.Resolve(orientation, chutesOut && !vehicle.SplashedDown, _frozen ? 0 : delta, snap: !animate, _reducedMotion);
        var position = rig.VehicleId == _primaryVehicleId
            ? Vector3.Zero
            : SpacecraftAxes.ToDisplayOffset(frame, vehicle.RelativePositionM);
        var target = new Transform3D(orientation, position);
        if (changed && animate && vehicle.DockedWithId is null && formerPartner is { Visible: true } &&
            rig.VehicleId != _primaryVehicleId)
        {
            // Separating: back out along the docking axis instead of blending through the partner's hull.
            var anchorPort = formerPartner.Transform * formerPartner.DockingPortLocal;
            _departures[rig] = new UndockDeparture(frame, MatedPose(formerPartner, rig), anchorPort.Basis.X, target);
        }
        else if (changed && animate)
        {
            rig.BeginPoseBlend();
        }

        if (_departures.TryGetValue(rig, out var departure))
        {
            target = departure.Resolve(frame, target);
            if (departure.Finished) _departures.Remove(rig);
        }
        rig.ApplyPose(target, delta);
    }

    /// <summary>
    /// Entry runs from the drop of the vehicle's last separable section (the retro pack) or the first entry heating until
    /// splashdown; once begun it holds while the chutes are out.
    /// </summary>
    private bool Entering(VehicleRig rig, in SpacecraftVehicleView vehicle, bool chutesOut)
    {
        if (vehicle.SplashedDown || _source is not { } source) return false;
        if (vehicle.EntryHeating01 >= EntryHudHeatingThreshold || (chutesOut && rig.Entry.IsActive)) return true;
        var sections = rig.SectionIds;
        if (sections.Count == 0) return false;
        for (var i = 0; i < sections.Count; i++)
        {
            if (!source.IsSectionJettisoned(rig.VehicleId, sections[i])) return false;
        }
        return true;
    }

    private bool ChutesOut(VehicleRig rig)
    {
        if (_source is not { } source) return false;
        var chutes = rig.ChuteIds;
        for (var i = 0; i < chutes.Count; i++)
        {
            if (source.GetChuteState(rig.VehicleId, chutes[i]) is ChuteDeployState.Deploying or ChuteDeployState.Reefed or ChuteDeployState.Open)
                return true;
        }
        return false;
    }

    // Docking ports face out of their vehicles (+X), so mating turns the second one half a turn about its up axis:
    // the same nose-to-nose relation as Attitude's reversed StackMount.
    private static Transform3D MatedPose(VehicleRig anchor, VehicleRig docking) => MatedPose(anchor.Transform, anchor, docking);

    private static Transform3D MatedPose(Transform3D anchorPose, VehicleRig anchor, VehicleRig docking)
    {
        var halfTurn = new Transform3D(new Basis(Vector3.Up, Mathf.Pi), Vector3.Zero);
        return anchorPose * anchor.DockingPortLocal * halfTurn * docking.DockingPortLocal.AffineInverse();
    }

    // The approach runs while the source shows a docking in progress and the pair is not docked yet. It starts by easing
    // both vehicles onto the approach line during the line-up (picked up later: with the rigs' own pose blend) and frames
    // the pair when the camera was on the crewed vehicle. Ending at contact starts the latch; ending early (the task was
    // stopped) blends back to the simulated poses.
    private void UpdateApproach(ISpacecraftViewSource source, bool atSea, bool animate)
    {
        var progress = atSea ? null : source.DockingApproach01;
        var active = progress is not null && _primaryRig is not null && _dockingTargetRig is { } target &&
            source.TryGetVehicle(_primaryVehicleId, out _) && source.TryGetVehicle(target.VehicleId, out var targetView) &&
            targetView.DockedWithId is null;
        if (!active)
        {
            if (_approach is not { } ending) return;
            var contact = ending.Progress >= 0.5 && _dockingTargetRig is { } partner &&
                source.TryGetVehicle(partner.VehicleId, out var partnerView) && partnerView.DockedWithId == _primaryVehicleId;
            EndApproach();
            if (_cameraBeforeApproach is not null) _cameraRestoreInSec = contact ? SideViewHoldAfterDockSec : 0;
            if (contact && animate && _primaryRig is { } chaser)
            {
                _latchAxis = (chaser.Basis * chaser.DockingPortLocal.Basis.X).Normalized();
                _latchSec = 0;
                if (!_reducedMotion) _cameraRig.Bump(LatchCameraBumpM);
            }
            else if (animate)
            {
                _primaryRig?.BeginPoseBlend();
                _dockingTargetRig?.BeginPoseBlend();
            }
            return;
        }

        var value = progress!.Value;
        _approach01 = value;
        if (_approach is null)
        {
            var blendIn = animate && value < DockingApproachProfile.AlignEnd;
            _approach = new DockingApproach(_primaryRig!, _dockingTargetRig!, value, blendIn);
            if (animate && !blendIn)
            {
                _primaryRig!.BeginPoseBlend();
                _dockingTargetRig!.BeginPoseBlend();
            }
            var tracked = _cameraRig.Tracked;
            if (tracked is null || tracked == _primaryRig || (tracked == _pairFocus && _cameraRestoreInSec >= 0))
            {
                _pendingSideView = true;
                // A docking re-flown before the camera went back keeps the view from before the first one.
                if (_cameraRestoreInSec < 0) _cameraBeforeApproach = _cameraRig.Capture();
                _cameraRestoreInSec = -1;
            }
            MeasurePair();
        }
        if (!_frozen) _approach.Fire(value);
    }

    private void EndApproach()
    {
        _approach?.Release();
        _approach = null;
        _approach01 = null;
        _pendingSideView = false;
    }

    // The latches catch: the chaser rides in a few centimetres along the docking axis and settles (real time, held while
    // frozen; reduced motion keeps it and drops only the camera jolt).
    private void ApplyLatch(double delta)
    {
        if (_latchSec < 0 || _primaryRig is not { } chaser) return;
        if (!_frozen) _latchSec += delta;
        if (_latchSec >= DockingApproach.LatchSec)
        {
            _latchSec = -1;
            return;
        }
        chaser.Position += _latchAxis * DockingApproach.LatchOffsetM(_latchSec);
    }

    // The pair is framed on its hulls, not the rig origins (Lodestar's sits on its docking cone at one end of the body).
    private void MeasurePair()
    {
        _primaryCenterLocal = _targetCenterLocal = Vector3.Zero;
        _pairRadiiM = 0f;
        if (_primaryRig is { Model: { } chaser } primary && MeshBounds(chaser) is { } a)
        {
            _primaryCenterLocal = primary.GlobalTransform.AffineInverse() * a.GetCenter();
            _pairRadiiM += a.Size.Length() * 0.5f;
        }
        if (_dockingTargetRig is { Model: { } passive } target && MeshBounds(passive) is { } b)
        {
            _targetCenterLocal = target.GlobalTransform.AffineInverse() * b.GetCenter();
            _pairRadiiM += b.Size.Length() * 0.5f;
        }
    }

    // During the approach the camera pivots on the middle of the whole approach path (target hull at its mated pose, chaser
    // hull at the standoff), so the target holds still on screen and the chaser visibly flies across to it.
    private void UpdatePairFocus()
    {
        if (_pairFocus is null || _primaryRig is null) return;
        if (_approach is { } approach && _dockingTargetRig is { } target)
        {
            var (chaserCentre, targetCentre) = ApproachEnds(approach, target);
            _pairFocus.Position = (chaserCentre + targetCentre) * 0.5f;
        }
        if (!_pendingSideView) return;
        _pendingSideView = false;
        FrameSideView();
    }

    private (Vector3 Chaser, Vector3 Target) ApproachEnds(DockingApproach approach, VehicleRig target)
    {
        var axis = approach.AxisIn(_approachChaserBase);
        var standoff = new Transform3D(_approachChaserBase.Basis, _approachChaserBase.Origin - axis * DockingApproach.StandoffM);
        return (standoff * _primaryCenterLocal, MatedPose(_approachChaserBase, _primaryRig!, target) * _targetCenterLocal);
    }

    /// <summary>
    /// Eases the camera to the side of the docking axis, a little above it, close enough that both hulls and the gap at the
    /// standoff fill <see cref="SideViewFill"/> of the width: the approach then runs across the screen. When the axis is
    /// near vertical on screen (no roll-free view can turn it), the pair is fitted to the height instead.
    /// </summary>
    private bool FrameSideView()
    {
        if (_approach is not { } approach || _pairFocus is null || _primaryRig is null || _dockingTargetRig is not { } target ||
            _cameraRig.Camera is not { } camera)
        {
            return false;
        }
        var axis = approach.AxisIn(_approachChaserBase);
        var toCamera = camera.GlobalPosition - _cameraRig.GlobalPosition;
        var side = toCamera - axis * toCamera.Dot(axis);
        if (side.LengthSquared() < 1e-4f) side = axis.Cross(Vector3.Up);
        if (side.LengthSquared() < 1e-4f) side = axis.Cross(Vector3.Right);
        var up = Vector3.Up - axis * axis.Dot(Vector3.Up);
        var direction = (side.Normalized() + up * SideViewLift).Normalized();
        var yawDeg = Mathf.RadToDeg(Mathf.Atan2(direction.X, direction.Z));
        var pitchDeg = -Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(direction.Y, -1f, 1f)));

        var (chaserCentre, targetCentre) = ApproachEnds(approach, target);
        var span = chaserCentre.DistanceTo(targetCentre) + _pairRadiiM;
        var size = camera.GetViewport().GetVisibleRect().Size;
        var aspect = size.Y > 0 ? size.X / size.Y : 16f / 9f;
        var tanHalfHeight = Mathf.Tan(Mathf.DegToRad(camera.Fov * 0.5f));
        var acrossScreen = Mathf.Abs(axis.Dot(Vector3.Up)) < 0.7f;
        var distance = acrossScreen
            ? span / SideViewFill * 0.5f / (tanHalfHeight * aspect)
            : span / 0.85f * 0.5f / tanHalfHeight;
        _cameraInputAtSideView = _cameraRig.InputRevision;
        _cameraRig.Glide(new CameraShot(_pairFocus, _pairFocus.Position, yawDeg, pitchDeg, distance), SideViewGlideRate);
        return true;
    }

    // After the docking (or a stopped approach) the side view holds a moment on the stack, then eases back to the view the
    // player had, unless the player moved the camera meanwhile.
    private void RestoreCameraAfterApproach(double delta)
    {
        if (_cameraRestoreInSec < 0 || _cameraBeforeApproach is not { } before) return;
        if (!_frozen) _cameraRestoreInSec -= delta;
        if (_cameraRestoreInSec > 0) return;
        _cameraRestoreInSec = -1;
        _cameraBeforeApproach = null;
        if (_cameraRig.InputRevision == _cameraInputAtSideView) _cameraRig.Glide(before, SideViewGlideRate);
    }

    private bool UpdateStaging(ISpacecraftViewSource source, VehicleRig rig, in SpacecraftVehicleView vehicle, bool animate)
    {
        var sections = rig.SectionIds;
        for (var i = 0; i < sections.Count; i++)
        {
            if (source.IsSectionJettisoned(rig.VehicleId, sections[i])) rig.SetSectionJettisoned(sections[i], animate);
        }

        var chutesOut = false;
        var chutes = rig.ChuteIds;
        for (var i = 0; i < chutes.Count; i++)
        {
            var chute = chutes[i];
            var state = source.GetChuteState(rig.VehicleId, chute);
            // On the water the canopy is let go and collapses; it never stands over the floating capsule.
            if (vehicle.SplashedDown && state is ChuteDeployState.Deploying or ChuteDeployState.Reefed or ChuteDeployState.Open)
                state = ChuteDeployState.Released;
            rig.SetChuteState(chute, state, animate);
            chutesOut |= state is ChuteDeployState.Deploying or ChuteDeployState.Reefed or ChuteDeployState.Open;
        }

        rig.SetEntryHeating(vehicle.EntryHeating01);
        rig.SetSplashedDown(vehicle.SplashedDown);
        return !vehicle.SplashedDown && (vehicle.EntryHeating01 >= EntryHudHeatingThreshold || chutesOut);
    }

    private static Aabb? MeshBounds(Node3D root)
    {
        Aabb? bounds = null;
        foreach (var node in root.FindChildren("*", nameof(MeshInstance3D), recursive: true, owned: false))
        {
            if (node is not MeshInstance3D mesh || mesh.Mesh is null || !mesh.IsVisibleInTree() ||
                mesh.IsInGroup(SpacecraftConventions.VfxGroup))
            {
                continue;
            }
            var box = mesh.GlobalTransform * mesh.GetAabb();
            bounds = bounds is { } merged ? merged.Merge(box) : box;
        }
        return bounds;
    }
}
