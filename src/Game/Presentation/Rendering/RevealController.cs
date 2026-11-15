using Godot;

namespace Usa.Game.Presentation.Rendering;

/// <summary>
/// Plays the VFX-03 grey-to-colour reveal (GDD 4.4: 0.6 s; reduced motion: 0.2 s crossfade) by putting a
/// per-reveal copy of <c>reveal_transition.tres</c> in the surfaces' override slot and tweening its progress.
/// One per spacecraft view; presenters call <see cref="Play"/>.
/// </summary>
public sealed partial class RevealController : Node
{
    private static readonly StringName ProgressParam = "reveal_progress";
    private static readonly StringName OriginParam = "reveal_origin";
    private static readonly StringName RadiusParam = "reveal_radius_m";
    private static readonly StringName ReducedMotionParam = "reduced_motion";

    [Export] public ShaderMaterial? Template { get; set; }
    [Export] public double DurationSec { get; set; } = 0.6;
    [Export] public double ReducedMotionDurationSec { get; set; } = 0.2;

    /// <summary>Accessibility setting: a short crossfade instead of the moving dissolve front.</summary>
    [Export] public bool ReducedMotion { get; set; }

    public override void _Ready() =>
        Template ??= GD.Load<ShaderMaterial>(RenderingConventions.RevealMaterialPath);

    /// <summary>
    /// Starts a reveal on <paramref name="surfaces"/>, spreading from <paramref name="originWorld"/> until it
    /// covers <paramref name="radiusM"/>. <paramref name="onFinished"/> runs at the end, not when the returned
    /// tween is killed; the caller then sets the final materials itself.
    /// </summary>
    public Tween Play(IReadOnlyList<PaletteSurface> surfaces, Vector3 originWorld, float radiusM, Action onFinished)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(onFinished);
        var template = Template ?? throw new InvalidOperationException($"{nameof(RevealController)} has no reveal material.");

        var material = (ShaderMaterial)template.Duplicate();
        material.SetShaderParameter(OriginParam, originWorld);
        material.SetShaderParameter(RadiusParam, radiusM);
        material.SetShaderParameter(ReducedMotionParam, ReducedMotion);
        material.SetShaderParameter(ProgressParam, 0f);
        foreach (var surface in surfaces) surface.SetOverride(material);

        var tween = CreateTween();
        tween.TweenMethod(
            Callable.From<float>(progress => material.SetShaderParameter(ProgressParam, progress)),
            0f, 1f, ReducedMotion ? ReducedMotionDurationSec : DurationSec);
        tween.Finished += onFinished;
        return tween;
    }
}
