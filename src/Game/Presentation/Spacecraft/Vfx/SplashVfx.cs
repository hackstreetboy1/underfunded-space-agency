using Godot;

namespace Usa.Game.Presentation.Spacecraft.Vfx;

/// <summary>
/// VFX-02c splashdown burst at the origin (the waterline): one GPU particle spray (at most <see cref="MaxParticles"/>, fewer
/// and slower with reduced motion) and an expanding foam ring. One-shot: <see cref="Play"/> fires it once per splashdown.
/// Built in code so the scene needs only this node. Holds its look while frozen.
/// </summary>
public sealed partial class SplashVfx : Node3D
{
    public const int MaxParticles = 240;
    private const int ReducedParticles = 90;
    private const float FoamHeightM = 0.42f; // above the highest swell of the ocean shader
    private const string FoamShaderPath = "res://assets/vfx/foam_ring.gdshader";

    private GpuParticles3D? _spray;
    private MeshInstance3D? _foam;
    private ShaderMaterial? _foamMaterial;
    private double _foamAgeSec = double.PositiveInfinity;
    private double _foamDurationSec = 2.5;
    private bool _frozen;

    [Export] public float FoamSizeM { get; set; } = 14f;

    public bool IsPlaying => _foamAgeSec < _foamDurationSec;

    public override void _Ready()
    {
        _spray = BuildSpray();
        AddChild(_spray);
        _foamMaterial = new ShaderMaterial { Shader = GD.Load<Shader>(FoamShaderPath), RenderPriority = 1 };
        _foamMaterial.SetShaderParameter("progress", 1f);
        _foam = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(FoamSizeM, FoamSizeM) },
            MaterialOverride = _foamMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = new Vector3(0f, FoamHeightM, 0f),
            Visible = false,
        };
        AddChild(_foam);
        SetProcess(false);
    }

    /// <summary>Fires the burst once. Reduced motion: fewer, slower particles and a slower ring, no sudden pop.</summary>
    public void Play(bool reducedMotion)
    {
        if (_spray is null || _foam is null) return;
        _spray.Amount = reducedMotion ? ReducedParticles : MaxParticles;
        if (_spray.ProcessMaterial is ParticleProcessMaterial material)
        {
            material.InitialVelocityMin = reducedMotion ? 1.5f : 3f;
            material.InitialVelocityMax = reducedMotion ? 3.5f : 7f;
        }
        _spray.Restart();
        _spray.Emitting = true;
        _foamDurationSec = reducedMotion ? 4.0 : 2.5;
        _foamAgeSec = 0;
        _foam.Visible = true;
        SetProcess(true);
    }

    /// <summary>Clears the effect (the view left the sea).</summary>
    public void Stop()
    {
        if (_spray is not null) _spray.Emitting = false;
        _foamAgeSec = double.PositiveInfinity;
        if (_foam is not null) _foam.Visible = false;
        SetProcess(false);
    }

    public void SetFrozen(bool frozen)
    {
        _frozen = frozen;
        if (_spray is not null) _spray.SpeedScale = frozen ? 0f : 1f;
    }

    public override void _Process(double delta)
    {
        if (_frozen || _foam is null || _foamMaterial is null) return;
        _foamAgeSec += delta;
        var progress = (float)Math.Clamp(_foamAgeSec / _foamDurationSec, 0.0, 1.0);
        _foamMaterial.SetShaderParameter("progress", progress);
        if (progress < 1f) return;
        _foam.Visible = false;
        SetProcess(false);
    }

    private static GpuParticles3D BuildSpray()
    {
        var puff = new Gradient { Offsets = [0f, 0.55f, 1f], Colors = [Colors.White, Colors.White, new Color(1f, 1f, 1f, 0f)] };
        var fade = new Gradient { Offsets = [0f, 0.6f, 1f], Colors = [Colors.White, new Color(1f, 1f, 1f, 0.8f), new Color(1f, 1f, 1f, 0f)] };
        var drawMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = new GradientTexture2D
            {
                Gradient = puff,
                Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.5f),
                FillTo = new Vector2(1f, 0.5f),
                Width = 64,
                Height = 64,
            },
        };
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring,
            EmissionRingAxis = Vector3.Up,
            EmissionRingRadius = 0.9f,
            EmissionRingInnerRadius = 0.5f,
            EmissionRingHeight = 0.1f,
            Direction = Vector3.Up,
            Spread = 38f,
            InitialVelocityMin = 3f,
            InitialVelocityMax = 7f,
            Gravity = new Vector3(0f, -9.8f, 0f),
            ScaleMin = 0.25f,
            ScaleMax = 0.6f,
            ColorRamp = new GradientTexture1D { Gradient = fade },
        };
        return new GpuParticles3D
        {
            Name = "Spray",
            Amount = MaxParticles,
            Lifetime = 1.6,
            OneShot = true,
            Explosiveness = 0.95f,
            Emitting = false,
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = Vector2.One, Material = drawMaterial },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new Vector3(-12f, -1f, -12f), new Vector3(24f, 14f, 24f)),
        };
    }
}
