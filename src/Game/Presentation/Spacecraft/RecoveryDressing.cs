using Godot;

namespace Usa.Game.Presentation.Spacecraft;

/// <summary>
/// The external view after the crewed capsule splashes down (GDD 7.3 step 7, UI 4.3; MODELING 10.3-10.5): ocean, sea sky
/// and Sun, the recovery ship <i>Albatross</i> on the horizon and the recovery helicopter circling the capsule. Root of
/// <c>res://scenes/views/RecoveryDressing.tscn</c>, placed in the spacecraft view and driven by it; hidden (lights
/// included) until <see cref="SetActive"/>. The capsule floats at the origin with the waterline at y = 0. How far the
/// recovery has come is the recovery card's estimate (<see cref="SetProgress"/>), never the simulation's truth. The
/// forces stay out of sight until the first estimate arrives and then ease towards it, and the helicopter flies in rather
/// than appearing overhead (review 3, R3-13).
/// </summary>
public sealed partial class RecoveryDressing : Node3D
{
    private const string RotorAnimation = "rotor_spin";
    private const string RotorStateKey = "rotor_state";

    private readonly List<Node3D> _rotorBlades = [];
    private readonly List<Node3D> _rotorBlur = [];
    private AnimationPlayer? _rotor;
    private double _timeSec;
    private double _target01;
    private double _shown01;
    private double _helicopterArrival01;
    private bool _hasEstimate;

    [Export] public Node3D? Ship { get; set; }
    [Export] public Node3D? Helicopter { get; set; }

    /// <summary>Sea sky, fog and tonemapping; the spacecraft view swaps it in for the orbital environment.</summary>
    [Export] public Godot.Environment? SeaEnvironment { get; set; }

    /// <summary>Direction of the ship from the capsule, degrees about +Y from +Z.</summary>
    [Export] public float ShipBearingDeg { get; set; } = 215f;

    [Export] public float ShipNearM { get; set; } = 90f;

    /// <summary>Ship distance at the start of the wait, inside the fog (the fog ends at about 200 m).</summary>
    [Export] public float ShipFarM { get; set; } = 240f;

    /// <summary>Share of the recovery wait after which the helicopter is overhead.</summary>
    [Export] public double HelicopterArrivesAt01 { get; set; } = 0.6;

    [Export] public float HelicopterOrbitRadiusM { get; set; } = 30f;
    [Export] public float HelicopterAltitudeM { get; set; } = 14f;
    [Export] public double HelicopterOrbitPeriodSec { get; set; } = 45.0;
    [Export] public float HelicopterBankDeg { get; set; } = 8f;

    /// <summary>Time constant of the eased progress: a jump in the estimate settles in about four of these.</summary>
    [Export] public double ProgressEaseSec { get; set; } = 0.5;

    /// <summary>How long the helicopter takes to fly in from the ship's side to its orbit over the capsule.</summary>
    [Export] public double HelicopterApproachSec { get; set; } = 6.0;

    /// <summary>Where the inbound helicopter starts, metres out along the ship's bearing and up.</summary>
    [Export] public float HelicopterApproachFromM { get; set; } = 160f;

    [Export] public float HelicopterApproachAltitudeM { get; set; } = 40f;

    /// <summary>Capsule float for models without their own float action (placeholders): nose this far from vertical.</summary>
    [Export] public float FloatTiltDeg { get; set; } = 20f;

    /// <summary>Model x of the waterline (TAN-03: 0.35 m from the heat-shield plane).</summary>
    [Export] public float WaterlineXM { get; set; } = 0.35f;

    [Export] public float BobHeightM { get; set; } = 0.08f;
    [Export] public double BobPeriodSec { get; set; } = 4.5;
    [Export] public float RockDeg { get; set; } = 3f;

    public bool Active { get; private set; }

    public override void _Ready()
    {
        // The camera fill and the Earth-shine skip the backdrop layer; only the sea Sun and the sky light the dressing.
        foreach (var node in FindChildren("*", nameof(GeometryInstance3D), recursive: true, owned: false))
        {
            if (node is GeometryInstance3D geometry) geometry.Layers = SpacecraftConventions.BackdropLayers;
        }
        if (Helicopter is not null) FindRotor(Helicopter);
        Visible = false;
        SetRotorSpinning(false);
    }

    public void SetActive(bool active)
    {
        if (active == Active) return;
        Active = active;
        Visible = active;
        _timeSec = 0;
        _target01 = 0;
        _shown01 = 0;
        _helicopterArrival01 = 0;
        _hasEstimate = false;
        SetRotorSpinning(active);
        SetDrop(0f);
        if (active) Place();
    }

    /// <summary>
    /// Recovery wait elapsed, 0..1, as the player's recovery card estimates it. Null before the card's first estimate keeps
    /// the forces at the start of the wait (ship in the fog, no helicopter); a later null keeps the last estimate.
    /// </summary>
    public void SetProgress(double? progress01)
    {
        if (progress01 is not { } value) return;
        _hasEstimate = true;
        _target01 = Math.Clamp(value, 0.0, 1.0);
    }

    /// <summary>The progress the forces are drawn at (eased towards the estimate).</summary>
    public double ShownProgress01 => _shown01;

    /// <summary>Whether an estimate has arrived since the dressing became active.</summary>
    public bool HasEstimate => _hasEstimate;

    /// <summary>
    /// How far below the capsule the sea is drawn, metres: the descent under the main chute lowers the whole dressing (water,
    /// ship, helicopter) and lets it rise to meet the capsule; 0 at splashdown.
    /// </summary>
    public void SetDrop(float heightM)
    {
        var drop = Math.Max(0f, heightM);
        Position = new Vector3(0f, -drop, 0f);
        // The ocean's foam ring marks where the capsule sits on the water: none until it gets there.
        if (GetNodeOrNull<MeshInstance3D>("Ocean")?.MaterialOverride is ShaderMaterial ocean)
            ocean.SetShaderParameter("foam_strength", drop > 0.05f ? 0f : 1f);
    }

