using Godot;
using Usa.Game.Presentation.Rendering;

namespace Usa.Game.Presentation.Spacecraft;

/// <summary>
/// VFX-01/02a: one plume per <c>Socket_</c> node of a vehicle model, driven by the firing fractions the source may show
/// (plume rule, <see cref="ISpacecraftViewSource.GetFiringFraction"/>). Plumes are children of their sockets, so they
/// follow separated sections. A pulse stays visible for at least <see cref="MinPulseSec"/> (simulation steps are 50 ms);
/// a pulse thruster of the crewed vehicle that fires without a break for <see cref="ContinuousLookAfterSec"/> of MET
/// switches to the hot, slightly orange plume (the stuck look) and back after it stops. The look follows what the
/// thruster is shown doing, never a failure flag. As a second line of defence, an attitude thruster whose condition the
/// source reports unconfirmed never shows a plume, whatever fraction the source returns.
/// </summary>
public sealed partial class ThrusterVfxDriver : Node
{
    private const double FiringEpsilon = 1e-3;
    private const float ThrottleEpsilon = 2e-3f;

    private static readonly StringName ThrottleParam = "throttle";
    private static readonly StringName SeedParam = "seed";
    private static readonly StringName LengthParam = "length_m";
    private static readonly StringName LengthFlickerParam = "length_flicker";
    private static readonly StringName EnergyParam = "energy";
    private static readonly StringName CoreBoostParam = "core_boost";

    private readonly List<Plume> _plumes = [];
    private float[] _commanded = [];
    private readonly Dictionary<string, Material> _materials = new(StringComparer.Ordinal);
    private readonly Dictionary<Material, Material> _commandedMaterials = [];

    [Export] public double MinPulseSec { get; set; } = 0.08;
    [Export] public double DecaySec { get; set; } = 0.06;

    /// <summary>Throttle shown for the smallest non-zero fraction, so short low-duty pulses still read.</summary>
    [Export] public float MinVisibleThrottle { get; set; } = 0.35f;

    [Export] public double ContinuousLookAfterSec { get; set; } = 1.5;
    [Export] public double ContinuousLookReleaseSec { get; set; } = 0.4;
    [Export] public float CullMarginPaddingM { get; set; } = 0.1f;

    /// <summary>Size of a plume shown only for a commanded pulse, so the approach's burns read from the framing distance.</summary>
    [Export] public float CommandedPlumeScale { get; set; } = 3.2f;

    /// <summary>Width of a commanded plume relative to its length scale (shorter than wide would read as a ball).</summary>
    [Export] public float CommandedPlumeWidthShare { get; set; } = 0.6f;

    /// <summary>Brightness of a commanded plume relative to its normal look, and its hot core.</summary>
    [Export] public float CommandedEnergyScale { get; set; } = 1.6f;

    [Export] public float CommandedCoreBoost { get; set; } = 1.1f;

    /// <summary>
    /// Creates a (dark) plume under every socket of <paramref name="modelRoot"/>; returns how many. <see cref="Configure"/>
    /// gives them their look once the mission's source is known.
    /// </summary>
    public int Bind(Node modelRoot)
    {
        ArgumentNullException.ThrowIfNull(modelRoot);
        var mesh = new CylinderMesh
        {
            TopRadius = 0.5f,
            BottomRadius = 0.5f,
            Height = 1f,
            RadialSegments = 16,
            Rings = 8,
            CapTop = false,
            CapBottom = false,
        };

        var sockets = new List<Node3D>();
        FindSockets(modelRoot, sockets);
        foreach (var socket in sockets)
        {
            var id = ResolveSocketId(socket);
            if (id is null)
            {
                GD.PushWarning($"[ThrusterVfx] {socket.GetPath()}: cannot tell which thruster this socket belongs to; no plume.");
                continue;
            }
            _plumes.Add(CreatePlume(socket, id, mesh));
        }
        _commanded = new float[_plumes.Count];
        return _plumes.Count;
    }

