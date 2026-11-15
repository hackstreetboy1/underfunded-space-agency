using Usa.Simulation.Data;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;
using Usa.Simulation.Scenario.Definitions;
using Usa.Simulation.Scenario.Model;

namespace Usa.Simulation.Scenario;

/// <summary>Validates an <see cref="IncidentsDefinition"/> against its mission and compiles its expressions.</summary>
internal sealed class IncidentsCompiler(string file, ExpressionCollector expressions, DefinitionErrors errors, Mission mission)
{
    public IncidentSet Compile(IncidentsDefinition definition)
    {
        var variants = CompileVariants(definition.Variants);
        var variantsById = variants.ToDictionary(v => v.Id, StringComparer.Ordinal);
        var eventIds = mission.Events.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);

        var seen = new HashSet<IncidentId>();
        var incidents = new List<Incident>(definition.Incidents.Count);
        for (var i = 0; i < definition.Incidents.Count; i++)
        {
            var incident = definition.Incidents[i];
            var path = $"$.incidents[{i}]";
            if (!seen.Add(incident.Id)) Error(path + ".id", $"Duplicate incident id '{incident.Id}'.");

            var triggers = new List<IncidentTrigger>();
            CompileTrigger(incident.Trigger, path + ".trigger", eventIds, triggers, nested: false);

            if (incident.Effects.Count == 0) Error(path + ".effects", "An incident needs at least one effect.");
            var effects = new EffectDefinition[incident.Effects.Count];
            for (var e = 0; e < incident.Effects.Count; e++)
            {
                var effect = incident.Effects[e];
                var effectPath = $"{path}.effects[{e}]";
                CheckTarget(effect.Target, effectPath + ".target", variantsById);
                var value = expressions.Compile(file, effectPath + ".value", effect.Value);
                effects[e] = new EffectDefinition(effect.Target, effect.Op, value);
            }

            VariantGuard? guard = null;
            if (incident.RequiresVariant is { } requires)
            {
                if (!variantsById.TryGetValue(requires.Id, out var variant))
                    Error(path + ".requiresVariant.id", $"Unknown variant '{requires.Id}'.");
                else if (!variant.Options.Contains(requires.Option, StringComparer.Ordinal))
                    Error(path + ".requiresVariant.option", $"Variant '{requires.Id}' has no option '{requires.Option}'.");
                guard = new VariantGuard(requires.Id, requires.Option);
            }

            if (incident.RaisesEvent is { } raises && !eventIds.Contains(raises))
                Error(path + ".raisesEvent", $"Unknown mission event '{raises}'.");

            incidents.Add(new Incident(incident.Id, incident.Description, triggers, effects, guard, incident.RaisesEvent));
        }

