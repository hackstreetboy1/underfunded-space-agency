using System.Collections.ObjectModel;
using Godot;
using Usa.Game.Presentation.Rendering;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Game.Presentation.Spacecraft;

/// <summary>
/// One vehicle in the external view: holds its model (placeholder or imported glTF), applies the body → display pose
/// (<see cref="SpacecraftAxes"/>), mates at its docking port, drops jettisoned sections, shows chute canopies and the
/// entry glow, and owns the vehicle's plumes. All inputs come from <see cref="SpacecraftView"/>; nothing here reads
/// the simulation.
/// </summary>
public sealed partial class VehicleRig : Node3D
{
    [Signal]
    public delegate void SectionJettisonedEventHandler(string sectionId);

    [Signal]
    public delegate void ChuteStateChangedEventHandler(string chuteId, ChuteDeployState state);

    [Signal]
    public delegate void SplashdownChangedEventHandler(bool splashedDown);

    private static readonly StringName HeatParam = "heat";
    private static readonly StringName CalmParam = "calm";

    private readonly Dictionary<string, Section> _sections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Chute> _chutes = new(StringComparer.Ordinal);
    private readonly List<Section> _drifting = [];
    private ReadOnlyCollection<string> _sectionIds = ReadOnlyCollection<string>.Empty;
    private ReadOnlyCollection<string> _chuteIds = ReadOnlyCollection<string>.Empty;
    private Quatd _lastOrientation;
    private double _orientationAgeSec;
    private bool _hasOrientation;
    private Transform3D _blendFrom;
    private double _blendSec = -1;
    private ShaderMaterial? _glowMaterial;
    private ShaderMaterial? _trailMaterial;
    private float _glowEnergy = -1f;
    private bool _splashedDown;
    private AnimationPlayer? _floatPlayer;

    /// <summary>Vehicle-JSON id, e.g. <c>tandem</c>.</summary>
    [Export] public string VehicleId { get; set; } = string.Empty;

    /// <summary>The model root (placeholder scene or imported .glb); swapped 1:1 when the real model is approved.</summary>
    [Export] public Node3D? Model { get; set; }

    /// <summary>Child marker at the docking interface, +X pointing out of the vehicle.</summary>
    [Export] public Node3D? DockingPort { get; set; }

    [Export] public ThrusterVfxDriver? Thrusters { get; set; }

    /// <summary>Optional VFX-02b shell around the heat shield; its ShaderMaterial must be local to the scene.</summary>
    [Export] public GeometryInstance3D? EntryGlow { get; set; }

    /// <summary>Optional faint wake behind the capsule, drawn with the same entry-glow shader as <see cref="EntryGlow"/>.</summary>
    [Export] public GeometryInstance3D? EntryTrail { get; set; }

    /// <summary>Optional parent of chute canopies (children carry <c>chute_for</c> metadata), attach point at its origin.</summary>
    [Export] public Node3D? Chutes { get; set; }

    [Export] public double PoseBlendSec { get; set; } = 1.2;

    /// <summary>Between simulation steps the orientation is advanced with the last angular velocity, at most this long.</summary>
    [Export] public double MaxExtrapolationSec { get; set; } = 0.15;

    [Export] public float SeparationSpeedMps { get; set; } = 0.5f;
    [Export] public float SeparationTumbleDegPerSec { get; set; } = 5f;

    /// <summary>A drifting section is hidden once it is this far from the rig.</summary>
    [Export] public float SeparationHideDistanceM { get; set; } = 80f;

    [Export] public double ChuteDeploySec { get; set; } = 1.2;

    public bool IsSplashedDown => _splashedDown;

    /// <summary>Separable sections found in the model, in staging order (index it; the view reads it every frame).</summary>
    public IReadOnlyList<string> SectionIds => _sectionIds;

    public IReadOnlyList<string> ChuteIds => _chuteIds;

    /// <summary>Flies the drawn capsule heat shield first through entry (<see cref="EntryAttitude"/>).</summary>
    public EntryAttitude Entry { get; } = new();

    /// <summary>Hangs the drawn capsule nose-up under an open chute (<see cref="ChuteHang"/>).</summary>
    public ChuteHang Hang { get; } = new();

    /// <summary>The docking port relative to the rig (identity when the rig has none).</summary>
    public Transform3D DockingPortLocal =>
        DockingPort is null ? Transform3D.Identity : GlobalTransform.AffineInverse() * DockingPort.GlobalTransform;

