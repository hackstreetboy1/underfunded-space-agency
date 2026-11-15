using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Commands.Catalog;

/// <summary>Optional cross-reference sources for load-time validation; each check runs only when its source is given.</summary>
/// <param name="State">Every precondition path and remote effect target must be known to it.</param>
public sealed record ActionCatalogChecks(
    IStateQuery? State = null,
    IReadOnlySet<TaskId>? KnownTasks = null,
    IReadOnlySet<FactId>? KnownFacts = null,
    IReadOnlySet<string>? KnownPhases = null,
    IReadOnlySet<ComponentId>? KnownComponents = null);