    /// <summary>Gives every plume its look from the mission's data; sockets of components that never fire stay dark.</summary>
    public void Configure(ISpacecraftViewSource source, bool primaryVehicle)
    {
        ArgumentNullException.ThrowIfNull(source);
        foreach (var plume in _plumes)
        {
            plume.Kind = source.GetPlumeKind(plume.ComponentId);
            var path = SpacecraftConventions.PlumeMaterialPath(plume.Kind, primaryVehicle);
            plume.NormalMaterial = path is null ? null : LoadMaterial(path);
            plume.ContinuousMaterial = SpacecraftConventions.HasContinuousLook(plume.Kind, primaryVehicle)
                ? LoadMaterial(SpacecraftConventions.PlumeStuckPath)
                : null;
            plume.ShowsContinuous = false;
            if (plume.NormalMaterial is { } material)
            {
                plume.Mesh.MaterialOverride = material;
                ApplyCullMargin(plume, material);
            }
        }
        Extinguish();
    }

    /// <summary>Presentation update; call once per frame.</summary>
    /// <param name="deltaSec">Frame time, for the pulse hold and fade.</param>
    /// <param name="metDeltaSec">MET advanced since the previous frame, for the continuous (stuck) look.</param>
    public void Update(ISpacecraftViewSource source, string vehicleId, double deltaSec, double metDeltaSec)
    {
        ArgumentNullException.ThrowIfNull(source);
        // A frame never counts for more firing than it shows: under time compression one frame spans many pulses, so MET
        // alone would turn a pulsing thruster "stuck"; during a freeze MET stands still, and so does the timer.
        var firingSec = Math.Clamp(metDeltaSec, 0, Math.Max(deltaSec, 0));
        for (var i = 0; i < _plumes.Count; i++)
        {
            var plume = _plumes[i];
            if (plume.NormalMaterial is null) continue;
            var fraction = Math.Clamp(source.GetFiringFraction(vehicleId, plume.ComponentId), 0.0, 1.0);
            if (plume.Kind == PlumeKind.AttitudeThruster && !IsConfirmed(source.GetComponentKnowledge(plume.ComponentId))) fraction = 0;
            // A commanded pulse (the view's own docking approach) shows whatever the thruster's knowledge, but never counts
            // towards the continuous look: only firing the source shows can turn a plume into the stuck look.
            var commanded = _commanded[i];
            // The commanded size holds through the pulse's fade, and gives way as soon as the source shows firing.
            SetCommandedLook(plume, fraction <= FiringEpsilon && (commanded > FiringEpsilon || (plume.CommandedLook && plume.Throttle > 0f)));
            Advance(plume, Math.Max(fraction, commanded), deltaSec, fraction > FiringEpsilon ? firingSec : 0);
        }
    }

    /// <summary>Plumes created by <see cref="Bind"/>; index them for <see cref="SetCommanded"/>.</summary>
    public int PlumeCount => _plumes.Count;

    public PlumeKind PlumeKindAt(int index) => _plumes[index].Kind;

    public string PlumeComponentIdAt(int index) => _plumes[index].ComponentId;

    /// <summary>The commanded firing of plume <paramref name="index"/> (diagnostics).</summary>
    public float CommandedAt(int index) => _commanded[index];

    /// <summary>Where plume <paramref name="index"/> sits and points (+X of its socket), in <paramref name="frame"/>'s local space.</summary>
    public Transform3D PlumeTransform(int index, Node3D frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var socket = _plumes[index].Mesh.GetParent<Node3D>();
        return frame.GlobalTransform.AffineInverse() * socket.GlobalTransform;
    }

    /// <summary>
    /// Commanded firing 0..1 the view itself flies (the docking approach's translation and attitude pulses: commanded,
    /// so the plume rule lets them show). It stays until changed; <see cref="ClearCommanded"/> drops all of it.
    /// </summary>
    public void SetCommanded(int index, float fraction) => _commanded[index] = Math.Clamp(fraction, 0f, 1f);

    public void ClearCommanded() => Array.Clear(_commanded);

