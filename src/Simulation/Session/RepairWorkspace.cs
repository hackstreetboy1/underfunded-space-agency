using Usa.Simulation.Engineering;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;

namespace Usa.Simulation.Session;

/// <summary>
/// The engineering bridge (review X2): validated workspace designs become installable assemblies. Designs arrive as the
/// <c>register_repair_design</c> meta command (workspace source), so the command log replays them.
/// <list type="bullet">
/// <item><see cref="Check"/> (during validation) refuses a design that does not parse or fit its goal
/// (<see cref="DesignUnreadable"/>) or that fails <see cref="AssemblyValidator"/> (<see cref="DesignFailed"/>).</item>
/// <item><see cref="Register"/> rebuilds the board, validates it and reserves the parts (re-registering an id returns the old
/// reservation first). <c>session.repair.&lt;id&gt;.ready</c> then reads true.</item>
/// <item><see cref="Install"/> (<c>session.repair.install</c>, install_repair success) consumes the reserved parts, sets the sink's
/// <c>jumper</c> to the goal's source outlet in the Vehicle area, and notes the repair of the replaced component in Knowledge.</item>
/// <item><see cref="Partial"/> (<c>session.repair.partial</c>, install_repair stopped) consumes the parts with no electrical effect.</item>
/// <item>When an effect takes reserved parts (<see cref="InventoryReservationLost"/>), ready designs that use the part are dropped
/// in assembly-id order until the loss is covered; each returns the rest of its reservation, reads not ready and publishes
/// <see cref="RepairReservationLost"/>. The reserved count therefore always equals the parts of the ready designs.</item>
/// </list>
/// Unknown or unvalidated ids are no-ops.
/// </summary>
public sealed class RepairWorkspace : IHashContributor
{
    public const string DesignUnreadable = "repair_design_unreadable";
    public const string DesignFailed = "repair_design_failed";

    private readonly PartCatalog _parts;
    private readonly RepairCatalog _repairs;
    private readonly Inventory _inventory;
    private readonly VehicleSet _vehicles;
    private readonly KnowledgeBase _knowledge;
    private readonly AssemblyValidator _validator;
    private readonly EventBus _events;
    private readonly SortedDictionary<string, RepairTaskSpec> _ready = new(StringComparer.Ordinal);

    internal RepairWorkspace(PartCatalog parts, RepairCatalog repairs, Inventory inventory, VehicleSet vehicles, KnowledgeBase knowledge, EventBus events)
    {
        _parts = parts;
        _repairs = repairs;
        _inventory = inventory;
        _vehicles = vehicles;
        _knowledge = knowledge;
        _events = events;
        _validator = new AssemblyValidator(parts);
        events.Subscribe(e =>
        {
            if (e is InventoryReservationLost lost) DropForLostParts(lost);
        });
    }

    public RepairCatalog Goals => _repairs;

    public PartCatalog Parts => _parts;

    public string LastInstalled { get; private set; } = "";

    public string LastPartial { get; private set; } = "";

    public bool IsReady(string assemblyId) => _ready.ContainsKey(assemblyId);

    /// <summary>A fresh board for designing <paramref name="goalId"/>, sink chosen by the goal's conditions; null when nothing needs repair.</summary>
    public AssemblyGraph? NewBoard(string goalId, Ports.IStateQuery state)
    {
        if (!_repairs.TryGet(goalId, out var goal) || goal.ResolveSink(state) is not { } sink) return null;
        return new AssemblyGraph(_parts, goal, sink);
    }

    /// <summary>Runs the digital test without registering anything.</summary>
    public AssemblyReport Test(AssemblyGraph board) => _validator.Validate(board, _inventory);

    /// <summary>Reads state only: the refusal reason for registering <paramref name="design"/> now, or null when it will be ready.</summary>
    /// <remarks>Re-registering a ready id first returns its own reservation, so an inventory shortfall alone does not refuse it.</remarks>
    public string? Check(string assemblyId, AssemblyDesign? design)
    {
        if (string.IsNullOrWhiteSpace(assemblyId) || design is null || Restore(design) is not { } board) return DesignUnreadable;
        var report = _validator.Validate(board, _inventory);
        if (report.Passed) return null;
        return _ready.ContainsKey(assemblyId) && report.Failures.All(f => f.Check == CheckKind.Inventory) ? null : DesignFailed;
    }