    public override void _Ready()
    {
        if (EntryGlow is not null)
        {
            _glowMaterial = EntryGlow.MaterialOverride as ShaderMaterial;
            EntryGlow.Visible = false;
        }
        if (EntryTrail is not null)
        {
            _trailMaterial = EntryTrail.MaterialOverride as ShaderMaterial;
            EntryTrail.Visible = false;
        }
        if (Chutes is not null) FindChutes(Chutes);
        if (Model is not null) FindChuteComponents(Model);
        _chuteIds = Array.AsReadOnly(_chutes.Keys.ToArray());
    }

    /// <summary>Creates the plumes; returns how many sockets were found.</summary>
    public int BindThrusters() => Model is not null && Thrusters is not null ? Thrusters.Bind(Model) : 0;

    /// <summary>
    /// Takes the mission's data for this vehicle: its separable sections and its plume looks. Restores the rig first
    /// (<see cref="ResetStaging"/>), so a view reused for another run starts whole.
    /// </summary>
    public void Configure(ISpacecraftViewSource source, bool primaryVehicle)
    {
        ArgumentNullException.ThrowIfNull(source);
        ResetStaging();
        _sections.Clear();
        var ids = new List<string>();
        if (Model is not null)
        {
            foreach (var spec in source.GetSeparableSections(VehicleId))
            {
                var node = FindSectionNode(Model, spec.SectionId);
                if (node is null)
                {
                    GD.PushWarning($"[VehicleRig] {VehicleId}: no node for section '{spec.SectionId}'; it cannot separate.");
                    continue;
                }
                _sections[spec.SectionId] = new Section(node, SpacecraftConventions.SeparationAnimation(spec.SeparationEventId));
                ids.Add(spec.SectionId);
            }
        }
        _sectionIds = ids.AsReadOnly();
        Thrusters?.Configure(source, primaryVehicle);
    }

    /// <summary>
    /// Puts every jettisoned section back, stows the chutes, clears the splashdown, the entry glow, the pose blend and the
    /// orientation history, and puts the plumes out.
    /// </summary>
    public void ResetStaging()
    {
        foreach (var section in _sections.Values) section.Restore();
        _drifting.Clear();
        foreach (var chute in _chutes.Values) chute.Stow();
        _hasOrientation = false;
        _orientationAgeSec = 0;
        _blendSec = -1;
        Entry.ResetMotion();
        Hang.Reset();
        SetEntryHeating(0);
        SetSplashedDown(false);
        SetFloating(false);
        Thrusters?.Extinguish();
    }

    /// <summary>
    /// The orientation to draw this frame: the latest sample, advanced by <paramref name="vehicle"/>'s angular velocity
    /// for the time since that sample arrived (the attitude model steps at 20 Hz; a fast roll would stutter).
    /// </summary>
    public Quatd SmoothedOrientation(in SpacecraftVehicleView vehicle, double deltaSec)
    {
        if (!_hasOrientation || vehicle.Orientation != _lastOrientation)
        {
            _lastOrientation = vehicle.Orientation;
            _orientationAgeSec = 0;
            _hasOrientation = true;
            return vehicle.Orientation;
        }
        _orientationAgeSec += deltaSec;
        return SpacecraftAxes.Extrapolate(
            vehicle.Orientation, vehicle.AngularVelocityRadPerSec, Math.Min(_orientationAgeSec, MaxExtrapolationSec));
    }

    /// <summary>Starts a short blend from the current pose (docking or undocking changes where the pose comes from).</summary>
    public void BeginPoseBlend()
    {
        _blendFrom = Transform;
        _blendSec = 0;
    }

    public void ApplyPose(Transform3D target, double deltaSec)
    {
        if (_blendSec < 0 || PoseBlendSec <= 0)
        {
            Transform = target;
            return;
        }

        _blendSec += deltaSec;
        var t = (float)Math.Clamp(_blendSec / PoseBlendSec, 0, 1);
        t = t * t * (3f - 2f * t);
        var from = _blendFrom.Basis.GetRotationQuaternion();
        var to = target.Basis.GetRotationQuaternion();
        Transform = new Transform3D(new Basis(from.Slerp(to, t)), _blendFrom.Origin.Lerp(target.Origin, t));
        if (t >= 1f) _blendSec = -1;
    }

