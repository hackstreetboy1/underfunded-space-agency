using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Flight;

/// <summary>Thrust supplied by the caller for one step: the vehicle's engines and where they point.</summary>
/// <param name="PointingAngleRad">Commanded inertial in-plane thrust direction.</param>
/// <param name="AttitudeErrorRad">Deviation of the real thrust axis from the commanded one (from the attitude model).</param>
public readonly record struct PoweredInput(double ThrustN, double IspS, double PointingAngleRad, double AttitudeErrorRad)
{
    public static PoweredInput Idle => default;

    public bool IsFiring => ThrustN > 0 && IspS > 0;

    public ThrustSample ToSample() => IsFiring
        ? new ThrustSample(ThrustN, ThrustN / (IspS * PhysicalConstants.StandardGravity), PointingAngleRad + AttitudeErrorRad)
        : ThrustSample.None;
}

/// <summary>Thrust applied over one integration step.</summary>
/// <param name="DirectionRad">Inertial in-plane direction of the thrust vector, attitude error included.</param>
/// <param name="ProgrammedMassFlowKgPerS">Part of <paramref name="MassFlowKgPerS"/> from a burn flight programmed itself (retro sequence, planned burn).</param>
public readonly record struct ThrustSample(double ThrustN, double MassFlowKgPerS, double DirectionRad, double ProgrammedMassFlowKgPerS = 0)
{
    public static readonly ThrustSample None = new(0, 0, 0);

    public Vec2d ForceN => ThrustN > 0 ? Vec2d.FromPolar(ThrustN, DirectionRad) : Vec2d.Zero;

    /// <summary>Vector sum of two thrusts; mass flows add.</summary>
    public static ThrustSample Combine(ThrustSample a, ThrustSample b)
    {
        if (a.ThrustN <= 0) return b;
        if (b.ThrustN <= 0) return a;
        var force = a.ForceN + b.ForceN;
        return new ThrustSample(
            force.Length, a.MassFlowKgPerS + b.MassFlowKgPerS, force.Angle, a.ProgrammedMassFlowKgPerS + b.ProgrammedMassFlowKgPerS);
    }
}
