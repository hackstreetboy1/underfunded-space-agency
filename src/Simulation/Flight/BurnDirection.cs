using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Flight;

/// <summary>Burn direction presets in the local orbital frame.</summary>
public enum BurnDirection
{
    Prograde,
    Retrograde,
    RadialIn,
    RadialOut,

    /// <summary>A free angle measured from prograde towards radial-out.</summary>
    Custom,
}

/// <summary>Converts local burn directions into inertial in-plane pointing angles.</summary>
public static class BurnFrames
{
    /// <summary>
    /// Offset from prograde towards radial-out: prograde 0, radial-out π/2, retrograde π, radial-in -π/2.
    /// "Radial" is perpendicular to the velocity in the orbit plane.
    /// </summary>
    public static double LocalAngleRad(BurnDirection direction, double customAngleRad) => direction switch
    {
        BurnDirection.Prograde => 0,
        BurnDirection.RadialOut => Math.PI / 2,
        BurnDirection.Retrograde => Math.PI,
        BurnDirection.RadialIn => -Math.PI / 2,
        BurnDirection.Custom => customAngleRad,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
    };

    /// <summary>Inertial pointing angle for a local angle (prograde = 0, radial-out = +π/2).</summary>
    public static double InertialAngleRad(double localAngleRad, in StateVector state)
    {
        var sense = state.OrbitSense;
        return Angles.Wrap(state.VelocityMps.Angle - sense * localAngleRad);
    }

    public static double InertialAngleRad(BurnDirection direction, double customAngleRad, in StateVector state) =>
        InertialAngleRad(LocalAngleRad(direction, customAngleRad), state);

    /// <summary>Converts a vector given as (prograde, radial-out) components into the inertial frame.</summary>
    public static Vec2d LocalToInertial(Vec2d local, in StateVector state)
    {
        var sense = state.OrbitSense;
        var prograde = state.VelocityMps.Normalized();
        var radialOut = prograde.Rotated(-sense * Math.PI / 2);
        return local.X * prograde + local.Y * radialOut;
    }
}
