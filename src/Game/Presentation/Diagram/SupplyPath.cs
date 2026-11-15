using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Diagram;

/// <summary>Everything that can feed a load: the components and power lines back to its sources.</summary>
/// <param name="Components">The load first, then upstream components in walk order.</param>
/// <param name="ConnectionIds">Power connections on the path (placed components only).</param>
/// <param name="Sources">Fuel cells and batteries reached.</param>
public sealed record SupplyPath(
    ComponentId Load,
    IReadOnlyList<ComponentId> Components,
    IReadOnlyList<string> ConnectionIds,
    IReadOnlyList<ComponentId> Sources)
{
    public bool IsEmpty => Components.Count <= 1;

    public bool Contains(ComponentId component)
    {
        foreach (var id in Components)
        {
            if (id == component) return true;
        }
        return false;
    }
}