    /// <summary>
    /// Drops a section. Plays the model's separation action when it has one (TAN-03), otherwise lets the section
    /// drift away along the vehicle's −X with a slight tumble. <paramref name="animate"/> false (loading a save)
    /// just hides it. Later calls for the same section do nothing.
    /// </summary>
    public void SetSectionJettisoned(string sectionId, bool animate)
    {
        if (!_sections.TryGetValue(sectionId, out var section) || section.Jettisoned) return;
        section.Jettisoned = true;

        if (!animate)
        {
            section.Node.Visible = false;
        }
        else if (FindModelAnimation(section.Animation) is { } player)
        {
            section.Player = player;
            section.OnAnimationFinished = finished =>
            {
                if (finished == section.Animation) section.Node.Visible = false;
            };
            player.AnimationFinished += section.OnAnimationFinished;
            player.Play(section.Animation);
        }
        else
        {
            var frame = GetParent<Node3D>();
            section.Node.Reparent(frame, keepGlobalTransform: true);
            var aft = -(GlobalTransform.Basis.X.Normalized());
            section.VelocityMps = aft * SeparationSpeedMps;
            section.TumbleAxis = GlobalTransform.Basis.Z.Normalized();
            _drifting.Add(section);
        }
        EmitSignal(SignalName.SectionJettisoned, sectionId);
    }

    public void SetChuteState(string chuteId, ChuteDeployState state, bool animate)
    {
        if (!_chutes.TryGetValue(chuteId, out var chute) || chute.State == state) return;
        chute.State = state;
        if (!animate) chute.Scale = TargetScale(state);
        EmitSignal(SignalName.ChuteStateChanged, chuteId, (int)state);
    }

    public void SetEntryHeating(double heating01)
    {
        if (EntryGlow is null) return;
        var heat = (float)Math.Clamp(heating01, 0, 1);
        if (Math.Abs(heat - _glowEnergy) < 1e-3f) return;
        _glowEnergy = heat;
        var visible = heat > 1e-3f;
        EntryGlow.Visible = visible;
        _glowMaterial?.SetShaderParameter(HeatParam, heat);
        if (EntryTrail is null) return;
        EntryTrail.Visible = visible;
        _trailMaterial?.SetShaderParameter(HeatParam, heat);
    }

    /// <summary>Steady glow without flicker: reduced motion, or the app is frozen.</summary>
    public void SetGlowCalm(bool calm)
    {
        var value = calm ? 1f : 0f;
        _glowMaterial?.SetShaderParameter(CalmParam, value);
        _trailMaterial?.SetShaderParameter(CalmParam, value);
    }

    public void SetSplashedDown(bool splashedDown)
    {
        if (splashedDown == _splashedDown) return;
        _splashedDown = splashedDown;
        EmitSignal(SignalName.SplashdownChanged, splashedDown);
    }

    /// <summary>
    /// Starts or stops the model's own splashdown float (<see cref="SpacecraftConventions.FloatAnimation"/>, looped). Returns
    /// true while the model floats itself, so the rig stays at the origin; false when it has no such action (placeholders)
    /// and the caller poses the rig instead.
    /// </summary>
    public bool SetFloating(bool floating)
    {
        if (!floating)
        {
            if (_floatPlayer is { } player && IsInstanceValid(player))
            {
                player.Seek(0, update: true);
                player.Stop();
            }
            _floatPlayer = null;
            return false;
        }
        if (_floatPlayer is not null) return true;
        _floatPlayer = FindModelAnimation(SpacecraftConventions.FloatAnimation);
        if (_floatPlayer is null) return false;
        _floatPlayer.GetAnimation(SpacecraftConventions.FloatAnimation).LoopMode = Animation.LoopModeEnum.Linear;
        _floatPlayer.Play(SpacecraftConventions.FloatAnimation);
        return true;
    }

    public override void _Process(double delta)
    {
        UpdateDrifting(delta);
        UpdateChutes(delta);
    }

    private void UpdateDrifting(double delta)
    {
        for (var i = _drifting.Count - 1; i >= 0; i--)
        {
            var section = _drifting[i];
            var node = section.Node;
            node.GlobalPosition += section.VelocityMps * (float)delta;
            node.GlobalRotate(section.TumbleAxis, Mathf.DegToRad(SeparationTumbleDegPerSec) * (float)delta);
            if (node.GlobalPosition.DistanceTo(GlobalPosition) > SeparationHideDistanceM)
            {
                node.Visible = false;
                _drifting.RemoveAt(i);
            }
        }
    }

