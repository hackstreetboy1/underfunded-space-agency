using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Flight;

/// <summary>Point-mass equations of motion about one body: gravity, thrust and drag, integrated with RK4.</summary>
internal static class FlightDynamics
{
    /// <summary>Advances <paramref name="state"/> and <paramref name="massKg"/> by one RK4 step of <paramref name="dt"/> seconds.</summary>
    public static void Rk4Step(
        CelestialBody body, double ballisticCoefficient, in ThrustSample thrust, double dt,
        ref StateVector state, ref double massKg)
    {
        var force = thrust.ForceN;
        var massFlow = thrust.MassFlowKgPerS;
        var r0 = state.PositionM;
        var v0 = state.VelocityMps;
        var m0 = massKg;
        var mid = m0 - massFlow * dt / 2;
        var end = m0 - massFlow * dt;

        var a1 = Acceleration(body, ballisticCoefficient, force, r0, v0, m0);
        var r2 = r0 + v0 * (dt / 2);
        var v2 = v0 + a1 * (dt / 2);
        var a2 = Acceleration(body, ballisticCoefficient, force, r2, v2, mid);
        var r3 = r0 + v2 * (dt / 2);
        var v3 = v0 + a2 * (dt / 2);
        var a3 = Acceleration(body, ballisticCoefficient, force, r3, v3, mid);
        var r4 = r0 + v3 * dt;
        var v4 = v0 + a3 * dt;
        var a4 = Acceleration(body, ballisticCoefficient, force, r4, v4, end);

        state = new StateVector(
            r0 + (v0 + 2 * v2 + 2 * v3 + v4) * (dt / 6),
            v0 + (a1 + 2 * a2 + 2 * a3 + a4) * (dt / 6));
        massKg = end;
    }

    public static Vec2d Acceleration(CelestialBody body, double ballisticCoefficient, Vec2d forceN, Vec2d r, Vec2d v, double massKg)
    {
        var radiusSquared = r.LengthSquared;
        var radius = Math.Sqrt(radiusSquared);
        var gravity = r * (-body.MuM3PerS2 / (radiusSquared * radius));
        return gravity + NonGravitational(body, ballisticCoefficient, forceN, r, v, massKg);
    }

    /// <summary>Thrust plus drag per unit mass: what an accelerometer reads (the g-load).</summary>
    public static Vec2d NonGravitational(CelestialBody body, double ballisticCoefficient, Vec2d forceN, Vec2d r, Vec2d v, double massKg)
    {
        var acceleration = forceN.X != 0 || forceN.Y != 0 ? forceN / massKg : Vec2d.Zero;
        if (body.Atmosphere is { } atmosphere)
        {
            var density = atmosphere.DensityAt(r.Length - body.RadiusM);
            if (density > 0)
            {
                var air = v - body.SurfaceVelocityAt(r);
                acceleration += air * (-0.5 * density * air.Length / ballisticCoefficient);
            }
        }
        return acceleration;
    }

    /// <summary>Sutton-Graves style stagnation heating proxy √ρ |v|³ (nose radius and constant omitted).</summary>
    public static double HeatingProxy(CelestialBody body, in StateVector state)
    {
        if (body.Atmosphere is not { } atmosphere) return 0;
        var density = atmosphere.DensityAt(state.RadiusM - body.RadiusM);
        if (density <= 0) return 0;
        var airspeed = body.AirVelocityOf(state).Length;
        return Math.Sqrt(density) * airspeed * airspeed * airspeed;
    }
}
