using Godot;

namespace Usa.Game.UI.Entry;

/// <summary>A horizontal bar with tolerance marks (the g-load gauge and the heating bar, G4-03; crew health mini-bars).</summary>
public sealed partial class HudBarGauge : Control
{
    private const float TrackHeight = 6;

    private double _fraction;
    private Color _fill = Colors.White;
    private Color _track = Colors.DimGray;
    private IReadOnlyList<(double Fraction, Color Colour, string Label)> _marks = [];

    public void Show(double fraction01, Color fill, Color track, IReadOnlyList<(double Fraction, Color Colour, string Label)>? marks = null)
    {
        _fraction = double.IsFinite(fraction01) ? Math.Clamp(fraction01, 0, 1) : 0;
        _fill = fill;
        _track = track;
        _marks = marks ?? [];
        QueueRedraw();
    }

    public override void _Draw()
    {
        var top = Size.Y - TrackHeight;
        DrawRect(new Rect2(0, top, Size.X, TrackHeight), _track);
        DrawRect(new Rect2(0, top, (float)(Size.X * _fraction), TrackHeight), _fill);
        if (_marks.Count == 0) return;
        var font = GetThemeFont("font", "Meta");
        var fontSize = GetThemeFontSize("font_size", "Meta");
        foreach (var (fraction, colour, text) in _marks)
        {
            var x = Mathf.Round((float)(Size.X * Math.Clamp(fraction, 0, 1)));
            DrawLine(new Vector2(x, top - 4), new Vector2(x, Size.Y), colour, 2);
            if (text.Length == 0) continue;
            var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize).X;
            DrawString(font, new Vector2(Math.Clamp(x - (width / 2), 0, Math.Max(0, Size.X - width)), top - 6), text,
                HorizontalAlignment.Left, -1, fontSize, colour);
        }
    }
}
