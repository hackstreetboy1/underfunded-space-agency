using Usa.Simulation.Kernel;
using Usa.Simulation.Vehicle;
using Usa.Simulation.Vehicle.Definitions;
using Usa.Simulation.Vehicle.Propulsion;
using Usa.Simulation.Vehicle.Sensors;

namespace Usa.Simulation.Session;

/// <summary>The mission's vehicles in id order, with the per-vehicle models adapters share.</summary>
public sealed class VehicleSet
{
    private readonly VehicleState[] _states;
    private readonly PropulsionModel[] _propulsion;
    private readonly SensorModel[] _sensors;

    public VehicleSet(IReadOnlyList<VehicleDefinition> definitions, EventBus events)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _states = definitions
            .OrderBy(d => d.Id, StringComparer.Ordinal)
            .Select(d => new VehicleState(new VehicleLayout(d), events))
            .ToArray();
        _propulsion = _states.Select(s => new PropulsionModel(s)).ToArray();
        _sensors = _states.Select(s => new SensorModel(s)).ToArray();
    }

    /// <summary>Sorted by vehicle id.</summary>
    public IReadOnlyList<VehicleState> States => _states;

    public int Count => _states.Length;

    public VehicleState this[int index] => _states[index];

    public VehicleState Get(string vehicleId) => _states[IndexOf(vehicleId)];

    public PropulsionModel Propulsion(int index) => _propulsion[index];

    public PropulsionModel Propulsion(string vehicleId) => _propulsion[IndexOf(vehicleId)];

    public SensorModel Sensors(int index) => _sensors[index];

    public bool Contains(string vehicleId) => FindIndex(vehicleId) >= 0;

    public int IndexOf(string vehicleId)
    {
        var index = FindIndex(vehicleId);
        return index >= 0 ? index : throw new ArgumentException($"Unknown vehicle '{vehicleId}'.", nameof(vehicleId));
    }

    /// <summary>Finds the vehicle and dense index of a component id such as <c>tandem/oams/thruster_08</c>.</summary>
    public bool TryFind(string componentId, out int vehicle, out int component)
    {
        ArgumentNullException.ThrowIfNull(componentId);
        for (var v = 0; v < _states.Length; v++)
        {
            if (!componentId.StartsWith(_states[v].Layout.PathPrefix, StringComparison.Ordinal)) continue;
            if (_states[v].Layout.TryGetIndex(componentId, out component))
            {
                vehicle = v;
                return true;
            }
        }
        vehicle = -1;
        component = -1;
        return false;
    }

    public bool IsKnownComponent(ComponentId id) => TryFind(id.Value, out _, out _);

    /// <summary>Every component id across the vehicles, in vehicle then index order.</summary>
    public IEnumerable<ComponentId> AllComponents()
    {
        foreach (var state in _states)
        {
            for (var i = 0; i < state.Layout.Count; i++) yield return state.Layout.IdOf(i);
        }
    }

    private int FindIndex(string vehicleId)
    {
        for (var v = 0; v < _states.Length; v++)
        {
            if (string.Equals(_states[v].VehicleId, vehicleId, StringComparison.Ordinal)) return v;
        }
        return -1;
    }
}
