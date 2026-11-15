using Usa.Simulation.Engineering;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;
using Usa.Simulation.Session;

namespace Usa.Game.Integration.Runtime;

/// <summary>
/// A read-only copy of the session's spares for the workspace model, which needs an <see cref="Inventory"/> object while the
/// session serves its own only as <c>inventory.&lt;part&gt;.count/.reserved</c> state paths. <see cref="Sync"/> copies those
/// counts in (call it on every <c>InventoryChanged</c>); nothing written here reaches the simulation.
/// </summary>
public sealed class SessionInventoryMirror
{
    private readonly IStateQuery _state;
    private readonly EventBus _events = new();
    private readonly (PartId Part, string CountPath, string ReservedPath)[] _parts;

    public SessionInventoryMirror(MissionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _state = session.State;
        Inventory = new Inventory(session.Workspace.Parts, session.Data.Inventory, _events);
        _parts = new (PartId, string, string)[Inventory.Parts.Count];
        for (var i = 0; i < _parts.Length; i++)
        {
            var part = Inventory.Parts[i];
            _parts[i] = (part, $"{StatePaths.Inventory}{part}.count", $"{StatePaths.Inventory}{part}.reserved");
        }
        Sync(session.Now);
    }

    public Inventory Inventory { get; }

    /// <returns>True when any count or reservation changed.</returns>
    public bool Sync(MissionTime now)
    {
        var changed = false;
        foreach (var (part, countPath, reservedPath) in _parts)
        {
            var count = Whole(countPath);
            var reserved = Math.Min(Whole(reservedPath), count);
            if (Inventory.Count(part) != count)
            {
                Inventory.Apply(countPath, EffectOperation.Set, StateValue.Of(count), now);
                changed = true;
            }
            var delta = reserved - Inventory.Reserved(part);
            if (delta > 0) Inventory.Reserve(part, delta, now);
            else if (delta < 0) Inventory.Return(part, -delta, now);
            changed |= delta != 0;
        }
        // Nobody drains the mirror's bus; drop what the copy published.
        _events.Drain(static _ => { });
        return changed;
    }

    private int Whole(string path) =>
        _state.TryGet(path, out var value) && value.Kind == StateValueKind.Number ? (int)Math.Max(0, Math.Round(value.AsNumber())) : 0;
}
