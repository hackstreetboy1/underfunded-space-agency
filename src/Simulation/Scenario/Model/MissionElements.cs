using Usa.Simulation.Ports;

namespace Usa.Simulation.Scenario.Model;

/// <summary>Exactly one of <paramref name="EventId"/> and <paramref name="When"/> is set.</summary>
public sealed record MissionTrigger(string? EventId, IExpression? When);

public sealed record PhaseTransition(IReadOnlyList<string> From, string To, MissionTrigger Trigger);

/// <param name="When">Raises the event automatically the first time it is true; null for externally raised events.</param>
public sealed record MissionEventSpec(string Id, IExpression? When, string? Source);

public sealed record NamedCondition(string Id, IExpression When);

/// <summary>A compiled <see cref="Definitions.MissionTimerDefinition"/>; event ids are validated, null when not used.</summary>
/// <param name="DurationMs">Deadline length, or null for a plain accumulator.</param>
public sealed record MissionTimer(
    string Id,
    IExpression? RunWhile,
    IExpression? ResetWhen,
    string? StartEvent,
    string? StopEvent,
    long? DurationMs,
    string? ExpireEvent);

public sealed record EndCondition(string Id, MissionTrigger Trigger, MissionOutcomeKind Outcome);
