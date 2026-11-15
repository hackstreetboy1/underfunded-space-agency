using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Flight;

/// <summary>Position and velocity relative to the centre of the dominant body, in its inertial plane.</summary>
public readonly record struct StateVector(Vec2d PositionM, Vec2d VelocityMps)
{
    public double RadiusM => PositionM.Length;

    public double SpeedMps => VelocityMps.Length;

    /// <summary>+1 when moving counter-clockwise (prograde in the inertial plane), -1 when clockwise.</summary>
    public int OrbitSense => PositionM.Cross(VelocityMps) >= 0 ? 1 : -1;

    /// <summary>Angle of the velocity above the local horizontal; negative while descending.</summary>
    public double FlightPathAngleRad
    {
        get
        {
            var radius = PositionM.Length;
            var speed = VelocityMps.Length;
            return radius > 0 && speed > 0 ? Math.Asin(Math.Clamp(PositionM.Dot(VelocityMps) / (radius * speed), -1, 1)) : 0;
        }
    }
}
