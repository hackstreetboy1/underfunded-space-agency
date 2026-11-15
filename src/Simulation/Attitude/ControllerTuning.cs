using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Attitude;

/// <summary>One mode's control law in SI units. <see cref="MaxSlewRadPerSec"/> is zero for pure rate damping.</summary>
internal readonly record struct RateLaw(
    double DeadbandRadPerSec,
    double SettleRadPerSec,
    double TimeConstantS,
    double PointingGainPerS,
    double MaxSlewRadPerSec);

internal sealed record ControllerTuning(RateLaw RateDamp, RateLaw AttitudeHold, double MinAxisTorqueFraction)
{
    /// <summary>
    /// Smallest rate time constant, in controller steps. One step cancels dt/τ of the rate error, so τ = 2 dt removes
    /// half of it: no overshoot even when cross-coupled thrusters double one axis's share (dt/τ = 1 is deadbeat,
    /// 2 diverges). Config below this is rejected at load, and the controller never uses less at run time.
    /// </summary>
    public const double MinTimeConstantSteps = 2;

    public static double MinTimeConstantS => MinTimeConstantSteps * SimulationClock.BaseTickMs / 1000.0;

    public static ControllerTuning FromDefinition(AttitudeConfigDefinition config)
    {
        var damp = config.RateDamp;
        var hold = config.AttitudeHold;
        return new ControllerTuning(
            new RateLaw(
                damp.DeadbandDegPerSec * Angles.DegToRad,
                damp.SettleDegPerSec * Angles.DegToRad,
                damp.RateTimeConstantS,
                0,
                0),
            new RateLaw(
                hold.DeadbandDegPerSec * Angles.DegToRad,
                hold.SettleDegPerSec * Angles.DegToRad,
                hold.RateTimeConstantS,
                hold.PointingGainPerS,
                hold.MaxSlewDegPerSec * Angles.DegToRad),
            config.MinAxisTorqueFraction);
    }
}

/// <summary>What a controller sees of its body for one step: stack-frame rates and inertia, orientation, and its own mount.</summary>
internal readonly record struct ControlFrame(
    Vec3d AngularVelocityRadPerSec,
    Vec3d InertiaKgM2,
    Quatd Orientation,
    StackMount Mount);
