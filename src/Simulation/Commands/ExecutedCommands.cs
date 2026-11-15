using Usa.Simulation.Kernel;

namespace Usa.Simulation.Commands;

/// <summary>Ids of commands that have executed, in execution order; the dedupe set for retries and reconnects.</summary>
public sealed class ExecutedCommands : IHashContributor
{
    private readonly List<CommandId> _order = [];
    private readonly HashSet<CommandId> _ids = [];

    public int Count => _order.Count;

    public IReadOnlyList<CommandId> InOrder => _order;

    public bool Contains(CommandId id) => _ids.Contains(id);

    /// <returns>False when <paramref name="id"/> was already recorded.</returns>
    /// <exception cref="ArgumentException">An empty or default id, which can never be a dedupe key.</exception>
    public bool Add(CommandId id)
    {
        if (string.IsNullOrWhiteSpace(id.Value)) throw new ArgumentException("A command id is required.", nameof(id));
        if (!_ids.Add(id)) return false;
        _order.Add(id);
        return true;
    }

    public void WriteHash(StateHashWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write((long)_order.Count);
        foreach (var id in _order) writer.Write(id.Value);
    }
}
