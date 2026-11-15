using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Flight;

/// <summary>An engine option for a planned burn.</summary>
public abstract record BurnEngine
{
    /// <summary>Propellant the engine may use for this burn.</summary>
    public abstract double AvailablePropellantKg { get; }

    /// <summary>Set for engines that always burn the same time (solid rockets); the plan's duration is ignored.</summary>
    public abstract double? FixedDurationS { get; }

    /// <summary>Isp × g0, used to express leftover propellant as Δv.</summary>
    public abstract double ExhaustVelocityMps { get; }

    /// <summary>Thrust and mass flow at a time since ignition, ignoring propellant limits.</summary>
    public abstract ThrustLevel Sample(double secondsSinceIgnition);
}

/// <summary>A throttle-less liquid engine or thruster bank (translation thrusters, a docked target's main engine).</summary>
public sealed record ConstantThrustEngine(double ThrustN, double IspS, double PropellantKg) : BurnEngine
{
    public override double AvailablePropellantKg => PropellantKg;

    public override double? FixedDurationS => null;

    public override double ExhaustVelocityMps => IspS * PhysicalConstants.StandardGravity;

    public double MassFlowKgPerS => ThrustN / ExhaustVelocityMps;

    public override ThrustLevel Sample(double secondsSinceIgnition) =>
        secondsSinceIgnition >= 0 ? new ThrustLevel(ThrustN, MassFlowKgPerS) : ThrustLevel.Off;
}

/// <summary>The solid retro rockets fired as one overlapping sequence.</summary>
public sealed record RetroSequenceEngine(RetroSequence Sequence) : BurnEngine
{
    public override double AvailablePropellantKg => Sequence.TotalPropellantKg;

    public override double? FixedDurationS => Sequence.TotalDurationS;

    public override double ExhaustVelocityMps
    {
        get
        {
            var thrustTime = 0.0;
            for (var i = 0; i < Sequence.Rockets.Count; i++) thrustTime += Sequence.Rockets[i].ThrustN * Sequence.Rockets[i].BurnDurationS;
            return AvailablePropellantKg > 0 ? thrustTime / AvailablePropellantKg : 0;
        }
    }

    public override ThrustLevel Sample(double secondsSinceIgnition) => Sequence.Sample(secondsSinceIgnition);
}
