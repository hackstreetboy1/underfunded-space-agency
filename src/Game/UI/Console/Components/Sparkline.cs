using Godot;
using Usa.Game.Presentation.Console.Resources;
using Usa.Game.Presentation.Console.Status;

namespace Usa.Game.UI.Console.Components;

/// <summary>
/// A resource tile's 60-minute sparkline (<c>Sparkline.tscn</c>, custom <c>_Draw</c>; UI.md 3.2 and 9, Figma G1-02 "Sparkline"
/// Falling / Rising / Flat / No data): one point per MET minute, gaps where the value was not known, and a dot on the newest
/// sample. With no data it draws a dashed baseline. Redraws only when the series changes (once a MET minute at most).
/// </summary>
public sealed partial class Sparkline : Control
{
    private const float LineWidth = 1.5f;
    private const float DotRadius = 2f;
    private const float DashPx = 3f;

    private SparklineSeries _series = SparklineSeries.Empty;
    private ConsolePalette _palette = ConsolePalette.Default;

    public void Show(SparklineSeries series, ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(palette);
        _series = series;
        _palette = palette;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var samples = _series.Samples;
        var size = Size;
        if (size.X <= 0 || size.Y <= 0) return;
        if (_series.Min is not { } min || _series.Max is not { } max)
        {
            DrawDashedLine(new Vector2(0, size.Y / 2), new Vector2(size.X, size.Y / 2), _palette.Of(ColourTokens.TextOff), 1f, DashPx);
            return;
        }

        var line = _palette.Of(ColourTokens.TextMid);
        var step = samples.Count > 1 ? size.X / (SparklineSampler.Capacity - 1) : 0;
        // Right-aligned: the newest sample sits at the right edge, older minutes to its left.
        var x0 = size.X - step * (samples.Count - 1);
        Vector2? previous = null;
        Vector2? last = null;
        for (var i = 0; i < samples.Count; i++)
        {
            if (samples[i] is not { } value)
            {
                previous = null;
                continue;
            }
            var t = max > min ? (float)((value - min) / (max - min)) : 0.5f;
            var point = new Vector2(x0 + step * i, size.Y - DotRadius - t * (size.Y - 2 * DotRadius));
            if (previous is { } from) DrawLine(from, point, line, LineWidth, antialiased: true);
            previous = point;
            last = point;
        }
        if (last is { } newest) DrawCircle(newest, DotRadius, _palette.Of(ColourTokens.TextHi));
    }
}
