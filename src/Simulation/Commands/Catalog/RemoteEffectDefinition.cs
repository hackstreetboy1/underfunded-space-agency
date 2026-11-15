using Usa.Simulation.Ports;

namespace Usa.Simulation.Commands.Catalog;

/// <summary>A state change made by a remote control. Exactly one of <paramref name="Value"/> and <paramref name="Arg"/> is set.</summary>
/// <param name="Target">State path template without '@', e.g. <c>{breaker}.closed</c>.</param>
/// <param name="Value">Expression source; may contain <c>{param}</c> placeholders for enum or boolean parameters.</param>
/// <param name="Arg">Name of the parameter whose value is written as-is.</param>
public sealed record RemoteEffectDefinition(string Target, EffectOperation Op, string? Value = null, string? Arg = null);