    /// <summary>The capsule's pose on the water for a model that has no float action.</summary>
    public Transform3D FloatPose()
    {
        var bob = BobHeightM * Mathf.Sin((float)(Mathf.Tau * _timeSec / BobPeriodSec));
        var rock = Mathf.DegToRad(RockDeg) * Mathf.Sin((float)(Mathf.Tau * _timeSec / (BobPeriodSec * 1.7)));
        var basis = new Basis(Vector3.Right, rock) * new Basis(Vector3.Back, Mathf.DegToRad(90f - FloatTiltDeg));
        return new Transform3D(basis, -(basis * new Vector3(WaterlineXM, 0f, 0f)) + new Vector3(0f, bob, 0f));
    }

    public override void _Process(double delta)
    {
        if (!Active) return;
        _timeSec += delta;
        _shown01 = Ease(_shown01, _target01, delta, ProgressEaseSec);
        var overhead = _shown01 >= HelicopterArrivesAt01;
        _helicopterArrival01 = overhead ? Math.Min(1.0, _helicopterArrival01 + (delta / Math.Max(HelicopterApproachSec, 0.01))) : 0.0;
        Place();
    }

    private void Place()
    {
        if (Ship is not null)
        {
            var approach = Mathf.SmoothStep(0f, 1f, (float)_shown01);
            var bearing = Mathf.DegToRad(ShipBearingDeg);
            var distance = Mathf.Lerp(ShipFarM, ShipNearM, approach);
            // Broadside to the capsule: the bow (+Z) points across the line of sight.
            Ship.Transform = new Transform3D(
                new Basis(Vector3.Up, bearing + Mathf.Pi * 0.5f),
                new Vector3(Mathf.Sin(bearing), 0f, Mathf.Cos(bearing)) * distance);
        }

        if (Helicopter is null) return;
        var overhead = _shown01 >= HelicopterArrivesAt01;
        if (Helicopter.Visible != overhead) Helicopter.Visible = overhead;
        if (!overhead) return;

        var angle = (float)(Mathf.Tau * _timeSec / Math.Max(HelicopterOrbitPeriodSec, 1.0));
        var position = new Vector3(Mathf.Cos(angle) * HelicopterOrbitRadiusM, HelicopterAltitudeM, Mathf.Sin(angle) * HelicopterOrbitRadiusM);
        var heading = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
        var yaw = new Basis(Vector3.Up, Mathf.Atan2(heading.X, heading.Z));
        // Bank into the turn: the nose is +Z, so a roll about +Z tilts the rotor towards the centre.
        var towardCentre = new Vector3(-position.X, 0f, -position.Z);
        var side = towardCentre.Dot(yaw.X) >= 0f ? -1f : 1f;
        var orbiting = new Transform3D(yaw * new Basis(Vector3.Back, side * Mathf.DegToRad(HelicopterBankDeg)), position);
        Helicopter.Transform = _helicopterArrival01 >= 1.0 ? orbiting : Inbound(orbiting);
    }

    /// <summary>Exponential approach of <paramref name="shown"/> to <paramref name="target"/>, frame-rate independent.</summary>
    public static double Ease(double shown, double target, double deltaSec, double timeConstantSec) =>
        timeConstantSec <= 0 ? target : shown + ((target - shown) * (1.0 - Math.Exp(-Math.Max(0.0, deltaSec) / timeConstantSec)));

    /// <summary>The helicopter flying in from the ship's side, levelling into its orbit as it arrives.</summary>
    private Transform3D Inbound(Transform3D orbiting)
    {
        var t = Mathf.SmoothStep(0f, 1f, (float)_helicopterArrival01);
        var bearing = Mathf.DegToRad(ShipBearingDeg);
        var from = (new Vector3(Mathf.Sin(bearing), 0f, Mathf.Cos(bearing)) * HelicopterApproachFromM) + (Vector3.Up * HelicopterApproachAltitudeM);
        var position = from.Lerp(orbiting.Origin, t);
        var travel = orbiting.Origin - from;
        var heading = new Basis(Vector3.Up, Mathf.Atan2(travel.X, travel.Z));
        var basis = heading.Slerp(orbiting.Basis, t);
        return new Transform3D(basis, position);
    }

    private void FindRotor(Node node)
    {
        if (node is AnimationPlayer player && player.HasAnimation(RotorAnimation)) _rotor = player;
        if (node is Node3D part)
        {
            var state = ModelProperties.TryGetString(part, RotorStateKey, out var value) ? value : string.Empty;
            string name = part.Name;
            if (state == "blur" || (state.Length == 0 && name.EndsWith("Blur", StringComparison.Ordinal))) _rotorBlur.Add(part);
            else if (state == "blades" || (state.Length == 0 && name.EndsWith("Blades", StringComparison.Ordinal))) _rotorBlades.Add(part);
        }
        foreach (var child in node.GetChildren()) FindRotor(child);
    }

    // At flying speed the blades read as the blurred disc (ENV-05).
    private void SetRotorSpinning(bool spinning)
    {
        foreach (var blades in _rotorBlades) blades.Visible = !spinning;
        foreach (var blur in _rotorBlur) blur.Visible = spinning;
        if (_rotor is null) return;
        if (!spinning)
        {
            _rotor.Stop();
            return;
        }
        _rotor.GetAnimation(RotorAnimation).LoopMode = Animation.LoopModeEnum.Linear;
        _rotor.Play(RotorAnimation);
    }
}
