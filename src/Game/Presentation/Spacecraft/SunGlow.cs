using Godot;

namespace Usa.Game.Presentation.Spacecraft;

/// <summary>
/// ENV-02 Sun disc: a soft glow billboard far out along the Sun direction, on the backdrop layer. It sits beyond the
/// Earth sphere, so the Earth hides it by depth as the Sun sets behind the limb, and it fades with the same eclipse
/// factor as the Sun light. The texture and material are generated, so it needs no asset.
/// </summary>
public sealed partial class SunGlow : Node3D
{
    private MeshInstance3D _billboard = null!;
    private StandardMaterial3D _material = null!;
    private float _shownAlpha = -1f;

    [Export] public float DistanceM { get; set; } = 9000f;

    /// <summary>Apparent diameter of the whole glow (the bright disc is about a sixth of it).</summary>
    [Export] public float GlowDiameterDeg { get; set; } = 7f;

    /// <summary>Warm white, matching the Sun light.</summary>
    [Export] public Color Tint { get; set; } = new(1f, 0.96f, 0.88f);

    [Export] public float Strength { get; set; } = 1f;

    public override void _Ready()
    {
        _material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            BillboardKeepScale = true,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            DisableReceiveShadows = true,
            DisableFog = true,
            AlbedoColor = Tint,
            AlbedoTexture = GlowTexture(),
        };
        _billboard = new MeshInstance3D
        {
            Name = "Billboard",
            Mesh = new QuadMesh { Size = Vector2.One },
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = SpacecraftConventions.BackdropLayers,
            Visible = false,
        };
        AddChild(_billboard);
    }

    /// <summary>
    /// Places the glow along <paramref name="sunDirection"/> (display frame, unit) and fades it by
    /// <paramref name="visibility01"/> (0 eclipsed, 1 full sunlight). Call once per frame.
    /// </summary>
    public void Apply(Vector3 sunDirection, float visibility01)
    {
        if (_billboard is null || sunDirection == Vector3.Zero) return;
        var size = 2f * DistanceM * Mathf.Tan(Mathf.DegToRad(GlowDiameterDeg) * 0.5f);
        _billboard.Transform = new Transform3D(Basis.Identity.Scaled(new Vector3(size, size, 1f)), sunDirection * DistanceM);

        var alpha = Mathf.Clamp(visibility01, 0f, 1f) * Strength;
        _billboard.Visible = alpha > 1e-3f;
        if (Mathf.Abs(alpha - _shownAlpha) < 1e-3f) return;
        _shownAlpha = alpha;
        _material.AlbedoColor = new Color(Tint, Mathf.Clamp(alpha, 0f, 1f));
    }

    // Bright disc, then a quick warm falloff into a wide faint halo.
    private static GradientTexture2D GlowTexture()
    {
        var gradient = new Gradient();
        gradient.SetOffset(0, 0f);
        gradient.SetColor(0, Colors.White);
        gradient.SetOffset(1, 1f);
        gradient.SetColor(1, new Color(1f, 0.85f, 0.6f, 0f));
        gradient.AddPoint(0.16f, Colors.White);
        gradient.AddPoint(0.24f, new Color(1f, 0.93f, 0.78f, 0.45f));
        gradient.AddPoint(0.5f, new Color(1f, 0.88f, 0.68f, 0.12f));
        return new GradientTexture2D
        {
            Gradient = gradient,
            Width = 128,
            Height = 128,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(0.5f, 0f),
        };
    }
}