    /// <summary>Hides every plume at once (the view is re-initialized or detached).</summary>
    public void Extinguish()
    {
        ClearCommanded();
        foreach (var plume in _plumes)
        {
            plume.HoldSec = 0;
            plume.ContinuousSec = 0;
            plume.QuietSec = 0;
            plume.TargetThrottle = 0;
            if (plume.ShowsContinuous && plume.NormalMaterial is { } normal)
            {
                plume.ShowsContinuous = false;
                plume.Mesh.MaterialOverride = normal;
                ApplyCullMargin(plume, normal);
            }
            SetCommandedLook(plume, false);
            SetThrottle(plume, 0f);
        }
    }

    private void SetCommandedLook(Plume plume, bool commanded)
    {
        if (commanded == plume.CommandedLook) return;
        plume.CommandedLook = commanded;
        var width = CommandedPlumeScale * CommandedPlumeWidthShare;
        plume.Mesh.Scale = commanded ? new Vector3(CommandedPlumeScale, width, width) : Vector3.One;
        if (plume.ShowsContinuous || plume.NormalMaterial is not { } normal) return;
        var material = commanded ? CommandedMaterial(normal) : normal;
        plume.Mesh.MaterialOverride = material;
        ApplyCullMargin(plume, material);
    }

    // A hotter, brighter copy of the plume's look for the view's commanded pulses (made once per look, never saved).
    private Material CommandedMaterial(Material normal)
    {
        if (_commandedMaterials.TryGetValue(normal, out var hot)) return hot;
        hot = normal;
        if (normal is ShaderMaterial shader && shader.Duplicate() is ShaderMaterial copy)
        {
            copy.SetShaderParameter(EnergyParam, shader.GetShaderParameter(EnergyParam).AsSingle() * CommandedEnergyScale);
            copy.SetShaderParameter(CoreBoostParam, CommandedCoreBoost);
            hot = copy;
        }
        _commandedMaterials[normal] = hot;
        return hot;
    }

    private static bool IsConfirmed(ComponentKnowledgeView knowledge) =>
        knowledge.State is ComponentLookState.ConfirmedNominal or ComponentLookState.ConfirmedDamaged;

    /// <summary>
    /// The socket's thruster id: its <c>socket_for</c> metadata (placeholders, and real models imported through
    /// <c>ModelImportPostprocess</c>, which copies it from the glTF extras), the raw glTF extras, or the
    /// node name read against the nearest component ancestor (<c>Socket_tandem_rcs_ring_a_t01</c> under
    /// <c>tandem/rcs/ring_a</c> → <c>tandem/rcs/ring_a/t01</c>).
    /// </summary>
    public static string? ResolveSocketId(Node3D socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        if (socket.HasMeta(SpacecraftConventions.SocketForMeta)) return socket.GetMeta(SpacecraftConventions.SocketForMeta).AsString();
        if (socket.HasMeta(SpacecraftConventions.GltfExtrasMeta) &&
            socket.GetMeta(SpacecraftConventions.GltfExtrasMeta).AsGodotDictionary()
                .TryGetValue(SpacecraftConventions.SocketForMeta, out var extra))
        {
            return extra.AsString();
        }

        string name = socket.Name;
        if (!name.StartsWith(SpacecraftConventions.SocketPrefix, StringComparison.Ordinal)) return null;
        var suffix = name[SpacecraftConventions.SocketPrefix.Length..];
        for (var node = socket.GetParent(); node is not null; node = node.GetParent())
        {
            if (!node.HasMeta(RenderingConventions.ComponentIdMeta)) continue;
            var parentId = node.GetMeta(RenderingConventions.ComponentIdMeta).AsString();
            var parentName = SpacecraftConventions.NodeNameFor(parentId);
            if (suffix == parentName) return parentId;
            if (suffix.StartsWith(parentName + "_", StringComparison.Ordinal)) return $"{parentId}/{suffix[(parentName.Length + 1)..]}";
            return null;
        }
        return null;
    }

    private static void FindSockets(Node node, List<Node3D> found)
    {
        if (node is Node3D node3D &&
            (node.HasMeta(SpacecraftConventions.SocketForMeta) ||
             node.Name.ToString().StartsWith(SpacecraftConventions.SocketPrefix, StringComparison.Ordinal)))
        {
            found.Add(node3D);
        }
        foreach (var child in node.GetChildren()) FindSockets(child, found);
    }

