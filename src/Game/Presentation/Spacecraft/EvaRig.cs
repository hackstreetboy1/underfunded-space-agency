using Godot;

namespace Usa.Game.Presentation.Spacecraft;

/// <summary>
/// The spacewalk in the external view (child of the crewed vehicle's rig, so it shares the vehicle's frame): while
/// <see cref="ISpacecraftViewSource.EvaCrew"/> is set it shows the suited crew member outside the hatch, the umbilical to
/// the hatch and the experiment package, which sits on the vehicle until retrieved and then rides in her hand. Everything
/// hides when she is back inside. Motion (idle animation, drift) stops while frozen and under reduced motion.
/// </summary>
public sealed partial class EvaRig : Node3D
{
    private const string SuitScenePath = "res://assets/models/crew/june_eva.glb";
    private const string IdleAnimation = "eva_float_idle";
    private const int CableSegments = 14;
    private const int CableSides = 6;
    private const float CableRadiusM = 0.018f;

    private const double CableRefreshS = 0.1;

    private Node3D? _suit;
    private double _cableAgeS;
    private AnimationPlayer? _player;
    private BoneAttachment3D? _handSocket;
    private MeshInstance3D _package = null!;
    private MeshInstance3D _cable = null!;
    private Vector3 _suitBase;
    private double _timeSec;
    private bool _frozen;
    private bool _reducedMotion;
    private bool _shown;
    private bool _retrieved;

    /// <summary>Hatch position in the vehicle rig's space (mission 1: the right hatch).</summary>
    [Export] public Vector3 HatchLocal { get; set; } = new(0.62f, 0.576f, 0.306f);

    /// <summary>How far outside the hatch the suit floats, metres.</summary>
    [Export] public float SuitDistanceM { get; set; } = 1.1f;

    /// <summary>Where the package sits on the vehicle before retrieval (rig space).</summary>
    [Export] public Vector3 PackageRestLocal { get; set; } = new(1.45f, 0.35f, 0.4f);

    /// <summary>Hand position, suit space, used when the model has no hand bone.</summary>
    [Export] public Vector3 PackageHandFallbackLocal { get; set; } = new(0.3f, 0.7f, 0.2f);

    /// <summary>Where the cable plugs into the suit (backpack), suit space (feet at the origin, up = away from the hull).</summary>
    [Export] public Vector3 SuitConnectorLocal { get; set; } = new(0f, 0.85f, -0.22f);

    /// <summary>Scale of the suit model, which is authored about 1 m tall.</summary>
    [Export] public float SuitScale { get; set; } = 1.3f;

    [Export] public float CableSagM { get; set; } = 0.12f;
    [Export] public float BobAmplitudeM { get; set; } = 0.02f;

    public bool ReducedMotion
    {
        get => _reducedMotion;
        set { _reducedMotion = value; ApplyAnimationSpeed(); }
    }

    public bool IsShown => _shown;

    /// <summary>What the camera frames during the spacewalk; null while she is inside.</summary>
    public Node3D? FocusTarget => _shown ? _suit : null;

