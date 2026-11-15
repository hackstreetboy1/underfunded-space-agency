using Usa.Simulation.Kernel;

namespace Usa.Simulation.Knowledge;

/// <summary>
/// Crew facts the validator needs, supplied by the integration step from the Crew and Vehicle areas
/// (alive flag, crew location, component access points).
/// </summary>
public interface IObservationAccess
{
    bool IsAlive(CrewId crew);

    /// <summary>
    /// True when <paramref name="crew"/> can currently observe <paramref name="component"/> with
    /// <paramref name="method"/>: in the right place (cabin gauge, window view, EVA) and not prevented.
    /// </summary>
    bool CanAccess(CrewId crew, ComponentId component, ObservationMethod method);
}
