using Usa.Simulation.Data;
using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;
using Usa.Simulation.Vehicle.Definitions;

namespace Usa.Simulation.Vehicle;

/// <summary>
/// Immutable, compiled form of a <see cref="VehicleDefinition"/>: components get dense indices in ordinal id order,
/// references are resolved to indices and family members are listed for the solvers.
/// </summary>
public sealed class VehicleLayout
{
    private const int None = -1;

    private readonly ComponentDefinition[] _components;
    private readonly Dictionary<string, int> _indexById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> _indexBySpan;
    private readonly int[] _sectionOf;
    private readonly int[] _tankOf;
    private readonly int[] _driverOf;
    private readonly int[] _groupOf;
    private readonly int[] _fromOf;
    private readonly int[] _toOf;
    private readonly int[] _busOf;
    private readonly int[] _heatNodeOf;
    private readonly int[] _o2TankOf;
    private readonly int[] _h2TankOf;
    private readonly int[] _waterTankOf;
    private readonly int[][] _members;
    private readonly Vec3d[] _positionM;
    private readonly Vec3d[] _directionUnit;
    private readonly Vec3d[] _sectionCenterOfMassM;
    private readonly int[] _thrusters;
    private readonly int[] _engines;
    private readonly int[] _groups;
    private readonly int[] _tanks;
    private readonly int[] _switches;
    private readonly int[] _buses;
    private readonly int[] _fuelCells;
    private readonly int[] _batteries;
    private readonly int[] _loads;
    private readonly int[] _shedOrder;
    private readonly int[] _sensors;
    private readonly int[] _harnesses;
    private readonly int[] _outlets;
    private readonly int[] _dockingPorts;
    private readonly int[] _parachutes;
    private readonly int[] _retroRockets;
    private readonly int[] _thermalNodes;

    /// <exception cref="DefinitionException">The definition is invalid.</exception>
    public VehicleLayout(VehicleDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = new DefinitionErrors();
        VehicleDefinitionValidator.Validate(definition, $"vehicle '{definition.Id}'", errors);
        errors.ThrowIfAny();

        Definition = definition;
        PathPrefix = definition.Id + "/";
        _components = definition.Components.OrderBy(c => c.Id.Value, StringComparer.Ordinal).ToArray();
        for (var i = 0; i < _components.Length; i++) _indexById.Add(_components[i].Id.Value, i);
        _indexBySpan = _indexById.GetAlternateLookup<ReadOnlySpan<char>>();

        var count = _components.Length;
        _sectionOf = new int[count];
        _tankOf = Filled(count);
        _driverOf = Filled(count);
        _groupOf = Filled(count);
        _fromOf = Filled(count);
        _toOf = Filled(count);
        _busOf = Filled(count);
        _heatNodeOf = Filled(count);
        _o2TankOf = Filled(count);
        _h2TankOf = Filled(count);
        _waterTankOf = Filled(count);
        _positionM = new Vec3d[count];
        _directionUnit = new Vec3d[count];

        var sectionIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var s = 0; s < definition.Sections.Count; s++) sectionIndex.Add(definition.Sections[s].Id, s);
        _sectionCenterOfMassM = definition.Sections.Select(s => ToVec(s.CenterOfMassM)).ToArray();

        for (var i = 0; i < count; i++) Resolve(i, sectionIndex);

        _thrusters = Where(c => c.Thruster is not null);
        _engines = Where(c => c.Engine is not null);
        _groups = Where(c => c.ThrusterGroup is not null);
        _tanks = Where(c => c.Tank is not null);
        _switches = Where(c => c.Breaker is not null || c.Switch is not null);
        _buses = Where(c => c.Bus is not null);
        _fuelCells = Where(c => c.FuelCell is not null);
        _batteries = Where(c => c.Battery is not null);
        _loads = Where(c => c.Load is not null);
        _sensors = Where(c => c.Sensor is not null);
        _harnesses = Where(c => c.Harness is not null);
        _outlets = Where(c => c.Outlet is not null);
        _dockingPorts = Where(c => c.Docking is not null);
        _parachutes = Where(c => c.Parachute is not null);
        _retroRockets = Where(c => c.RetroRocket is not null);
        _thermalNodes = Where(c => c.ThermalNode is not null);

