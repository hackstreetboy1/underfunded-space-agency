using Godot;
using Usa.Simulation.Flight;

namespace Usa.Game.UI.Map;

/// <summary>
/// The 16 px direction icon on the planner's icon-only chips (Gate 4 decision 5; Figma icons Prograde, Retrograde,
/// Radial in, Radial out in node 30:2309), drawn as vectors until the SVG set is exported. 2 px strokes, round shapes.
/// </summary>
public sealed partial class DirectionGlyph : Control
{
    private BurnDirection _direction = BurnDirection.Prograde;
    private Color _colour = Colors.White;

    [Export]
    public BurnDirection Direction
    {
        get => _direction;
        set
        {
            _direction = value;
            QueueRedraw();
        }
    }

    public void SetColour(Color colour)
    {
        if (_colour == colour) return;
        _colour = colour;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var centre = Size / 2;
        const float ring = 5;
        DrawArc(centre, ring, 0, Mathf.Tau, 24, _colour, 2, true);
        switch (_direction)
        {
            case BurnDirection.Prograde:
                DrawCircle(centre, 1.5f, _colour);
                Ticks(centre, ring, ring + 3, [-Mathf.Pi / 2, 0, Mathf.Pi]);
                break;
            case BurnDirection.Retrograde:
                DrawLine(centre + new Vector2(-3, -3), centre + new Vector2(3, 3), _colour, 2, true);
                DrawLine(centre + new Vector2(-3, 3), centre + new Vector2(3, -3), _colour, 2, true);
                Ticks(centre, ring, ring + 3, [Mathf.Pi / 2, Mathf.Pi / 4 * 3, Mathf.Pi / 4]);
                break;
            case BurnDirection.RadialIn:
                Ticks(centre, 1, ring - 1, [Mathf.Pi / 4, Mathf.Pi / 4 * 3, -Mathf.Pi / 4, -Mathf.Pi / 4 * 3]);
                break;
            case BurnDirection.RadialOut:
                DrawCircle(centre, 1.5f, _colour);
                Ticks(centre, ring, ring + 3, [0, Mathf.Pi / 2, Mathf.Pi, -Mathf.Pi / 2]);
                break;
            default:
                DrawLine(centre + new Vector2(0, -ring), centre + new Vector2(0, ring), _colour, 2, true);
                break;
        }
    }

    private void Ticks(Vector2 centre, float from, float to, float[] angles)
    {
        foreach (var angle in angles)
        {
            var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            DrawLine(centre + (direction * from), centre + (direction * to), _colour, 2, true);
        }
    }
}
