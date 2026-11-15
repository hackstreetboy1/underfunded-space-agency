using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Attitude;

/// <summary>In-plane pointing geometry. The orbital plane is the inertial X-Y plane; angles are measured from +X towards +Y.</summary>
internal static class PointingMath
{
    /// <summary>
    /// Sun pitch in degrees, (-180, 180]: <c>wrap(pointing_deg − sun_deg − 90)</c>, 0 when the X axis leads the Sun line by 90°
    /// (the body −Y side faces the Sun: arrays square to it, M2 §16.2 <c>attitude.&lt;v&gt;.sun_pitch_deg</c>).
    /// </summary>
    public static double SunPitchDeg(double pointingDeg, double sunInertialDeg) =>
        Angles.Wrap((pointingDeg - sunInertialDeg - 90) * Angles.DegToRad) * Angles.RadToDeg;

    public static Vec3d InPlaneDirection(double angleRad) => new(Math.Cos(angleRad), Math.Sin(angleRad), 0);

    /// <summary>Angle of the axis projected onto the orbital plane, [0, 2π).</summary>
    public static double InPlaneAngleRad(Vec3d axisInertial) => Angles.WrapPositive(Math.Atan2(axisInertial.Y, axisInertial.X));

    /// <summary>Elevation of a unit axis above the orbital plane, [-π/2, π/2].</summary>
    public static double OutOfPlaneRad(Vec3d axisInertial) => Math.Asin(Math.Clamp(axisInertial.Z, -1.0, 1.0));

    /// <summary>Unsigned angle between two vectors, [0, π]; robust near 0 and π.</summary>
    public static double AngleBetweenRad(Vec3d a, Vec3d b) => Math.Atan2(a.Cross(b).Length, a.Dot(b));
}
