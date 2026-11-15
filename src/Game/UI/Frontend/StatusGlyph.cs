using Godot;
using Usa.Game.UI.Frontend.Model;

namespace Usa.Game.UI.Frontend;

/// <summary>
/// The status shape beside a debrief objective (UI.md 2.2, never colour alone): filled circle (ok), triangle (caution),
/// octagon (critical), hollow circle (neutral). Drawn, so no font needs the glyphs.
/// </summary>
public sealed partial class StatusGlyph : Control
{
    private DebriefTone _tone = DebriefTone.Neutral;
    private Color _colour = Colors.White;

    public static StatusGlyph Create(DebriefTone tone, Color colour, int sizePx = 12)
    {
        var glyph = new StatusGlyph
        {
            CustomMinimumSize = new Vector2(sizePx, sizePx),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        glyph.Set(tone, colour);
        return glyph;
    }

    public void Set(DebriefTone tone, Color colour)
    {
        _tone = tone;
        _colour = colour;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Mathf.Min(Size.X, Size.Y);
        var centre = Size / 2f;
        var radius = size / 2f;
        switch (_tone)
        {
            case DebriefTone.Ok:
                DrawCircle(centre, radius, _colour);
                break;
            case DebriefTone.Caution:
                DrawColoredPolygon([new Vector2(centre.X, centre.Y - radius), new Vector2(centre.X + radius, centre.Y + radius), new Vector2(centre.X - radius, centre.Y + radius)], _colour);
                break;
            case DebriefTone.Critical:
                var points = new Vector2[8];
                for (var i = 0; i < 8; i++)
                {
                    var angle = Mathf.Pi / 8f + i * Mathf.Pi / 4f;
                    points[i] = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                }
                DrawColoredPolygon(points, _colour);
                break;
            default:
                DrawArc(centre, radius - 1f, 0f, Mathf.Tau, 24, _colour, 1.5f, antialiased: true);
                break;
        }
    }
}