    /// <returns>The validation report; null when the design does not fit its goal (unknown goal, sink, part or port).</returns>
    public AssemblyReport? Register(string assemblyId, AssemblyDesign design, MissionTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyId);
        ArgumentNullException.ThrowIfNull(design);
        if (_ready.Remove(assemblyId, out var previous)) _inventory.ReturnAll(previous.PartsUsed, now);
        if (Restore(design) is not { } board) return null;
        var report = _validator.Validate(board, _inventory);
        if (report.Task is { } spec && _inventory.ReserveAll(spec.PartsUsed, now) == InventoryResult.Ok) _ready[assemblyId] = spec;
        return report;
    }

    /// <summary>The parts reserved for the design registered under <paramref name="assemblyId"/> (the client registers each goal under its goal id); empty when none.</summary>
    public IReadOnlyList<PartQuantity> ReservedParts(string assemblyId) =>
        _ready.TryGetValue(assemblyId, out var spec) ? spec.PartsUsed : [];

    /// <returns>False for an unknown id, or when an effect took part of the reservation (the rest is returned, nothing is installed).</returns>
    public bool Install(string assemblyId, MissionTime now)
    {
        if (!TakeSettled(assemblyId, now, out var spec)) return false;
        if (_vehicles.TryFind(spec.Sink.Value, out var v, out var sink) && _vehicles[v].Layout.TryGetIndex(spec.Source.Value, out var source))
            _vehicles[v].SetJumper(sink, source, now);
        _knowledge.RecordRepair(spec.Repairs, spec.GoalId, now);
        LastInstalled = assemblyId;
        return true;
    }

    /// <returns>False for an unknown id, or when an effect took part of the reservation (the rest is returned).</returns>
    public bool Partial(string assemblyId, MissionTime now)
    {
        if (!TakeSettled(assemblyId, now, out _)) return false;
        LastPartial = assemblyId;
        return true;
    }

    /// <summary>Removes the ready design and consumes its parts, only when every part is still fully reserved.</summary>
    private bool TakeSettled(string assemblyId, MissionTime now, out RepairTaskSpec spec)
    {
        if (!_ready.Remove(assemblyId, out spec!)) return false;
        var needed = new Dictionary<PartId, int>();
        foreach (var (part, quantity) in spec.PartsUsed) needed[part] = needed.GetValueOrDefault(part) + quantity;
        foreach (var (part, quantity) in needed)
        {
            if (_inventory.Reserved(part) >= quantity) continue;
            _inventory.ReturnAll(spec.PartsUsed, now);
            return false;
        }
        return _inventory.SettleRepair(spec, succeeded: true, now) == InventoryResult.Ok;
    }

    /// <summary>Drops ready designs using the lost part until the loss is covered, returning the rest of their reservations.</summary>
    private void DropForLostParts(InventoryReservationLost lost)
    {
        var deficit = lost.Quantity;
        var users = new List<string>();
        foreach (var (id, spec) in _ready)
        {
            if (QuantityOf(spec, lost.Part) > 0) users.Add(id);
        }
        foreach (var id in users)
        {
            if (deficit <= 0) break;
            var spec = _ready[id];
            _ready.Remove(id);
            var absorbed = Math.Min(deficit, QuantityOf(spec, lost.Part));
            deficit -= absorbed;
            var returned = new List<PartQuantity>(spec.PartsUsed.Count);
            foreach (var (part, quantity) in spec.PartsUsed)
            {
                if (part != lost.Part) returned.Add(new PartQuantity(part, quantity));
            }
            var keep = QuantityOf(spec, lost.Part) - absorbed;
            if (keep > 0) returned.Add(new PartQuantity(lost.Part, keep));
            if (returned.Count > 0) _inventory.ReturnAll(returned, lost.Time);
            _events.Publish(new RepairReservationLost(lost.Time, id, lost.Part));
        }
    }

    private static int QuantityOf(RepairTaskSpec spec, PartId part)
    {
        var total = 0;
        foreach (var (p, quantity) in spec.PartsUsed)
        {
            if (p == part) total += quantity;
        }
        return total;
    }

    public void WriteHash(StateHashWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        foreach (var (id, spec) in _ready)
        {
            writer.Write(id);
            writer.Write(spec.Sink.Value);
        }
        writer.Write(LastInstalled);
        writer.Write(LastPartial);
    }

    private AssemblyGraph? Restore(AssemblyDesign design) =>
        _repairs.TryGet(design.GoalId, out var goal) && AssemblyGraph.TryRestore(_parts, goal, design, out var board).Succeeded ? board : null;
}
