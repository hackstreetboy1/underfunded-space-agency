using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Engineering;

/// <summary>
/// Spare parts on board. Parts for a pending repair are reserved first, then consumed or returned when it ends.
/// Tools (<see cref="PartKind.Tool"/>) are never reserved or consumed: a task that needs one checks its count
/// (<c>TaskBinder</c>), and a repair design cannot use one (tools are not assembly parts).
/// Serves <c>inventory.&lt;partId&gt;.count</c> (on board), <c>.available</c> (not reserved) and <c>.reserved</c>
/// for every catalog part, and accepts Set/Add effects on <c>inventory.&lt;partId&gt;.count</c>.
/// </summary>
/// <remarks>
/// Reservations are not keyed by holder: the repair workspace is the only one, and it keeps the reserved count equal to
/// the parts of its pending designs (it handles <see cref="InventoryReservationLost"/>). Key them by holder before adding
/// a second.
/// </remarks>
public sealed class Inventory : IStateProvider, IEffectHandler, IHashContributor
{
    private enum Field
    {
        Count,
        Available,
        Reserved,
    }

    private readonly PartId[] _parts;
    private readonly int[] _counts;
    private readonly int[] _reserved;
    private readonly bool[] _isTool;
    private readonly Dictionary<PartId, int> _index;
    private readonly Dictionary<string, (int Index, Field Field)> _paths;
    private readonly EventBus _events;

    public Inventory(PartCatalog catalog, InventoryDefinition definition, EventBus events)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(events);
        definition.Validate($"inventory '{definition.Mission}/{definition.Difficulty}'", catalog);

