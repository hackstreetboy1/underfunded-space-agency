using Usa.Simulation.Kernel;

namespace Usa.Simulation.Flight;

/// <summary>Supplies thrust at the start of each powered integration step (held constant over the step).</summary>
public interface IThrustLaw
{
    ThrustSample Sample(MissionTime time, double stepSeconds, in StateVector state, double massKg);
}
