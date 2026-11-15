using Godot;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Game.Presentation.Spacecraft;

/// <summary>
/// Frame conventions between the simulation and the 3D view.
/// <para>
/// <b>Body → model.</b> The simulation body frame (X nose/roll, Y pitch, Z yaw) is the models' Blender object frame
/// (MODELING 2 and <c>layouts.json</c>: nose +X, top +Z, hull angle measured from +Z towards +Y). The glTF export
/// turns Blender (x, y, z) into Godot (x, z, −y), so one mapping takes simulation vectors into a model's local
/// Godot frame:
/// </para>
/// <list type="bullet">
/// <item>body +X (nose, roll axis) → Godot +X</item>
/// <item>body +Z (yaw axis, model top) → Godot +Y</item>
/// <item>body +Y (pitch axis) → Godot −Z</item>
/// </list>
/// <para>
/// It is a proper rotation (−90° about X), so it maps a rotation's axis and keeps its angle: the body → inertial
/// quaternion (w, x, y, z) becomes (w, x, z, −y) in Godot axes. The inertial frame goes through the same mapping
/// (orbital plane inertial X-Y → Godot X-Z, orbit normal → Godot +Y); <see cref="DisplayFrame"/> then turns that so
/// nadir points straight down and prograde along −Z, which is the frame the view is drawn in.
/// </para>
/// </summary>
public static class SpacecraftAxes
{
    /// <summary>Models are built at 0.75× real size (MODELING 1.2); real distances between vehicles are scaled to match.</summary>
    public const float ToyScale = 0.75f;

    public static Vector3 ToGodot(Vec3d v) => new((float)v.X, (float)v.Z, (float)-v.Y);

    public static Quaternion ToGodot(Quatd q)
    {
        var unit = q.Normalized();
        return new Quaternion((float)unit.X, (float)unit.Z, (float)-unit.Y, (float)unit.W);
    }

    /// <summary>
    /// Rotation from Godot-mapped inertial axes to the display frame: nadir → −Y, prograde (orbit normal × zenith)
    /// → −Z. Identity when nadir is unknown. Multiply on the left of anything converted with <see cref="ToGodot(Vec3d)"/>.
    /// </summary>
    public static Basis DisplayFrame(Vec3d nadirInertial)
    {
        var down = ToGodot(nadirInertial);
        if (down.LengthSquared() < 1e-12f) return Basis.Identity;

        var up = -down.Normalized();
        // The orbit normal (inertial +Z) is Godot +Y after ToGodot.
        var forward = Vector3.Up.Cross(up);
        if (forward.LengthSquared() < 1e-6f) forward = up.Cross(Vector3.Right);
        forward = forward.Normalized();
        var right = forward.Cross(up);
        // Columns = display axes in inertial coordinates; the transpose maps inertial into display.
        return new Basis(right, up, -forward).Transposed();
    }

    /// <summary>Body → display orientation for a body → inertial quaternion.</summary>
    public static Basis ToDisplay(Basis displayFrame, Quatd bodyToInertial) =>
        displayFrame * new Basis(ToGodot(bodyToInertial));

    /// <summary>A real-metre inertial offset as a display-frame offset in toy-scale units.</summary>
    public static Vector3 ToDisplayOffset(Basis displayFrame, Vec3d inertialM) =>
        displayFrame * (ToGodot(inertialM) * ToyScale);

    public static Vector3 ToDisplayDirection(Basis displayFrame, Vec3d inertial)
    {
        var direction = displayFrame * ToGodot(inertial);
        return direction.LengthSquared() > 1e-12f ? direction.Normalized() : Vector3.Zero;
    }

    /// <summary>Advances <paramref name="orientation"/> by a constant body-frame rate for <paramref name="dtS"/>.</summary>
    public static Quatd Extrapolate(Quatd orientation, Vec3d angularVelocityRadPerSec, double dtS)
    {
        var rate = angularVelocityRadPerSec.Length;
        if (rate < 1e-9 || dtS <= 0) return orientation;
        return (orientation * Quatd.FromAxisAngle(angularVelocityRadPerSec, rate * dtS)).Normalized();
    }
}
