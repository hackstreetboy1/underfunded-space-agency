using Usa.Simulation.Kernel;

namespace Usa.Simulation.Commands.Catalog;

/// <summary>
/// One action the player (or the AI on the player's behalf) can request. Text fields that name
/// parameters in braces (<c>{breaker}.closed</c>) are templates expanded with the argument values.
/// </summary>
/// <param name="Description">Shown to the AI in the phase allowlist.</param>
/// <param name="Phases">Mission phases in which the action may be requested.</param>
/// <param name="EffectTextKeys">Localisation keys describing consequences, shown on the confirmation prompt.</param>
/// <param name="Affects">Component id templates listed on the confirmation prompt.</param>
/// <param name="TaskId">Crew task started by a <see cref="ActionKind.CrewTask"/> action.</param>
/// <param name="Effects">State changes applied by a <see cref="ActionKind.Remote"/> action.</param>
/// <param name="Glance">What a <see cref="ActionKind.GlanceRead"/> action reads.</param>
/// <param name="Meta">Which session command a <see cref="ActionKind.Meta"/> action runs.</param>
/// <param name="UsesCrew">For meta actions: whether a crew member carries it out (e.g. a status report).</param>
public sealed record ActionDefinition(
    ActionId Id,
    ActionKind Kind,
    string Description,
    ActionImpact Impact,
    IReadOnlyList<string> Phases,
    IReadOnlyList<ActionParameter>? Parameters = null,
    IReadOnlyList<PreconditionDefinition>? Preconditions = null,
    IReadOnlyList<ImpactRule>? ImpactRules = null,
    IReadOnlyList<string>? EffectTextKeys = null,
    IReadOnlyList<string>? Affects = null,
    TaskId? TaskId = null,
    IReadOnlyList<RemoteEffectDefinition>? Effects = null,
    GlanceDefinition? Glance = null,
    MetaCommandKind? Meta = null,
    bool UsesCrew = false);
