using Godot;

namespace Usa.Game.UI.Frontend;

/// <summary>
/// The guided first run's target ring: an <c>accent</c> outline drawn in the callout layer around the control the visible
/// callout points at (the control's own style is never touched). It pulses gently, holds still with Reduced motion, and
/// never takes input.
/// </summary>
public sealed partial class TutorialHighlight : Control
{
    private const float PadPx = 4f;
    private const float RingPx = 2f;
    private const float GlowPx = 6f;
    private const double PulsePeriodS = 1.4;

    private Color _accent = Colors.White;
    private bool _reducedMotion;
    private double _clockS;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.None;
        Visible = false;
        SetProcess(false);
    }

    public void Configure(Color accent, bool reducedMotion)
    {
        _accent = accent;
        _reducedMotion = reducedMotion;
        QueueRedraw();
    }

    /// <summary>Rings <paramref name="target"/> (callout-layer coordinates), or hides with null.</summary>
    public void Show(Rect2? target)
    {
        if (target is not { } rect)
        {
            Visible = false;
            SetProcess(false);
            return;
        }
        var bounds = rect.Grow(PadPx + GlowPx);
        if (!Visible || Position != bounds.Position || Size != bounds.Size)
        {
            Position = bounds.Position;
            Size = bounds.Size;
            QueueRedraw();
        }
        Visible = true;
        SetProcess(!_reducedMotion);
    }

    // Presentation only: advances the pulse.
    public override void _Process(double delta)
    {
        _clockS = (_clockS + delta) % PulsePeriodS;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var pulse = _reducedMotion ? 1f : 0.5f + 0.5f * Mathf.Sin((float)(_clockS / PulsePeriodS * Math.Tau));
        var ring = new Rect2(Vector2.One * GlowPx, Size - Vector2.One * GlowPx * 2f);
        var glow = _accent with { A = 0.18f + 0.22f * pulse };
        DrawRect(ring.Grow(GlowPx / 2f), glow, false, GlowPx);
        DrawRect(ring, _accent with { A = 0.75f + 0.25f * pulse }, false, RingPx);
    }
}
