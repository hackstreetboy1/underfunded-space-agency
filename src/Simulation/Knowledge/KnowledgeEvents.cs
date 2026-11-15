using Usa.Simulation.Kernel;

namespace Usa.Simulation.Knowledge;

/// <summary>A validated observation entered the knowledge base.</summary>
public sealed record ObservationRecorded(MissionTime Time, Observation Observation) : ISimEvent;

/// <summary>
/// A fact's knowledge changed: a new value arrived (<paramref name="Previous"/> may equal <paramref name="Current"/>)
/// or its state moved (became stale, conflicting, resolved).
/// </summary>
public sealed record KnowledgeChanged(MissionTime Time, FactId Fact, KnowledgeState Previous, KnowledgeState Current, ObservationId? Latest) : ISimEvent;

/// <summary>
/// What is known about a component changed (suspected, confirmed, stale, repaired). The 3D view and the
/// system diagram reveal components from this event only, never from truth.
/// </summary>
public sealed record ComponentKnowledgeChanged(MissionTime Time, ComponentId Component, ComponentKnowledgeState Previous, ComponentKnowledge Current) : ISimEvent;

/// <summary>A downlinked ground band word changed (e.g. OAMS propellant NOMINAL to LOW).</summary>
public sealed record GroundBandChanged(MissionTime Time, FactId Fact, string? Previous, string Current) : ISimEvent;
