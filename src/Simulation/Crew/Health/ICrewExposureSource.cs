using Usa.Simulation.Kernel;

namespace Usa.Simulation.Crew.Health;

/// <summary>Supplies each astronaut's current exposure to <see cref="HealthModel"/>.</summary>
public interface ICrewExposureSource
{
    CrewExposure Read(CrewId crew);
}
