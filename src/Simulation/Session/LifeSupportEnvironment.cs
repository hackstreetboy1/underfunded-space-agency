using Usa.Simulation.Crew;
using Usa.Simulation.Kernel;
using Usa.Simulation.LifeSupport;
using Usa.Simulation.LifeSupport.Consumables;
using Usa.Simulation.Vehicle;
using Usa.Simulation.Vehicle.Definitions;

namespace Usa.Simulation.Session;

/// <summary>
/// The life-support adapter (1 s, just before <see cref="LifeSupportSystem"/>'s own step logic runs).
/// <para>Inputs: electrical heat released into each thermal node component (heaters included, so heater power is not added
/// twice), radiator effectiveness (1 while attached and operable, else 0), fan airflow (powered operable fans over all fans),
/// regulator supply (primary plus secondary tank) and state, crew location/activity/alive from the roster (each member breathes
/// in the cabin of their current location; suited crew breathe from their suit, task work counts as <see cref="CrewActivity.Work"/>),
/// fuel-cell product water, which is moved out of the vehicle water tank into the potable supply, and the Sun
/// (<see cref="SolarEnvironment"/>: sunlit fraction and per-node solar heating) when the mission has a sun model.</para>
/// <para>Outputs: the O2 the regulators drew comes out of the vehicle tanks (primary first).</para>
/// <para>Heaters run a thermostat: on below setpoint minus hysteresis, off above the setpoint.</para>
/// </summary>
internal sealed class LifeSupportEnvironment : ILifeSupportEnvironment, IHashContributor
{
    private readonly VehicleSet _vehicles;
    private readonly CrewRoster _roster;
    private (int Vehicle, int Component)[] _nodes = [];
    private (int Vehicle, int Component)[] _radiators = [];
    private CabinLink[] _cabins = [];
    private HeaterLink[] _heaters = [];
    private (int Vehicle, int Tank)[] _waterTanks = [];
    private double[] _lastWaterKg = [];
    private CrewMember[] _crew = [];
    private LifeSupportSystem? _system;

    /// <summary>Per roster location (<see cref="CrewRoster.Locations"/> order): the life-support cabin index, -1 when suited.</summary>
    private int[] _locationCabins = [];

    public LifeSupportEnvironment(VehicleSet vehicles, CrewRoster roster)
    {
        _vehicles = vehicles;
        _roster = roster;
    }

    /// <summary>Optional sun model; without one the sunlit fraction and per-node solar inputs keep their defaults.</summary>
    public SolarEnvironment? Solar { get; set; }

    /// <summary>Resolves component indices once the life-support models exist.</summary>
    public void Attach(LifeSupportSystem system)
    {
        _system = system;
        var thermal = system.Thermal;
        _nodes = new (int, int)[thermal.NodeCount];
        for (var n = 0; n < _nodes.Length; n++) _nodes[n] = Find(thermal.NodeId(n).Value);
        _radiators = new (int, int)[thermal.RadiatorCount];
        for (var r = 0; r < _radiators.Length; r++) _radiators[r] = Find(thermal.RadiatorId(r).Value);

        _cabins = new CabinLink[system.Cabins.Count];
        for (var c = 0; c < _cabins.Length; c++)
        {
            var vehicle = _vehicles.IndexOf(system.Cabins[c].Id);
            var layout = _vehicles[vehicle].Layout;
            var fans = new List<int>();
            var regulators = new List<int>();
            for (var i = 0; i < layout.Count; i++)
            {
                if (layout[i].Fan is not null) fans.Add(i);
                if (layout[i].Regulator is not null) regulators.Add(i);
            }
            _cabins[c] = new CabinLink(vehicle, [.. fans], [.. regulators]);
        }
        var locations = _roster.Locations;
        _locationCabins = new int[locations.Count];
        for (var l = 0; l < locations.Count; l++)
        {
            _locationCabins[l] = -1;
            if (locations[l].Cabin is not { } cabinVehicle) continue;
            for (var c = 0; c < system.Cabins.Count; c++)
            {
                if (system.Cabins[c].Id == cabinVehicle) _locationCabins[l] = c;
            }
            if (_locationCabins[l] < 0)
                throw new InvalidOperationException($"Crew location '{locations[l].Id}' names cabin '{cabinVehicle}', which no vehicle provides.");
        }

        var heaters = new List<HeaterLink>();
        var water = new List<(int, int)>();
        for (var v = 0; v < _vehicles.Count; v++)
        {
            var layout = _vehicles[v].Layout;
            for (var i = 0; i < layout.Count; i++)
            {
                if (layout[i].Heater is { } heater && layout[i].Load is not null)
                {
                    var node = thermal.IndexOfNode(heater.Node);
                    if (node >= 0) heaters.Add(new HeaterLink(v, i, node, heater.SetpointC, heater.HysteresisC));
                }
            }
            foreach (var cell in layout.FuelCells)
            {
                var tank = layout.WaterTankOf(cell);
                if (tank >= 0 && !water.Contains((v, tank))) water.Add((v, tank));
            }
        }
        _heaters = [.. heaters];
        _waterTanks = [.. water];
        _lastWaterKg = _waterTanks.Select(t => _vehicles[t.Item1][t.Item2].QuantityKg).ToArray();

        var provisions = system.Provisions;
        _crew = new CrewMember[provisions.CrewCount];
        for (var i = 0; i < _crew.Length; i++) _crew[i] = _roster[provisions.CrewAt(i)];
    }

