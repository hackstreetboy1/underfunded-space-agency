using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Flight;

/// <summary>Solid retro rockets fired in an overlapping ripple. Thrust is the sum of the rockets burning.</summary>
public sealed record RetroSequence(IReadOnlyList<RetroRocket> Rockets)
{
    public double TotalDurationS
    {
        get
        {
            var end = 0.0;
            for (var i = 0; i < Rockets.Count; i++) end = Math.Max(end, Rockets[i].IgnitionOffsetS + Rockets[i].BurnDurationS);
            return end;
        }
    }

    public double TotalPropellantKg
    {
        get
        {
            var total = 0.0;
            for (var i = 0; i < Rockets.Count; i++) total += Rockets[i].PropellantKg;
            return total;
        }
    }

    public ThrustLevel Sample(double secondsSinceIgnition)
    {
        var thrust = 0.0;
        var massFlow = 0.0;
        for (var i = 0; i < Rockets.Count; i++)
        {
            var rocket = Rockets[i];
            if (secondsSinceIgnition < rocket.IgnitionOffsetS || secondsSinceIgnition >= rocket.IgnitionOffsetS + rocket.BurnDurationS) continue;
            thrust += rocket.ThrustN;
            massFlow += rocket.MassFlowKgPerS;
        }
        return new ThrustLevel(thrust, massFlow);
    }
}

public sealed record RetroRocket(ComponentId Id, double IgnitionOffsetS, double BurnDurationS, double ThrustN, double IspS)
{
    /// <summary>
    /// A rocket as the vehicle definition describes it (thrust, burn time, propellant mass, ignition offset);
    /// Isp follows from the total impulse, so the propellant flown equals the vehicle's propellant exactly.
    /// </summary>
    public static RetroRocket FromVehicleSpec(ComponentId id, double ignitionOffsetS, double thrustN, double burnS, double propellantKg) =>
        new(id, ignitionOffsetS, burnS, thrustN, thrustN * burnS / (propellantKg * PhysicalConstants.StandardGravity));

    public double MassFlowKgPerS => ThrustN / (IspS * PhysicalConstants.StandardGravity);

    public double PropellantKg => MassFlowKgPerS * BurnDurationS;
}
