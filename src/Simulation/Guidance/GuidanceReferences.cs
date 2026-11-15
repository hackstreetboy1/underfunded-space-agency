using Usa.Simulation.Ports;

namespace Usa.Simulation.Guidance;

/// <summary>
/// Catalog lookups used to validate guidance data at load. The integration step passes the real
/// catalogs (facts, actions, state, UI anchors); any lookup left null is not checked.
/// </summary>
public sealed class GuidanceReferences
{
    public static GuidanceReferences None { get; } = new();

    public Func<string, bool>? IsKnownFact { get; init; }

    public Func<string, bool>? IsKnownAction { get; init; }

    public Func<string, bool>? IsKnownAnchor { get; init; }

    /// <summary>When set, every expression path must be known to it.</summary>
    public IStateQuery? State { get; init; }
}
