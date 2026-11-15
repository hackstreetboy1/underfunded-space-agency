using Usa.Simulation.Ports;

namespace Usa.Simulation.Commands.Catalog;

/// <summary>A precondition compiled for every combination of its placeholder values.</summary>
internal sealed class CompiledPrecondition(ArgumentTemplate template, string reason, IReadOnlyDictionary<string, IExpression> expressions)
{
    public string Reason { get; } = reason;

    public IExpression Resolve(IReadOnlyDictionary<string, string> canonicalArgs) => expressions[template.Render(canonicalArgs)];
}

internal sealed class CompiledImpactRule(KeyValuePair<string, string[]>[] conditions, ActionImpact impact, IReadOnlyList<string> effectTextKeys)
{
    public ActionImpact Impact { get; } = impact;

    public IReadOnlyList<string> EffectTextKeys { get; } = effectTextKeys;

    public bool Matches(IReadOnlyDictionary<string, string> canonicalArgs)
    {
        foreach (var (name, patterns) in conditions)
        {
            if (!canonicalArgs.TryGetValue(name, out var value) || !MatchesAny(patterns, value)) return false;
        }
        return true;
    }

    public static bool MatchesAny(IReadOnlyList<string> patterns, string value)
    {
        foreach (var pattern in patterns)
        {
            var matched = pattern.EndsWith('*')
                ? value.StartsWith(pattern.AsSpan(0, pattern.Length - 1), StringComparison.Ordinal)
                : string.Equals(value, pattern, StringComparison.Ordinal);
            if (matched) return true;
        }
        return false;
    }
}

/// <summary>A remote effect whose value is either a compiled expression or a parameter written as-is.</summary>
internal sealed class CompiledEffect(
    ArgumentTemplate target,
    EffectOperation operation,
    string? argName,
    ArgumentTemplate? valueTemplate,
    IReadOnlyDictionary<string, IExpression>? values)
{
    public EffectDefinition Build(ParsedArguments args)
    {
        var value = argName is not null
            ? new ConstantExpression(args.Values[argName].ToStateValue())
            : values![valueTemplate!.Render(args.Canonical)];
        return new EffectDefinition(target.Render(args.Canonical), operation, value);
    }
}

internal sealed class ConstantExpression(StateValue value) : IExpression
{
    public string Source { get; } = value.ToString();

    public IReadOnlyList<string> Paths => [];

    public StateValue Evaluate(IStateQuery state) => value;
}
