namespace Usa.Simulation.Commands.Catalog;

/// <summary>
/// Raises an action's impact when every listed parameter matches one of its patterns. A pattern ending
/// in '*' matches by prefix; booleans match "true"/"false".
/// </summary>
public sealed record ImpactRule(
    IReadOnlyDictionary<string, IReadOnlyList<string>> Match,
    ActionImpact Impact,
    IReadOnlyList<string>? EffectTextKeys = null);
