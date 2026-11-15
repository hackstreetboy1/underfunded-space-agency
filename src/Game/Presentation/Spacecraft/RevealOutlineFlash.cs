using Godot;
using Usa.Game.Presentation.Rendering;

namespace Usa.Game.Presentation.Spacecraft;

/// <summary>
/// The subtle outline flash on a newly revealed part (UI 4.4): a 2 px screen-space outline in the <c>info</c> colour
/// with a faint fresnel rim, rising quickly and fading out. It rides in each mesh's <c>material_overlay</c> slot, so the
/// knowledge look (surface overrides) and the VFX-03 reveal underneath are untouched, and it never moves the camera.
/// Reduced motion: no rim pulse, a steady outline that fades over <see cref="ReducedMotionFadeSec"/>.
/// </summary>
public sealed partial class RevealOutlineFlash : Node
{
    private static readonly StringName ColorParam = "flash_color";
    private static readonly StringName WidthParam = "outline_px";
    private static readonly StringName RimPowerParam = "rim_power";
    private static readonly StringName RimStrengthParam = "rim_strength";
    private static readonly StringName IntensityParam = "intensity";

    private readonly Dictionary<Node3D, Flash> _active = [];
    private Shader? _outlineShader;
    private Shader? _rimShader;

    /// <summary>UI token <c>info</c>.</summary>
    [Export] public Color InfoColor { get; set; } = new("#5FB3D9");

    [Export] public float OutlineWidthPx { get; set; } = 2f;
    [Export] public float RimPower { get; set; } = 3f;

    /// <summary>Peak strength of the additive fresnel rim; kept low so the flash stays subtle.</summary>
    [Export] public float RimStrength { get; set; } = 0.35f;

    [Export] public double RiseSec { get; set; } = 0.1;
    [Export] public double HoldSec { get; set; } = 0.3;
    [Export] public double FadeSec { get; set; } = 0.6;
    [Export] public double ReducedMotionHoldSec { get; set; } = 0.5;
    [Export] public double ReducedMotionFadeSec { get; set; } = 0.2;

    /// <summary>Accessibility setting (UI 2.5): no pulse, a steady outline only.</summary>
    [Export] public bool ReducedMotion { get; set; }

    /// <summary>Components with a flash running.</summary>
    public int ActiveCount => _active.Count;

    public override void _Ready()
    {
        _outlineShader = new Shader { Code = OutlineShaderCode };
        _rimShader = new Shader { Code = RimShaderCode };
    }

    /// <summary>
    /// Flashes every mesh of <paramref name="component"/> (the node with <c>component_id</c>), its damage variants
    /// included and nested components excluded. A flash already running on it restarts.
    /// </summary>
    public void Play(Node3D component)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (_outlineShader is null || _rimShader is null) return;
        Stop(component);

        var meshes = new List<GeometryInstance3D>();
        CollectMeshes(component, isRoot: true, meshes);
        if (meshes.Count == 0) return;

        var rim = new ShaderMaterial { Shader = _rimShader };
        rim.SetShaderParameter(ColorParam, InfoColor);
        rim.SetShaderParameter(RimPowerParam, RimPower);
        rim.SetShaderParameter(RimStrengthParam, ReducedMotion ? 0f : RimStrength);
        var outline = new ShaderMaterial { Shader = _outlineShader, NextPass = rim };
        outline.SetShaderParameter(ColorParam, InfoColor);
        outline.SetShaderParameter(WidthParam, OutlineWidthPx);

        var flash = new Flash(outline, rim);
        foreach (var mesh in meshes)
        {
            // Another effect owns this slot; leave it alone rather than stacking overlays.
            if (mesh.MaterialOverlay is not null) continue;
            mesh.MaterialOverlay = outline;
            flash.Meshes.Add(mesh);
        }
        if (flash.Meshes.Count == 0) return;

