using Godot;

namespace Usa.Game.UI.Entry;

/// <summary>
/// The recovery progress track on the splashdown card (G4-04): a line from the ship's start to the capsule ✕, filled up to
/// the recovery forces' position, with their marker at <see cref="Show"/>'s progress.
/// </summary>
public sealed partial class RecoveryTrack : Control
{
    private double _progress;
    private Color _fill = Colors.White;
    private Color _track = Colors.DimGray;
    private Color _marker = Colors.White;

    public void Show(double progress01, Color fill, Color track, Color marker)
    {
        _progress = double.IsFinite(progress01) ? Math.Clamp(progress01, 0, 1) : 0;
        _fill = fill;
        _track = track;
        _marker = marker;
        QueueRedraw();
    }

    public override void _Draw()
    {
        const float capsule = 6;
        var y = Size.Y / 2;
        var end = Size.X - capsule - 2;
        var x = (float)(end * _progress);
        DrawLine(new Vector2(0, y), new Vector2(end, y), _track, 3);
        DrawLine(new Vector2(0, y), new Vector2(x, y), _fill, 3);
        DrawLine(new Vector2(end - capsule, y - capsule), new Vector2(end + capsule, y + capsule), _marker, 2, true);
        DrawLine(new Vector2(end - capsule, y + capsule), new Vector2(end + capsule, y - capsule), _marker, 2, true);
        Vector2[] hull = [new(x - 7, y - 4), new(x + 7, y - 4), new(x + 4, y + 2), new(x - 4, y + 2)];
        DrawColoredPolygon(hull, _marker);
        DrawLine(new Vector2(x, y - 4), new Vector2(x, y - 10), _marker, 2);
    }
}