    private void UpdateChutes(double delta)
    {
        foreach (var chute in _chutes.Values)
        {
            if (chute.Canopy is null) continue;
            var target = TargetScale(chute.State);
            var rate = (float)(delta / Math.Max(ChuteDeploySec, 1e-3));
            chute.Scale = Mathf.MoveToward(chute.Scale, target, rate);
            chute.BreathPhase += delta;
            var breath = chute.State == ChuteDeployState.Open
                ? 1f + 0.03f * Mathf.Sin((float)(chute.BreathPhase * Mathf.Tau * 0.4))
                : 1f;
            var visible = chute.Scale > 0.02f;
            if (chute.Canopy.Visible != visible) chute.Canopy.Visible = visible;
            if (visible) chute.Canopy.Scale = new Vector3(chute.Scale, chute.Scale * breath, chute.Scale * breath);
        }
    }

    private static float TargetScale(ChuteDeployState state) => state switch
    {
        ChuteDeployState.Deploying => 0.35f,
        ChuteDeployState.Reefed => 0.55f,
        ChuteDeployState.Open => 1f,
        _ => 0f,
    };

    private AnimationPlayer? FindModelAnimation(string animation)
    {
        if (Model is null) return null;
        foreach (var node in Model.FindChildren("*", nameof(AnimationPlayer), recursive: true, owned: false))
        {
            if (node is AnimationPlayer player && player.HasAnimation(animation)) return player;
        }
        return null;
    }

    private Node3D? FindSectionNode(Node3D model, string sectionId)
    {
        foreach (var node in model.FindChildren("*", nameof(Node3D), recursive: true, owned: false))
        {
            if (node is Node3D section && ModelProperties.TryGetString(node, SpacecraftConventions.SectionIdMeta, out var id) &&
                id == sectionId)
            {
                return section;
            }
        }
        return model.FindChild(SpacecraftConventions.SectionNodeName(VehicleId, sectionId), recursive: true, owned: false) as Node3D;
    }

    private void FindChutes(Node3D chutes)
    {
        foreach (var child in chutes.GetChildren())
        {
            if (child is not Node3D canopy || !ModelProperties.TryGetString(child, SpacecraftConventions.ChuteForMeta, out var chuteId)) continue;
            canopy.Visible = false;
            _chutes[chuteId] = new Chute(canopy);
        }
    }

    // Packed chutes are components of the model; they are tracked even without a canopy so states still signal.
    private void FindChuteComponents(Node node)
    {
        if (node.HasMeta(RenderingConventions.ComponentIdMeta))
        {
            var id = node.GetMeta(RenderingConventions.ComponentIdMeta).AsString();
            if (id.Contains("/chute/", StringComparison.Ordinal)) _chutes.TryAdd(id, new Chute(null));
        }
        foreach (var child in node.GetChildren()) FindChuteComponents(child);
    }

    private sealed class Section(Node3D node, string animation)
    {
        private readonly Node? _parent = node.GetParent();
        private readonly Transform3D _transform = node.Transform;

        public Node3D Node { get; } = node;
        public string Animation { get; } = animation;
        public bool Jettisoned { get; set; }
        public Vector3 VelocityMps { get; set; }
        public Vector3 TumbleAxis { get; set; } = Vector3.Up;
        public AnimationPlayer? Player { get; set; }
        public AnimationMixer.AnimationFinishedEventHandler? OnAnimationFinished { get; set; }

        /// <summary>Back where the model had it, attached and visible; a separation action is rewound.</summary>
        public void Restore()
        {
            if (Player is { } player && GodotObject.IsInstanceValid(player))
            {
                if (OnAnimationFinished is { } handler) player.AnimationFinished -= handler;
                if (player.HasAnimation(Animation))
                {
                    player.Play(Animation);
                    player.Seek(0, update: true);
                    player.Stop();
                }
            }
            Player = null;
            OnAnimationFinished = null;
            if (_parent is not null && GodotObject.IsInstanceValid(_parent) && Node.GetParent() != _parent) Node.Reparent(_parent, keepGlobalTransform: false);
            Node.Transform = _transform;
            Node.Visible = true;
            Jettisoned = false;
            VelocityMps = Vector3.Zero;
        }
    }

    private sealed class Chute(Node3D? canopy)
    {
        public Node3D? Canopy { get; } = canopy;
        public ChuteDeployState State { get; set; } = ChuteDeployState.Stowed;
        public float Scale { get; set; }
        public double BreathPhase { get; set; }

        public void Stow()
        {
            State = ChuteDeployState.Stowed;
            Scale = 0;
            BreathPhase = 0;
            if (Canopy is not null) Canopy.Visible = false;
        }
    }
}