    public void ReadInputs(MissionTime now, LifeSupportFrame frame)
    {
        var system = _system ?? throw new InvalidOperationException("Attach the life-support system first.");
        var thermal = frame.Thermal;
        var load = thermal.LoadHeatW;
        for (var n = 0; n < _nodes.Length; n++)
        {
            var (v, i) = _nodes[n];
            load[n] = v >= 0 && _vehicles[v].IsAttached(i) ? _vehicles[v][i].HeatW : 0;
        }
        thermal.HeaterW.Clear();

        var radiators = thermal.RadiatorEffectiveness;
        for (var r = 0; r < _radiators.Length; r++)
        {
            var (v, i) = _radiators[r];
            radiators[r] = v >= 0 && _vehicles[v].IsOperable(i) ? 1 : 0;
        }

        RunThermostats(system, now);
        Solar?.Fill(system.Thermal, thermal);
        ReadCabins(frame);
        ReadCrew(frame);
        frame.WaterIncomeL = CollectFuelCellWater(now);
    }

    public void ApplyOutputs(MissionTime now, LifeSupportFrame frame)
    {
        var results = frame.CabinResults;
        for (var c = 0; c < _cabins.Length; c++)
        {
            var remaining = results[c].O2DrawnKg;
            var state = _vehicles[_cabins[c].Vehicle];
            foreach (var regulator in _cabins[c].Regulators)
            {
                if (remaining <= 0) break;
                remaining -= DrawFrom(state, state.Layout[regulator].Regulator!.Tank, remaining, now);
                if (remaining > 0 && state.Layout[regulator].Regulator!.SecondaryTank is { } secondary)
                    remaining -= DrawFrom(state, secondary, remaining, now);
            }
        }
    }

    public void WriteHash(StateHashWriter writer)
    {
        foreach (var kg in _lastWaterKg) writer.Write(kg);
    }

    private void ReadCabins(LifeSupportFrame frame)
    {
        var fan = frame.FanAirflowFraction;
        var supply = frame.O2SupplyAvailableKg;
        var regulatorOk = frame.RegulatorEnabled;
        for (var c = 0; c < _cabins.Length; c++)
        {
            var link = _cabins[c];
            var state = _vehicles[link.Vehicle];
            var total = 0.0;
            var running = 0.0;
            foreach (var f in link.Fans)
            {
                var airflow = state.Layout[f].Fan!.AirflowM3PerS;
                total += airflow;
                if (state.IsOperable(f) && (state.Layout[f].Load is null || state[f].Powered)) running += airflow;
            }
            fan[c] = total > 0 ? running / total : 0;

            var available = 0.0;
            var enabled = false;
            foreach (var r in link.Regulators)
            {
                var spec = state.Layout[r].Regulator!;
                if (!state.IsOperable(r)) continue;
                enabled = true;
                available += Contents(state, spec.Tank);
                if (spec.SecondaryTank is { } secondary) available += Contents(state, secondary);
            }
            supply[c] = available;
            regulatorOk[c] = enabled;
        }
    }

    private void ReadCrew(LifeSupportFrame frame)
    {
        var activity = frame.CrewActivities;
        var cabin = frame.CrewCabin;
        var alive = frame.CrewAlive;
        for (var i = 0; i < _crew.Length; i++)
        {
            var member = _crew[i];
            alive[i] = member.IsAlive;
            var onEva = member.Location == CrewLocation.Eva;
            cabin[i] = _locationCabins[member.LocationIndex];
            activity[i] = onEva ? CrewActivity.Eva : member.IsBusy ? CrewActivity.Work : CrewActivity.Nominal;
        }
    }

    private void RunThermostats(LifeSupportSystem system, MissionTime now)
    {
        foreach (var heater in _heaters)
        {
            var tempC = system.Thermal.TemperatureC(heater.Node);
            var state = _vehicles[heater.Vehicle];
            if (tempC < heater.SetpointC - heater.HysteresisC) state.SetOn(heater.Component, true, now);
            else if (tempC > heater.SetpointC) state.SetOn(heater.Component, false, now);
        }
    }

    private double CollectFuelCellWater(MissionTime now)
    {
        var litres = 0.0;
        for (var t = 0; t < _waterTanks.Length; t++)
        {
            var (v, tank) = _waterTanks[t];
            var state = _vehicles[v];
            var produced = state[tank].QuantityKg - _lastWaterKg[t];
            if (produced > 0) litres += state.DrawFromTank(tank, produced, now);
            _lastWaterKg[t] = state[tank].QuantityKg;
        }
        return litres;
    }

    private static double Contents(VehicleState state, ComponentId tank) =>
        state.Layout.TryGetIndex(tank.Value, out var index) && state.IsAttached(index) && state[index].Condition != ComponentCondition.Isolated
            ? state[index].QuantityKg
            : 0;

    private static double DrawFrom(VehicleState state, ComponentId tank, double kg, MissionTime now) =>
        state.Layout.TryGetIndex(tank.Value, out var index) ? state.DrawFromTank(index, kg, now) : 0;

    private (int Vehicle, int Component) Find(string componentId) =>
        _vehicles.TryFind(componentId, out var v, out var i) ? (v, i) : (-1, -1);

    private sealed record CabinLink(int Vehicle, int[] Fans, int[] Regulators);

    private sealed record HeaterLink(int Vehicle, int Component, int Node, double SetpointC, double HysteresisC);
}
