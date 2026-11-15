using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;
using Usa.Simulation.Scenario.Definitions;

namespace Usa.Simulation.Scenario.Model;

/// <param name="Triggers">Alternatives; the first to fire wins. Never of type <see cref="IncidentTriggerType.Any"/>.</param>
/// <param name="Effects">Targets may still hold a <c>{variant.&lt;id&gt;}</c> placeholder.</param>
public sealed record Incident(
    IncidentId Id,
    string Description,
    IReadOnlyList<IncidentTrigger> Triggers,
    IReadOnlyList<EffectDefinition> Effects,
    VariantGuard? RequiresVariant,
    string? RaisesEvent);

/// <param name="Window">Delay range for after_event, MET range for at_time, null for condition.</param>
public sealed record IncidentTrigger(
    IncidentTriggerType Type,
    string? EventId,
    SecondsRange? Window,
    IExpression? When,
    double HoldS);

public sealed record Variant(string Id, IReadOnlyList<string> Options);

public sealed record VariantGuard(string VariantId, string Option);
