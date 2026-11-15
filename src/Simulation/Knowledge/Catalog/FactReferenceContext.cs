using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Knowledge.Catalog;

/// <summary>
/// Ids and state owned by other areas, supplied at integration to cross-check the fact catalog.
/// A null member skips that check.
/// </summary>
/// <param name="Telemetry">Used to confirm every downlink telemetry path and every <c>valuePath</c> read is real state.</param>
/// <param name="Expressions">Compiles <c>valuePath</c> expressions; with <paramref name="Telemetry"/> their paths are checked too.</param>
public sealed record FactReferenceContext(
    IReadOnlySet<ComponentId>? Components = null,
    IReadOnlySet<CrewId>? Crew = null,
    IReadOnlySet<TaskId>? Tasks = null,
    IStateQuery? Telemetry = null,
    IExpressionCompiler? Expressions = null);
