namespace Usa.Simulation.Ports;

/// <summary>
/// Read access to simulation state by path. This is how areas see each other's state without
/// compile-time coupling: expressions, validators and feasibility rules all read through it.
/// Path conventions are listed in <see cref="StatePaths"/>.
/// </summary>
public interface IStateQuery
{
    bool TryGet(string path, out StateValue value);

    /// <summary>True when <paramref name="path"/> names real state (used to validate data at load).</summary>
    bool IsKnownPath(string path);
}

/// <summary>Serves every path that starts with <see cref="Prefix"/>.</summary>
public interface IStateProvider
{
    /// <summary>e.g. "tandem/", "crew.", "mission.". The longest matching prefix wins.</summary>
    string Prefix { get; }

    bool TryGet(string path, out StateValue value);

    bool IsKnownPath(string path);
}

public sealed class UnknownStatePathException(string path)
    : InvalidOperationException($"Unknown state path '{path}'.")
{
    public string Path { get; } = path;
}

/// <summary>Routes queries to the provider with the longest matching prefix.</summary>
public sealed class CompositeStateQuery : IStateQuery
{
    private readonly List<IStateProvider> _providers = [];

    public void Register(IStateProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (_providers.Any(p => p.Prefix == provider.Prefix))
            throw new InvalidOperationException($"A provider for '{provider.Prefix}' is already registered.");
        _providers.Add(provider);
        _providers.Sort((a, b) => b.Prefix.Length.CompareTo(a.Prefix.Length));
    }

    public bool TryGet(string path, out StateValue value)
    {
        var provider = Find(path);
        if (provider is not null) return provider.TryGet(path, out value);
        value = StateValue.None;
        return false;
    }

    public bool IsKnownPath(string path) => Find(path)?.IsKnownPath(path) ?? false;

    private IStateProvider? Find(string path)
    {
        foreach (var provider in _providers)
        {
            if (path.StartsWith(provider.Prefix, StringComparison.Ordinal)) return provider;
        }
        return null;
    }
}

public static class StateQueryExtensions
{
    public static StateValue Get(this IStateQuery state, string path) =>
        state.TryGet(path, out var value) ? value : throw new UnknownStatePathException(path);

    public static double GetNumber(this IStateQuery state, string path) => state.Get(path).AsNumber();

    public static bool GetBool(this IStateQuery state, string path) => state.Get(path).AsBool();

    public static string GetText(this IStateQuery state, string path) => state.Get(path).AsText();
}
