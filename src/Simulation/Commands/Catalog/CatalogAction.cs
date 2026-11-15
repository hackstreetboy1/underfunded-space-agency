using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Commands.Catalog;

/// <summary>An action definition with its expressions and templates compiled at load.</summary>
public sealed class CatalogAction
{
    private readonly HashSet<string> _phases;
    private readonly CompiledPrecondition[] _preconditions;
    private readonly CompiledImpactRule[] _impactRules;
    private readonly CompiledEffect[] _effects;
    private readonly ArgumentTemplate[] _affects;

    internal CatalogAction(
        ActionDefinition definition,
        CompiledPrecondition[] preconditions,
        CompiledImpactRule[] impactRules,
        CompiledEffect[] effects,
        ArgumentTemplate[] affects)
    {
        Definition = definition;
        Parameters = definition.Parameters ?? [];
        _phases = new HashSet<string>(definition.Phases, StringComparer.Ordinal);
        _preconditions = preconditions;
        _impactRules = impactRules;
        _effects = effects;
        _affects = affects;
    }

    public ActionDefinition Definition { get; }

    public ActionId Id => Definition.Id;

    public ActionKind Kind => Definition.Kind;

    public ActionImpact BaseImpact => Definition.Impact;

    public IReadOnlyList<ActionParameter> Parameters { get; }

    /// <summary>True when a crew member carries the action out and must be resolved and able to work.</summary>
    public bool UsesCrew => Kind is ActionKind.CrewTask or ActionKind.GlanceRead || Definition.UsesCrew;

    public bool IsAllowedIn(string phase) => _phases.Contains(phase);

    public ActionParameter? FindParameter(string name) => ActionParameters.Find(Parameters, name);

    /// <summary>The reason code of the first precondition that does not hold, or null when all hold.</summary>
    public string? FirstFailedPrecondition(IStateQuery state, ParsedArguments args)
    {
        ArgumentNullException.ThrowIfNull(args);
        foreach (var precondition in _preconditions)
        {
            if (!precondition.Resolve(args.Canonical).IsTrue(state)) return precondition.Reason;
        }
        return null;
    }

    /// <summary>The base impact raised by every matching parameter rule.</summary>
    public ActionImpact ImpactFor(ParsedArguments args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var impact = BaseImpact;
        foreach (var rule in _impactRules)
        {
            if (rule.Impact > impact && rule.Matches(args.Canonical)) impact = rule.Impact;
        }
        return impact;
    }

    public IReadOnlyList<string> EffectTextKeysFor(ParsedArguments args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var keys = new List<string>(Definition.EffectTextKeys ?? []);
        foreach (var rule in _impactRules)
        {
            if (!rule.Matches(args.Canonical)) continue;
            foreach (var key in rule.EffectTextKeys)
            {
                if (!keys.Contains(key, StringComparer.Ordinal)) keys.Add(key);
            }
        }
        return keys;
    }

    public IReadOnlyList<ComponentId> AffectedComponents(ParsedArguments args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var components = new ComponentId[_affects.Length];
        for (var i = 0; i < _affects.Length; i++) components[i] = new ComponentId(_affects[i].Render(args.Canonical));
        return components;
    }

    /// <summary>The state changes a remote action makes with these arguments.</summary>
    public IReadOnlyList<EffectDefinition> BuildEffects(ParsedArguments args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var effects = new EffectDefinition[_effects.Length];
        for (var i = 0; i < _effects.Length; i++) effects[i] = _effects[i].Build(args);
        return effects;
    }

    /// <summary>Facts a glance read reports with these arguments.</summary>
    public IReadOnlyList<FactId> GlanceFacts(ParsedArguments args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (Definition.Glance is not { } glance) return [];
        return glance.FactParam is { } param ? [new FactId(args.Values[param].AsText())] : glance.Facts ?? [];
    }

    public override string ToString() => Id.Value;
}