    public override void _Ready()
    {
        _package = new MeshInstance3D
        {
            Name = "ExperimentPackage",
            Mesh = new BoxMesh { Size = new Vector3(0.22f, 0.14f, 0.14f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.85f, 0.8f), Roughness = 0.6f },
            Visible = false,
        };
        AddChild(_package);

        _cable = new MeshInstance3D
        {
            Name = "Umbilical",
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.45f, 0.12f), Roughness = 0.7f },
            Visible = false,
        };
        AddChild(_cable);

        if (GD.Load<PackedScene>(SuitScenePath)?.Instantiate() is Node3D model)
        {
            // june_eva.glb's skin does not match its mesh (identity bind poses, vertices authored ~4.4 m off the skeleton) and
            // its animations are authored in another frame, so skinned she renders metres away from where her bones are. She
            // is drawn as the static mesh instead: unskinned, recentred with her feet on the pivot and scaled, and the pivot
            // floats outside the hatch (BeginEva). No hand bone: the package rides at PackageHandFallbackLocal.
            var suit = new Node3D { Name = "Suit", Visible = false };
            AddChild(suit);
            suit.AddChild(model);
            foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), recursive: true, owned: false).OfType<MeshInstance3D>())
            {
                mesh.Skin = null;
                mesh.Skeleton = new NodePath();
            }
            var bounds = MeshBounds(model);
            model.Scale = Vector3.One * SuitScale;
            model.Position = new Vector3(-bounds.GetCenter().X, -bounds.Position.Y, -bounds.GetCenter().Z) * SuitScale;
            _suit = suit;
        }
        Visible = false;
    }

    public void SetFrozen(bool frozen)
    {
        _frozen = frozen;
        ApplyAnimationSpeed();
    }

    /// <summary>Applies this frame's view: null hides the rig.</summary>
    public void Apply(EvaCrewView? crew, double delta)
    {
        var show = crew is not null;
        if (show != _shown)
        {
            _shown = show;
            Visible = show;
            if (_suit is not null) _suit.Visible = show;
            if (show) BeginEva();
            else _player?.Stop();
        }
        if (crew is not { } eva) return;

        if (!_frozen && !_reducedMotion) _timeSec += delta;
        if (_suit is not null)
        {
            var bob = _reducedMotion ? 0f : Mathf.Sin((float)_timeSec * 1.3f) * BobAmplitudeM;
            _suit.Position = _suitBase + Vector3.Up * bob;
            if (!_frozen) TrackCable(delta);
        }

        if (eva.PackageRetrieved != _retrieved)
        {
            _retrieved = eva.PackageRetrieved;
            PlacePackage();
        }
    }

    private void BeginEva()
    {
        var outward = EvaPlacement.Outward(HatchLocal);
        _suitBase = HatchLocal + outward * SuitDistanceM;
        if (_suit is not null)
        {
            _suit.Basis = new Basis(new Quaternion(Vector3.Up, outward));
            _suit.Position = _suitBase;
            _timeSec = 0;
            if (_player?.HasAnimation(IdleAnimation) == true) _player.Play(IdleAnimation);
            ApplyAnimationSpeed();
        }
        _retrieved = false;
        PlacePackage();
        BuildCable();
    }

    private void PlacePackage()
    {
        var carried = _retrieved;
        Node target = !carried ? this : _handSocket ?? (Node?)_suit ?? this;
        if (_package.GetParent() != target)
        {
            _package.GetParent()?.RemoveChild(_package);
            target.AddChild(_package);
        }
        _package.Position = !carried ? PackageRestLocal : _handSocket is not null ? Vector3.Zero : PackageHandFallbackLocal;
        _package.Rotation = Vector3.Zero;
        _package.Visible = true;
    }

    private void ApplyAnimationSpeed()
    {
        if (_player is not null) _player.SpeedScale = _frozen || _reducedMotion ? 0f : 1f;
    }

    /// <summary>The umbilical's suit end (her backpack), in rig space; follows her drift.</summary>
    private Vector3 SuitConnector()
    {
        if (_suit is null) return HatchLocal;
        return _suit.Position + _suit.Basis * SuitConnectorLocal;
    }

    /// <summary>Combined bounds of the model's meshes in the model's own space.</summary>
    private static Aabb MeshBounds(Node3D model)
    {
        Aabb? bounds = null;
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), recursive: true, owned: false).OfType<MeshInstance3D>())
        {
            var toModel = Transform3D.Identity;
            for (Node? n = mesh; n is not null && n != model; n = n.GetParent())
            {
                if (n is Node3D spatial) toModel = spatial.Transform * toModel;
            }
            var box = toModel * mesh.GetAabb();
            bounds = bounds is { } b ? b.Merge(box) : box;
        }
        return bounds ?? new Aabb(Vector3.Zero, Vector3.Zero);
    }

    // The suit drifts with its animation, so the cable is re-swept a few times a second to stay plugged in.
    private void TrackCable(double delta)
    {
        _cableAgeS += delta;
        if (_cableAgeS < CableRefreshS) return;
        _cableAgeS = 0;
        BuildCable();
    }

    // Hand bone when the model has one (the importer may rename "hand.R" to "hand_R"), else null and an offset is used.
    private static BoneAttachment3D? FindHandSocket(Node3D root)
    {
        if (root.FindChildren("*", nameof(Skeleton3D), recursive: true, owned: false).FirstOrDefault() is not Skeleton3D skeleton) return null;
        var bone = skeleton.FindBone("hand.R");
        if (bone < 0) bone = skeleton.FindBone("hand_R");
        if (bone < 0) return null;
        var attachment = new BoneAttachment3D { Name = "HandSocket", BoneName = skeleton.GetBoneName(bone) };
        skeleton.AddChild(attachment);
        return attachment;
    }

    // The shipped eva_umbilical.glb is a short baked piece that cannot span hatch to suit, so the cable is swept here.
    private void BuildCable()
    {
        var start = HatchLocal;
        var end = SuitConnector();
        var vertices = new Vector3[(CableSegments + 1) * CableSides];
        var indices = new List<int>();
        for (var i = 0; i <= CableSegments; i++)
        {
            var t = i / (float)CableSegments;
            var p = EvaPlacement.CablePoint(start, end, CableSagM, t);
            var next = EvaPlacement.CablePoint(start, end, CableSagM, Math.Min(t + 0.02f, 1f));
            var tangent = (next - p).LengthSquared() < 1e-10f ? (end - start).Normalized() : (next - p).Normalized();
            var side = tangent.Cross(Vector3.Up);
            side = side.LengthSquared() < 1e-6f ? Vector3.Right : side.Normalized();
            var up = side.Cross(tangent);
            for (var s = 0; s < CableSides; s++)
            {
                var a = s * Mathf.Tau / CableSides;
                vertices[i * CableSides + s] = p + (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * CableRadiusM;
            }
        }
        for (var i = 0; i < CableSegments; i++)
        {
            for (var s = 0; s < CableSides; s++)
            {
                var a = i * CableSides + s;
                var b = i * CableSides + (s + 1) % CableSides;
                indices.AddRange([a, b, a + CableSides, b, b + CableSides, a + CableSides]);
            }
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _cable.Mesh = mesh;
        _cable.Visible = true;
    }
}