        _events = events;
        _parts = new PartId[catalog.Parts.Count];
        _counts = new int[_parts.Length];
        _reserved = new int[_parts.Length];
        _isTool = new bool[_parts.Length];
        _index = new Dictionary<PartId, int>(_parts.Length);
        _paths = new Dictionary<string, (int, Field)>(_parts.Length * 3, StringComparer.Ordinal);
        for (var i = 0; i < _parts.Length; i++)
        {
            var id = catalog.Parts[i].Id;
            _parts[i] = id;
            _isTool[i] = catalog.Parts[i].IsTool;
            _index.Add(id, i);
            _paths.Add($"{StatePaths.Inventory}{id}.count", (i, Field.Count));
            _paths.Add($"{StatePaths.Inventory}{id}.available", (i, Field.Available));
            _paths.Add($"{StatePaths.Inventory}{id}.reserved", (i, Field.Reserved));
        }
        foreach (var item in definition.Items) _counts[_index[item.Part]] = item.Count;
    }

    public string Prefix => StatePaths.Inventory;

    /// <summary>Every catalog part, sorted by id.</summary>
    public IReadOnlyList<PartId> Parts => _parts;

    public int Count(PartId part) => _index.TryGetValue(part, out var i) ? _counts[i] : 0;

    public int Reserved(PartId part) => _index.TryGetValue(part, out var i) ? _reserved[i] : 0;

    public int Available(PartId part) => _index.TryGetValue(part, out var i) ? _counts[i] - _reserved[i] : 0;

    public bool IsTool(PartId part) => _index.TryGetValue(part, out var i) && _isTool[i];

    /// <summary>Sets aside parts for a pending task.</summary>
    public InventoryResult Reserve(PartId part, int quantity, MissionTime now)
    {
        var check = Check(part, quantity, out var i);
        if (check != InventoryResult.Ok) return check;
        if (_isTool[i]) return InventoryResult.ToolNotReservable;
        if (_counts[i] - _reserved[i] < quantity) return InventoryResult.NotEnoughAvailable;
        _reserved[i] += quantity;
        Publish(i, now);
        return InventoryResult.Ok;
    }

    /// <summary>Gives reserved parts back to the available stock (unused, or the task was stopped).</summary>
    public InventoryResult Return(PartId part, int quantity, MissionTime now)
    {
        var check = Check(part, quantity, out var i);
        if (check != InventoryResult.Ok) return check;
        if (_reserved[i] < quantity) return InventoryResult.NotEnoughReserved;
        _reserved[i] -= quantity;
        Publish(i, now);
        return InventoryResult.Ok;
    }

    /// <summary>Uses up reserved parts.</summary>
    public InventoryResult Consume(PartId part, int quantity, MissionTime now)
    {
        var check = Check(part, quantity, out var i);
        if (check != InventoryResult.Ok) return check;
        if (_reserved[i] < quantity) return InventoryResult.NotEnoughReserved;
        _reserved[i] -= quantity;
        _counts[i] -= quantity;
        Publish(i, now);
        return InventoryResult.Ok;
    }

    /// <summary>Reserves every listed part, or nothing if any is short.</summary>
    public InventoryResult ReserveAll(IReadOnlyList<PartQuantity> parts, MissionTime now)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var needed = new int[_parts.Length];
        foreach (var (part, quantity) in parts)
        {
            var check = Check(part, quantity, out var i);
            if (check != InventoryResult.Ok) return check;
            if (_isTool[i]) return InventoryResult.ToolNotReservable;
            needed[i] += quantity;
        }
        for (var i = 0; i < needed.Length; i++)
        {
            if (_counts[i] - _reserved[i] < needed[i]) return InventoryResult.NotEnoughAvailable;
        }
        for (var i = 0; i < needed.Length; i++)
        {
            if (needed[i] == 0) continue;
            _reserved[i] += needed[i];
            Publish(i, now);
        }
        return InventoryResult.Ok;
    }

    /// <summary>Returns every listed reserved part (the task was stopped before it finished), up to what is still reserved.</summary>
    public InventoryResult ReturnAll(IReadOnlyList<PartQuantity> parts, MissionTime now) =>
        Settle(parts, [], now);

    /// <summary>
    /// Ends a repair whose <see cref="RepairTaskSpec.PartsUsed"/> were reserved: success consumes them all,
    /// failure consumes only <see cref="RepairTaskSpec.ConsumedOnFailure"/> and returns the rest.
    /// Parts an effect already removed from the reservation are skipped; every other part still settles.
    /// </summary>
    public InventoryResult SettleRepair(RepairTaskSpec spec, bool succeeded, MissionTime now)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return Settle(spec.PartsUsed, succeeded ? spec.PartsUsed : spec.ConsumedOnFailure, now);
    }

    private InventoryResult Settle(IReadOnlyList<PartQuantity> reserved, IReadOnlyList<PartQuantity> consumed, MissionTime now)
    {
        ArgumentNullException.ThrowIfNull(reserved);
        var release = new int[_parts.Length];
        var use = new int[_parts.Length];
        foreach (var (part, quantity) in reserved)
        {
            var check = Check(part, quantity, out var i);
            if (check != InventoryResult.Ok) return check;
            release[i] += quantity;
        }
        foreach (var (part, quantity) in consumed)
        {
            var check = Check(part, quantity, out var i);
            if (check != InventoryResult.Ok) return check;
            use[i] += quantity;
        }
        var anyReserved = false;
        for (var i = 0; i < release.Length; i++)
        {
            if (use[i] > release[i]) return InventoryResult.NotEnoughReserved;
            anyReserved |= release[i] > 0 && _reserved[i] > 0;
        }
        if (!anyReserved) return InventoryResult.NotEnoughReserved;
        for (var i = 0; i < release.Length; i++)
        {
            // An effect may already have taken parts out of this reservation (see Apply); settle what is left so the
            // other parts never stay reserved forever.
            var released = Math.Min(release[i], _reserved[i]);
            if (released == 0) continue;
            _reserved[i] -= released;
            _counts[i] -= Math.Min(use[i], released);
            Publish(i, now);
        }
        return InventoryResult.Ok;
    }

    public bool TryGet(string path, out StateValue value)
    {
        if (!_paths.TryGetValue(path, out var entry))
        {
            value = StateValue.None;
            return false;
        }
        var (i, field) = entry;
        value = StateValue.Of(field switch
        {
            Field.Count => _counts[i],
            Field.Reserved => _reserved[i],
            _ => _counts[i] - _reserved[i],
        });
        return true;
    }

    public bool IsKnownPath(string path) => _paths.ContainsKey(path);

    public bool CanApply(string target, EffectOperation operation) =>
        _paths.TryGetValue(target, out var entry) && entry.Field == Field.Count && operation is EffectOperation.Set or EffectOperation.Add;

    /// <remarks>
    /// The result is rounded to a whole count and floored at zero. A reservation shrinks to fit, and the loss is published
    /// as <see cref="InventoryReservationLost"/> so the holder can drop what can no longer be built (review SIM-2 S4).
    /// </remarks>
    public void Apply(string target, EffectOperation operation, StateValue value, MissionTime now)
    {
        if (!CanApply(target, operation))
            throw new InvalidOperationException($"Inventory can't apply {operation} to '{target}'.");
        var i = _paths[target].Index;
        var amount = value.AsNumber();
        var result = operation == EffectOperation.Set ? amount : _counts[i] + amount;
        if (!double.IsFinite(result)) throw new InvalidOperationException($"Effect on '{target}' produced {result}.");
        _counts[i] = (int)Math.Max(0, Math.Round(result));
        var lost = _reserved[i] - Math.Min(_reserved[i], _counts[i]);
        _reserved[i] -= lost;
        Publish(i, now);
        if (lost > 0) _events.Publish(new InventoryReservationLost(now, _parts[i], lost));
    }

    public void WriteHash(StateHashWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        for (var i = 0; i < _parts.Length; i++)
        {
            writer.Write(_parts[i].Value);
            writer.Write((long)_counts[i]);
            writer.Write((long)_reserved[i]);
        }
    }

    private InventoryResult Check(PartId part, int quantity, out int index)
    {
        if (!_index.TryGetValue(part, out index)) return InventoryResult.UnknownPart;
        return quantity <= 0 ? InventoryResult.InvalidQuantity : InventoryResult.Ok;
    }

    private void Publish(int i, MissionTime now) =>
        _events.Publish(new InventoryChanged(now, _parts[i], _counts[i], _counts[i] - _reserved[i]));
}
