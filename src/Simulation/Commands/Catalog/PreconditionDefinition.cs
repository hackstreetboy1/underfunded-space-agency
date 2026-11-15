namespace Usa.Simulation.Commands.Catalog;

/// <summary>An expression that must be true for the action to run; otherwise it is refused with <paramref name="Reason"/>.</summary>
/// <param name="Require">Expression source; may contain <c>{param}</c> placeholders for enum or boolean parameters.</param>
/// <param name="Reason">Reason code the crew uses to explain the refusal, e.g. <c>spin_too_high_for_inspection</c>.</param>
public sealed record PreconditionDefinition(string Require, string Reason);