    private static Plume CreatePlume(Node3D socket, string componentId, Mesh mesh)
    {
        var instance = new MeshInstance3D
        {
            Name = "Plume",
            Mesh = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = SpacecraftConventions.VehicleLayers,
            Visible = false,
        };
        instance.AddToGroup(SpacecraftConventions.VfxGroup);
        socket.AddChild(instance);
        instance.SetInstanceShaderParameter(SeedParam, StableSeed(componentId));
        instance.SetInstanceShaderParameter(ThrottleParam, 0f);

        return new Plume(componentId, instance);
    }

    private void Advance(Plume plume, double fraction, double deltaSec, double firingSec)
    {
        if (fraction > FiringEpsilon)
        {
            plume.TargetThrottle = MinVisibleThrottle + (1f - MinVisibleThrottle) * (float)fraction;
            plume.HoldSec = MinPulseSec;
            plume.ContinuousSec += firingSec;
            plume.QuietSec = 0;
        }
        else
        {
            plume.HoldSec -= deltaSec;
            plume.QuietSec += deltaSec;
            if (plume.QuietSec >= ContinuousLookReleaseSec) plume.ContinuousSec = 0;
        }

        var throttle = plume.HoldSec > 0
            ? plume.TargetThrottle
            : Math.Max(0f, plume.Throttle - (float)(deltaSec / Math.Max(DecaySec, 1e-3)));

        var wantsContinuous = plume.ContinuousMaterial is not null && plume.ContinuousSec >= ContinuousLookAfterSec;
        if (wantsContinuous != plume.ShowsContinuous)
        {
            plume.ShowsContinuous = wantsContinuous;
            var material = wantsContinuous ? plume.ContinuousMaterial! : plume.NormalMaterial!;
            plume.Mesh.MaterialOverride = material;
            ApplyCullMargin(plume, material);
        }

        SetThrottle(plume, throttle);
    }

    private static void SetThrottle(Plume plume, float throttle)
    {
        var visible = throttle > ThrottleEpsilon;
        var value = visible ? throttle : 0f;
        if (visible != plume.Mesh.Visible) plume.Mesh.Visible = visible;
        if (value == plume.Throttle || (value != 0f && Math.Abs(value - plume.Throttle) < ThrottleEpsilon)) return;
        plume.Throttle = value;
        plume.Mesh.SetInstanceShaderParameter(ThrottleParam, value);
    }

    // The shader stretches a unit cylinder into a cone up to length_m long, so the mesh AABB undershoots it.
    private void ApplyCullMargin(Plume plume, Material material)
    {
        var length = 1f;
        var flicker = 0f;
        if (material is ShaderMaterial shader)
        {
            length = shader.GetShaderParameter(LengthParam).AsSingle();
            flicker = shader.GetShaderParameter(LengthFlickerParam).AsSingle();
        }
        plume.Mesh.ExtraCullMargin = length * (1f + flicker) * (plume.CommandedLook ? CommandedPlumeScale : 1f) + CullMarginPaddingM;
    }

    private Material LoadMaterial(string path)
    {
        if (!_materials.TryGetValue(path, out var material))
        {
            material = GD.Load<Material>(path);
            _materials.Add(path, material);
        }
        return material;
    }

    private static float StableSeed(string componentId)
    {
        var hash = 2166136261u;
        foreach (var c in componentId) hash = (hash ^ c) * 16777619u;
        return (hash & 0xFFFF) / 655.36f;
    }

    private sealed class Plume(string componentId, MeshInstance3D mesh)
    {
        public string ComponentId { get; } = componentId;
        public MeshInstance3D Mesh { get; } = mesh;
        public PlumeKind Kind { get; set; }
        public Material? NormalMaterial { get; set; }
        public Material? ContinuousMaterial { get; set; }
        public float Throttle { get; set; }
        public float TargetThrottle { get; set; }
        public double HoldSec { get; set; }
        public double ContinuousSec { get; set; }
        public double QuietSec { get; set; }
        public bool ShowsContinuous { get; set; }
        public bool CommandedLook { get; set; }
    }
}
