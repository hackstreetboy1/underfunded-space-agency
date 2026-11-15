using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Diagram;

/// <summary>
/// The ground's knowledge of components for the diagram (an adapter over the knowledge base). Node states come only
/// from here, never from the vehicle simulation.
/// </summary>
public interface IDiagramKnowledgeSource
{
    /// <summary>Incremented whenever any component's look, variant, staleness or confirmation changes.</summary>
    int KnowledgeRevision { get; }

    /// <summary>Current MET, for stale ages.</summary>
    MissionTime Now { get; }

    DiagramKnowledgeEntry Lookup(ComponentId component);
}
