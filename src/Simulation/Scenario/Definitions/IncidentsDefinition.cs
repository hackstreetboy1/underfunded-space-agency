using Usa.Simulation.Data;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Scenario.Definitions;

/// <summary>Root of <c>missions/&lt;id&gt;/incidents.json</c>, schema <c>incidents/1</c>.</summary>
public sealed record IncidentsDefinition(
    string Schema,
    IReadOnlyList<VariantDefinition> Variants,
    IReadOnlyList<IncidentDefinition> Incidents) : IVersionedDefinition;

/// <summary>One option is picked per mission from the <c>variants</c> stream and served as <c>variant.&lt;id&gt;</c>.</summary>
public sealed record VariantDefinition(string Id, IReadOnlyList<string> Options);

/// <param name="RequiresVariant">The incident is armed only when this variant option was picked.</param>
/// <param name="RaisesEvent">Mission event raised when the incident fires.</param>
public sealed record IncidentDefinition(
    IncidentId Id,
    IncidentTriggerDefinition Trigger,
    IReadOnlyList<IncidentEffectDefinition> Effects,
    string Description = "",
    VariantGuardDefinition? RequiresVariant = null,
    string? RaisesEvent = null);

public sealed record IncidentTriggerDefinition(
    IncidentTriggerType Type,
    string? Event = null,
    SecondsRange? DelayS = null,
    SecondsRange? MetS = null,
    string? When = null,
    double HoldS = 0,
    IReadOnlyList<IncidentTriggerDefinition>? Triggers = null);

/// <param name="Target">State path without '@'; may start with <c>{variant.&lt;id&gt;}</c>, replaced by the pick.</param>
/// <param name="Value">Expression source, e.g. <c>'failed'</c> or <c>@difficulty.stuck_thrust_fraction</c>.</param>
public sealed record IncidentEffectDefinition(string Target, EffectOperation Op, string Value);

public sealed record VariantGuardDefinition(string Id, string Option);

/// <summary>A seeded uniform draw range in seconds.</summary>
public sealed record SecondsRange(double Min, double Max);