        // Brownout sheds the lowest priority first, then the highest index, so the order is fixed by data alone.
        _shedOrder = _loads
            .OrderByDescending(i => (int)_components[i].Load!.Priority)
            .ThenByDescending(i => i)
            .ToArray();

        _members = new int[count][];
        for (var i = 0; i < count; i++)
        {
            var group = i;
            _members[i] = _components[i].ThrusterGroup is null ? [] : _thrusters.Where(t => _groupOf[t] == group).ToArray();
        }
    }

    public VehicleDefinition Definition { get; }

    public string VehicleId => Definition.Id;

    /// <summary>State path prefix of this vehicle's components, e.g. <c>tandem/</c>.</summary>
    public string PathPrefix { get; }

    public int Count => _components.Length;

    public ComponentDefinition this[int index] => _components[index];

    public IReadOnlyList<SectionDefinition> Sections => Definition.Sections;

    public ComponentId IdOf(int index) => _components[index].Id;

    public bool TryGetIndex(string componentId, out int index) => _indexById.TryGetValue(componentId, out index);

    public bool TryGetIndex(ReadOnlySpan<char> componentId, out int index) => _indexBySpan.TryGetValue(componentId, out index);

    /// <exception cref="InvalidOperationException">The component is not in this vehicle.</exception>
    public int IndexOf(string componentId) =>
        _indexById.TryGetValue(componentId, out var index)
            ? index
            : throw new InvalidOperationException($"Component '{componentId}' is not part of vehicle '{VehicleId}'.");

    public int IndexOf(ComponentId componentId) => IndexOf(componentId.Value);

    /// <summary>Index of the section with jettison event <paramref name="eventId"/>, or -1.</summary>
    public int SectionWithJettison(string eventId)
    {
        for (var s = 0; s < Definition.Sections.Count; s++)
        {
            if (string.Equals(Definition.Sections[s].Jettison, eventId, StringComparison.Ordinal)) return s;
        }
        return None;
    }

    public int SectionIndex(string sectionId)
    {
        for (var s = 0; s < Definition.Sections.Count; s++)
        {
            if (string.Equals(Definition.Sections[s].Id, sectionId, StringComparison.Ordinal)) return s;
        }
        return None;
    }

    public Vec3d SectionCenterOfMassM(int section) => _sectionCenterOfMassM[section];

    public int SectionOf(int index) => _sectionOf[index];

    /// <summary>Thruster or engine spec of a component, or null.</summary>
    public ThrusterSpec? ThrusterSpecOf(int index) => _components[index].Thruster ?? _components[index].Engine;

    /// <summary>Breaker or switch spec of a component, or null.</summary>
    public SwitchSpec? SwitchSpecOf(int index) => _components[index].Breaker ?? _components[index].Switch;

    /// <summary>Propellant tank of a thruster or engine.</summary>
    public int TankOf(int index) => _tankOf[index];

    /// <summary>Driver of a thruster or engine; -1 when it has none.</summary>
    public int DriverOf(int index) => _driverOf[index];

    /// <summary>Thruster group of a thruster; -1 when ungrouped.</summary>
    public int GroupOf(int index) => _groupOf[index];

    /// <summary>Upstream end of a switch, breaker or harness.</summary>
    public int FromOf(int index) => _fromOf[index];

    /// <summary>Downstream end of a switch, breaker or harness; -1 for junction switches.</summary>
    public int ToOf(int index) => _toOf[index];

    /// <summary>Bus a load, fuel cell, battery or outlet connects to directly; -1 when fed through a switch.</summary>
    public int BusOf(int index) => _busOf[index];

    /// <summary>Thermal node receiving the component's heat; -1 when none.</summary>
    public int HeatNodeOf(int index) => _heatNodeOf[index];

    public int O2TankOf(int fuelCell) => _o2TankOf[fuelCell];

    public int H2TankOf(int fuelCell) => _h2TankOf[fuelCell];

    public int WaterTankOf(int fuelCell) => _waterTankOf[fuelCell];

    /// <summary>Thrusters belonging to a thruster group, in index order.</summary>
    public ReadOnlySpan<int> MembersOf(int group) => _members[group];

    /// <summary>Position of a thruster, engine, retro rocket or tank contents.</summary>
    public Vec3d PositionM(int index) => _positionM[index];

    /// <summary>Force direction of a thruster, engine or retro rocket.</summary>
    public Vec3d DirectionUnit(int index) => _directionUnit[index];

    public ReadOnlySpan<int> Thrusters => _thrusters;

    public ReadOnlySpan<int> Engines => _engines;

    public ReadOnlySpan<int> ThrusterGroups => _groups;

    public ReadOnlySpan<int> Tanks => _tanks;

    /// <summary>Breakers and switches.</summary>
    public ReadOnlySpan<int> Switches => _switches;

    public ReadOnlySpan<int> Buses => _buses;

    public ReadOnlySpan<int> FuelCells => _fuelCells;

    public ReadOnlySpan<int> Batteries => _batteries;

    /// <summary>Every component with a <see cref="LoadSpec"/>.</summary>
    public ReadOnlySpan<int> Loads => _loads;

    /// <summary>Loads in brownout shedding order (discretionary first, survival last).</summary>
    public ReadOnlySpan<int> ShedOrder => _shedOrder;

    public ReadOnlySpan<int> Sensors => _sensors;

    public ReadOnlySpan<int> Harnesses => _harnesses;

    public ReadOnlySpan<int> Outlets => _outlets;

    public ReadOnlySpan<int> DockingPorts => _dockingPorts;

    public ReadOnlySpan<int> Parachutes => _parachutes;

    public ReadOnlySpan<int> RetroRockets => _retroRockets;

    public ReadOnlySpan<int> ThermalNodes => _thermalNodes;

    private void Resolve(int i, Dictionary<string, int> sectionIndex)
    {
        var c = _components[i];
        _sectionOf[i] = sectionIndex[c.Section];

        if ((c.Thruster ?? c.Engine) is { } thruster)
        {
            _tankOf[i] = IndexOf(thruster.Tank);
            _driverOf[i] = Optional(thruster.Driver);
            _groupOf[i] = Optional(thruster.Group);
            _positionM[i] = ToVec(thruster.PositionM);
            _directionUnit[i] = ToVec(thruster.DirectionUnit);
        }
        if (c.Tank is { } tank) _positionM[i] = ToVec(tank.PositionM);
        if (c.RetroRocket is { } retro)
        {
            _positionM[i] = ToVec(retro.PositionM);
            _directionUnit[i] = ToVec(retro.DirectionUnit);
        }
        if ((c.Breaker ?? c.Switch) is { } sw)
        {
            _fromOf[i] = IndexOf(sw.From);
            _toOf[i] = Optional(sw.To);
        }
        if (c.Harness is { } harness)
        {
            _fromOf[i] = IndexOf(harness.From);
            _toOf[i] = IndexOf(harness.To);
        }
        if (c.Load is { } load)
        {
            _busOf[i] = Optional(load.Bus);
            _heatNodeOf[i] = Optional(load.ThermalNode);
        }
        if (c.FuelCell is { } cell)
        {
            _busOf[i] = IndexOf(cell.Bus);
            _heatNodeOf[i] = Optional(cell.ThermalNode);
            _o2TankOf[i] = IndexOf(cell.O2Tank);
            _h2TankOf[i] = IndexOf(cell.H2Tank);
            _waterTankOf[i] = IndexOf(cell.WaterTank);
        }
        if (c.Battery is { } battery)
        {
            _busOf[i] = IndexOf(battery.Bus);
            _heatNodeOf[i] = Optional(battery.ThermalNode);
        }
        if (c.Outlet is { } outlet) _busOf[i] = IndexOf(outlet.Bus);
    }

    private int Optional(ComponentId? id) => id is { } value ? IndexOf(value) : None;

    private int[] Where(Func<ComponentDefinition, bool> predicate)
    {
        var result = new List<int>();
        for (var i = 0; i < _components.Length; i++)
        {
            if (predicate(_components[i])) result.Add(i);
        }
        return result.ToArray();
    }

    private static int[] Filled(int count)
    {
        var array = new int[count];
        Array.Fill(array, None);
        return array;
    }

    private static Vec3d ToVec(IReadOnlyList<double> values) => new(values[0], values[1], values[2]);
}
