using Godot;
using Usa.Game.UI.Console;

namespace Usa.Game.UI.Entry;

/// <summary>How a <see cref="LegendSwatch"/> draws its sample.</summary>
public enum LegendSwatchKind
{
    Line,
    Dashed,
    Band,
    Corridor,
    Arc,
    Cross,
    Tick,
}

/// <summary>
/// A legend sample drawn with the same token colours as the map or board it explains, instead of box-drawing
/// characters IBM Plex does not have (review 3, R3-11). The owning view passes its palette.
/// </summary>
public sealed partial class LegendSwatch : Control
{
    private Color _colour = Colors.White;
    private Color _edge = Colors.White;

    [Export]
    public LegendSwatchKind Kind { get; set; }

    /// <summary>Colour token (UI.md 2.1), e.g. <c>info</c> or <c>info-ghost</c>.</summary>
    [Export]
    public string Token { get; set; } = "text.mid";

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetPalette(ConsolePalette.Default);
    }

    public void SetPalette(ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _colour = palette.Of(Token);
        _edge = palette.Of("caution");
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        var midY = size.Y / 2;
        switch (Kind)
        {
            case LegendSwatchKind.Line:
                DrawLine(new Vector2(1, midY), new Vector2(size.X - 1, midY), _colour, 2, true);
                break;
            case LegendSwatchKind.Dashed:
                DrawDashedLine(new Vector2(1, midY), new Vector2(size.X - 1, midY), _colour, 2, 4, true, true);
                break;
            case LegendSwatchKind.Band:
                DrawRect(new Rect2(0, midY - 4, size.X, 8), _colour with { A = 0.35f });
                break;
            case LegendSwatchKind.Corridor:
                DrawRect(new Rect2(0, midY - 3, size.X, 6), _colour with { A = 0.6f });
                DrawLine(new Vector2(0, midY - 3), new Vector2(size.X, midY - 3), _edge, 1);
                DrawLine(new Vector2(0, midY + 3), new Vector2(size.X, midY + 3), _edge, 1);
                break;
            case LegendSwatchKind.Arc:
                DrawArc(new Vector2(size.X / 2, size.Y + 2), size.X / 2 - 1, Mathf.Pi * 1.15f, Mathf.Pi * 1.85f, 12, _colour, 3, true);
                break;
            case LegendSwatchKind.Cross:
                var half = Math.Min(size.X, size.Y) / 2 - 1;
                var centre = size / 2;
                DrawLine(centre + new Vector2(-half, -half), centre + new Vector2(half, half), _colour, 2, true);
                DrawLine(centre + new Vector2(-half, half), centre + new Vector2(half, -half), _colour, 2, true);
                break;
            default:
                DrawLine(new Vector2(size.X / 2, 1), new Vector2(size.X / 2, size.Y - 1), _colour, 2, true);
                break;
        }
    }
}
