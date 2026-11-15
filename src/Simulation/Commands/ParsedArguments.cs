namespace Usa.Simulation.Commands;

/// <summary>Arguments checked against an action's parameter schema, held in name order with typed values.</summary>
public sealed class ParsedArguments
{
    private readonly SortedDictionary<string, CommandArg> _values;
    private readonly Dictionary<string, string> _canonical;

    internal ParsedArguments(SortedDictionary<string, CommandArg> values)
    {
        _values = values;
        _canonical = new Dictionary<string, string>(values.Count, StringComparer.Ordinal);
        foreach (var (name, value) in values) _canonical[name] = value.ToCanonicalString();
    }

    /// <summary>Iterates in ordinal name order.</summary>
    public IReadOnlyDictionary<string, CommandArg> Values => _values;

    internal IReadOnlyDictionary<string, string> Canonical => _canonical;
}
