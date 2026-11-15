using Usa.Simulation.Ports;

namespace Usa.Simulation.Expressions;

/// <summary>
/// Serves fixed values under one prefix, e.g. the difficulty profile as <c>difficulty.&lt;key&gt;</c> or
/// scenario variant picks as <c>variant.&lt;key&gt;</c>. Keys are given without the prefix.
/// </summary>
public sealed class StaticStateProvider : IStateProvider
{
    private readonly Dictionary<string, StateValue> _values;

    public StaticStateProvider(string prefix, IReadOnlyDictionary<string, StateValue> values)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        ArgumentNullException.ThrowIfNull(values);
        Prefix = prefix;
        _values = new Dictionary<string, StateValue>(values.Count, StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException($"A key under '{prefix}' is empty.", nameof(values));
            if (value.Kind == StateValueKind.None)
                throw new ArgumentException($"'{prefix}{key}' has no value.", nameof(values));
            _values.Add(prefix + key, value);
        }
    }

    public string Prefix { get; }

    public int Count => _values.Count;

    public bool TryGet(string path, out StateValue value) => _values.TryGetValue(path, out value);

    public bool IsKnownPath(string path) => _values.ContainsKey(path);
}
