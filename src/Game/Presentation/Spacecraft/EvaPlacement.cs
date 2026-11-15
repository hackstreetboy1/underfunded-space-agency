using Godot;

namespace Usa.Game.Presentation.Spacecraft;

/// <summary>Placement and cable maths for the spacewalk, kept apart from the scene code.</summary>
public static class EvaPlacement
{
    /// <summary>Unit direction pointing out of the hull from the capsule axis (+X) through the hatch.</summary>
    public static Vector3 Outward(Vector3 hatchLocal)
    {
        var radial = new Vector3(0, hatchLocal.Y, hatchLocal.Z);
        return radial.LengthSquared() < 1e-8f ? Vector3.Up : radial.Normalized();
    }

    /// <summary>A point on the sagging cable: a quadratic Bézier whose middle control hangs below the chord.</summary>
    public static Vector3 CablePoint(Vector3 start, Vector3 end, float sagM, float t)
    {
        var control = (start + end) * 0.5f + Vector3.Down * (sagM * 2f);
        var u = 1f - t;
        return u * u * start + 2f * u * t * control + t * t * end;
    }
}
