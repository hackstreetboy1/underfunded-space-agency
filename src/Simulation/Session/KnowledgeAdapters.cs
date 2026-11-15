using Usa.Simulation.Commands;
using Usa.Simulation.Crew;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;

namespace Usa.Simulation.Session;

/// <summary>
/// Who can observe what (<see cref="IObservationAccess"/>): a living crew member observes components that are still attached
/// (or not part of any vehicle, e.g. the cabin itself). From inside the cabin every method works; on EVA only physical
/// inspection does (no gauges or panel tests outside).
/// </summary>
internal sealed class ObservationAccess(CrewRoster roster, VehicleSet vehicles) : IObservationAccess
{
    public bool IsAlive(CrewId crew) => roster.TryFind(crew, out var member) && member.IsAlive;

    public bool CanAccess(CrewId crew, ComponentId component, ObservationMethod method)
    {
        if (!roster.TryFind(crew, out var member) || !member.IsAlive) return false;
        if (vehicles.TryFind(component.Value, out var v, out var index) && !vehicles[v].IsAttached(index)) return false;
        return member.Location == CrewLocation.Cabin || method == ObservationMethod.Inspection;
    }
}

/// <summary>Each crew member's known-fact view for the command pipeline (<see cref="IKnownFacts"/>).</summary>
internal sealed class KnownFacts(KnowledgeBase knowledge) : IKnownFacts
{
    public bool Knows(CrewId crew, FactId fact) => knowledge.Knows(crew, fact);
}
