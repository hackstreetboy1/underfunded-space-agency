using Usa.Simulation.Kernel;

namespace Usa.Simulation.Knowledge;

/// <summary>A validated, recorded observation. Immutable; history is never rewritten.</summary>
/// <param name="Value">Numeric value for number facts and estimates; null for text facts and ground bands.</param>
/// <param name="Qualitative">Text value: a component condition such as <c>failed:stuck_open</c>, a status, or a ground band word.</param>
/// <param name="Observer">The crew member who made it; null for downlink and model values.</param>
/// <param name="Scope">Components the observation physically covered (inspections and tests).</param>
public sealed record Observation(
    ObservationId Id,
    FactId Fact,
    double? Value,
    string? Qualitative,
    CrewId? Observer,
    ObservationMethod Method,
    double Confidence,
    MissionTime Time,
    IReadOnlyList<ComponentId> Scope)
{
    /// <summary>The command that produced it (a glance read or crew task), so comms can match results to orders; null otherwise.</summary>
    public CommandId? Origin { get; init; }
}
