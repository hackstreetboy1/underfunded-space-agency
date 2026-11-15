using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Crew.Tasks;

/// <summary>
/// Cross-area lookups for <see cref="TaskCatalog.ValidateBindings"/>, supplied at integration.
/// A null member skips that check.
/// </summary>
public sealed record TaskReferenceChecks
{
    public Func<PartId, bool>? IsKnownPart { get; init; }

    public Func<FactId, bool>? IsKnownFact { get; init; }

    public Func<ComponentId, bool>? IsKnownComponent { get; init; }

    /// <summary>Checks every path read by preconditions and effect values.</summary>
    public IStateQuery? State { get; init; }

    /// <summary>Checks every effect target, e.g. <see cref="EffectRouter.CanApply"/>.</summary>
    public Func<EffectDefinition, bool>? CanApplyEffect { get; init; }

    /// <summary>The mission's crew locations; checks task locations, required locations and moves.</summary>
    public CrewLocations? Locations { get; init; }
}
