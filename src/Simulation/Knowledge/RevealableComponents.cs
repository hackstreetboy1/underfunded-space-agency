using Usa.Simulation.Kernel;

namespace Usa.Simulation.Knowledge;

/// <summary>Which components can be suspected, confirmed and served as state; shared by the base and its validator.</summary>
internal sealed class RevealableComponents
{
    private readonly HashSet<string>? _ids;
    private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _idsSpan;

    /// <param name="ids">When null, any well-formed vehicle component id is revealable.</param>
    public RevealableComponents(IEnumerable<ComponentId>? ids)
    {
        if (ids is null) return;
        _ids = new HashSet<string>(ids.Select(c => c.Value), StringComparer.Ordinal);
        _idsSpan = _ids.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    public bool Contains(ComponentId component) => Contains(component.Value.AsSpan());

    public bool Contains(ReadOnlySpan<char> component)
    {
        if (_ids is not null) return _idsSpan.Contains(component);
        if (component.IsEmpty || !component.Contains('/')) return false;
        foreach (var c in component)
        {
            if (!(c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '/' or '.' or '-')) return false;
        }
        return true;
    }
}
