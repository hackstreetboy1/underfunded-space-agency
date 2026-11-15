using Godot;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.UI.Console.Binding;

namespace Usa.Game.UI.Console.Components;

/// <summary>
/// The crew card's speaking indicator (UI.md 3.4): three small bars drawn in the accent colour, bottom-aligned, animated from
/// the speech player's output level for that crew member (<see cref="SpeakingMeterModel"/>). It processes only while shown
/// and moving; with Reduced motion, or without a level source, it draws a still shape.
/// </summary>
public sealed partial class SpeakingMeter : Control
{
    private const float GapPx = 2f;

    private readonly SpeakingMeterModel _model = new();
    private Func<float>? _level;
    private ConsolePalette _palette = ConsolePalette.Default;
    private bool _speaking;
    private bool _reducedMotion;

    public override void _Ready() => Refresh();

    // Presentation only: sample the playback level and redraw.
    public override void _Process(double delta)
    {
        _model.Advance(delta, _level?.Invoke() ?? 0f);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        if (size.X <= 0 || size.Y <= 0) return;
        var colour = _palette.Of(ColourTokens.Accent);
        var width = (size.X - GapPx * (SpeakingMeterModel.BarCount - 1)) / SpeakingMeterModel.BarCount;
        if (width <= 0) return;
        for (var i = 0; i < SpeakingMeterModel.BarCount; i++)
        {
            var height = Mathf.Max(1f, Mathf.Round(size.Y * _model.Height(i, Still)));
            DrawRect(new Rect2(i * (width + GapPx), size.Y - height, width, height), colour);
        }
    }

    /// <param name="level">The speaker's output level, 0..1, read once a frame while speaking; null draws the still shape.</param>
    public void Configure(Func<float>? level, ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _level = level;
        _palette = palette;
        Refresh();
    }

    public void SetSpeaking(bool speaking)
    {
        if (speaking == _speaking) return;
        _speaking = speaking;
        _model.Reset();
        Refresh();
    }

    public void SetReducedMotion(bool reducedMotion)
    {
        if (reducedMotion == _reducedMotion) return;
        _reducedMotion = reducedMotion;
        Refresh();
    }

    private bool Still => _reducedMotion || _level is null;

    private void Refresh()
    {
        Visible = _speaking;
        SetProcess(_speaking && !Still);
        QueueRedraw();
    }
}