        SetIntensity(flash, ReducedMotion ? 1f : 0f);
        var tween = CreateTween();
        var set = Callable.From<float>(value => SetIntensity(flash, value));
        if (ReducedMotion)
        {
            tween.TweenInterval(ReducedMotionHoldSec);
            tween.TweenMethod(set, 1f, 0f, ReducedMotionFadeSec);
        }
        else
        {
            tween.TweenMethod(set, 0f, 1f, RiseSec).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
            tween.TweenInterval(HoldSec);
            tween.TweenMethod(set, 1f, 0f, FadeSec).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        }
        tween.Finished += () => Stop(component);
        flash.Tween = tween;
        _active[component] = flash;
    }

    /// <summary>Ends a running flash at once and gives the overlay slots back.</summary>
    public void Stop(Node3D component)
    {
        if (!_active.Remove(component, out var flash)) return;
        if (flash.Tween is { } tween && tween.IsValid()) tween.Kill();
        foreach (var mesh in flash.Meshes)
        {
            if (IsInstanceValid(mesh) && ReferenceEquals(mesh.MaterialOverlay, flash.Outline)) mesh.MaterialOverlay = null;
        }
    }

    public void StopAll()
    {
        foreach (var component in _active.Keys.ToArray()) Stop(component);
    }

    public override void _ExitTree() => StopAll();

    private static void SetIntensity(Flash flash, float value)
    {
        flash.Outline.SetShaderParameter(IntensityParam, value);
        flash.Rim.SetShaderParameter(IntensityParam, value);
    }

    private static void CollectMeshes(Node node, bool isRoot, List<GeometryInstance3D> into)
    {
        if (node is ComponentPresenter || node.IsInGroup(SpacecraftConventions.VfxGroup)) return;
        if (!isRoot && node.HasMeta(RenderingConventions.ComponentIdMeta)) return;
        if (node is MeshInstance3D mesh) into.Add(mesh);
        foreach (var child in node.GetChildren()) CollectMeshes(child, isRoot: false, into);
    }

    private sealed class Flash(ShaderMaterial outline, ShaderMaterial rim)
    {
        public ShaderMaterial Outline { get; } = outline;
        public ShaderMaterial Rim { get; } = rim;
        public List<GeometryInstance3D> Meshes { get; } = [];
        public Tween? Tween { get; set; }
    }

    // Inverted hull pushed out along the screen-space normal by a fixed pixel width, so the outline stays 2 px at
    // every zoom; only its silhouette shows behind the front faces.
    private const string OutlineShaderCode = """
        shader_type spatial;
        render_mode unshaded, cull_front, depth_draw_never, blend_mix, shadows_disabled, fog_disabled;

        uniform vec4 flash_color : source_color = vec4(0.373, 0.702, 0.851, 1.0);
        uniform float outline_px = 2.0;
        uniform float intensity : hint_range(0.0, 1.0) = 0.0;

        void vertex() {
            vec4 clip = PROJECTION_MATRIX * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0));
            vec3 view_normal = mat3(MODELVIEW_MATRIX) * NORMAL;
            vec2 screen_dir = (PROJECTION_MATRIX * vec4(view_normal, 0.0)).xy;
            float len = length(screen_dir);
            if (len > 1e-6) {
                clip.xy += screen_dir / len * outline_px * 2.0 / VIEWPORT_SIZE * clip.w;
            }
            POSITION = clip;
        }

        void fragment() {
            ALBEDO = flash_color.rgb;
            ALPHA = flash_color.a * intensity;
        }
        """;

    private const string RimShaderCode = """
        shader_type spatial;
        render_mode unshaded, cull_back, depth_draw_never, blend_add, shadows_disabled, fog_disabled;

        uniform vec4 flash_color : source_color = vec4(0.373, 0.702, 0.851, 1.0);
        uniform float rim_power = 3.0;
        uniform float rim_strength = 0.35;
        uniform float intensity : hint_range(0.0, 1.0) = 0.0;

        void fragment() {
            float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), rim_power);
            ALBEDO = flash_color.rgb;
            ALPHA = rim * rim_strength * intensity;
        }
        """;
}