        incidents.Sort((a, b) => a.Id.CompareTo(b.Id));
        return new IncidentSet(variants, incidents);
    }

    private List<Variant> CompileVariants(IReadOnlyList<VariantDefinition> definitions)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var variants = new List<Variant>(definitions.Count);
        for (var i = 0; i < definitions.Count; i++)
        {
            var definition = definitions[i];
            var path = $"$.variants[{i}]";
            CheckId(definition.Id, path + ".id");
            if (!seen.Add(definition.Id)) Error(path + ".id", $"Duplicate variant id '{definition.Id}'.");
            if (definition.Options.Count == 0) Error(path + ".options", "A variant needs at least one option.");
            var options = new HashSet<string>(StringComparer.Ordinal);
            for (var o = 0; o < definition.Options.Count; o++)
            {
                if (string.IsNullOrWhiteSpace(definition.Options[o])) Error($"{path}.options[{o}]", "Option is empty.");
                else if (!options.Add(definition.Options[o])) Error($"{path}.options[{o}]", $"Duplicate option '{definition.Options[o]}'.");
            }
            variants.Add(new Variant(definition.Id, definition.Options));
        }
        variants.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        return variants;
    }

    private void CompileTrigger(
        IncidentTriggerDefinition trigger,
        string path,
        HashSet<string> eventIds,
        List<IncidentTrigger> leaves,
        bool nested)
    {
        switch (trigger.Type)
        {
            case IncidentTriggerType.Any:
                if (nested) Error(path + ".type", "'any' triggers cannot be nested.");
                Forbid(trigger.Event is not null, path, "event", trigger.Type);
                Forbid(trigger.DelayS is not null, path, "delayS", trigger.Type);
                Forbid(trigger.MetS is not null, path, "metS", trigger.Type);
                Forbid(trigger.When is not null, path, "when", trigger.Type);
                Forbid(trigger.HoldS != 0, path, "holdS", trigger.Type);
                if (trigger.Triggers is not { Count: > 0 } children)
                {
                    Error(path + ".triggers", "An 'any' trigger needs at least one nested trigger.");
                    return;
                }
                for (var i = 0; i < children.Count; i++)
                {
                    CompileTrigger(children[i], $"{path}.triggers[{i}]", eventIds, leaves, nested: true);
                }
                return;

            case IncidentTriggerType.AtTime:
                Forbid(trigger.Event is not null, path, "event", trigger.Type);
                Forbid(trigger.DelayS is not null, path, "delayS", trigger.Type);
                Forbid(trigger.When is not null, path, "when", trigger.Type);
                Forbid(trigger.HoldS != 0, path, "holdS", trigger.Type);
                Forbid(trigger.Triggers is not null, path, "triggers", trigger.Type);
                CheckRange(trigger.MetS, path + ".metS");
                leaves.Add(new IncidentTrigger(trigger.Type, null, trigger.MetS, null, 0));
                return;

            case IncidentTriggerType.AfterEvent:
                Forbid(trigger.MetS is not null, path, "metS", trigger.Type);
                Forbid(trigger.When is not null, path, "when", trigger.Type);
                Forbid(trigger.HoldS != 0, path, "holdS", trigger.Type);
                Forbid(trigger.Triggers is not null, path, "triggers", trigger.Type);
                if (trigger.Event is null) Error(path + ".event", "An after_event trigger needs an event.");
                else if (!eventIds.Contains(trigger.Event)) Error(path + ".event", $"Unknown mission event '{trigger.Event}'.");
                CheckRange(trigger.DelayS, path + ".delayS");
                leaves.Add(new IncidentTrigger(trigger.Type, trigger.Event, trigger.DelayS, null, 0));
                return;

            case IncidentTriggerType.Condition:
                Forbid(trigger.Event is not null, path, "event", trigger.Type);
                Forbid(trigger.DelayS is not null, path, "delayS", trigger.Type);
                Forbid(trigger.MetS is not null, path, "metS", trigger.Type);
                Forbid(trigger.Triggers is not null, path, "triggers", trigger.Type);
                if (trigger.HoldS < 0 || !double.IsFinite(trigger.HoldS)) Error(path + ".holdS", "holdS must be a finite value >= 0.");
                var when = expressions.CompileCondition(file, path + ".when", trigger.When);
                leaves.Add(new IncidentTrigger(trigger.Type, null, null, when, trigger.HoldS));
                return;

            default:
                Error(path + ".type", $"Unsupported trigger type '{trigger.Type}'.");
                return;
        }
    }

    private void CheckRange(SecondsRange? range, string path)
    {
        if (range is null)
        {
            Error(path, "Missing {min, max} range.");
            return;
        }
        if (!double.IsFinite(range.Min) || !double.IsFinite(range.Max) || range.Min < 0 || range.Max < range.Min)
            Error(path, "Range needs 0 <= min <= max.");
    }

    private void CheckTarget(string target, string path, Dictionary<string, Variant> variants)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            Error(path, "Target is empty.");
            return;
        }
        if (!EffectTargets.HasPlaceholder(target)) return;
        if (!EffectTargets.TryGetVariantId(target, out var variantId))
            Error(path, "A placeholder must be a leading '{variant.<id>}'.");
        else if (!variants.ContainsKey(variantId))
            Error(path, $"Unknown variant '{variantId}'.");
    }

    private void Forbid(bool present, string path, string field, IncidentTriggerType type)
    {
        if (present) Error($"{path}.{field}", $"'{field}' does not apply to {type} triggers.");
    }

    private void CheckId(string id, string path)
    {
        try
        {
            IdRules.Validate(id, "Variant id");
        }
        catch (ArgumentException ex)
        {
            Error(path, ex.Message);
        }
    }

    private void Error(string jsonPath, string message) => errors.Add(file, jsonPath, message);
}
