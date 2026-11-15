using Usa.Simulation.Kernel;
using Usa.Simulation.Vehicle.Definitions;

namespace Usa.Simulation.Vehicle.Power;

/// <summary>
/// Electrical network of one vehicle, stepped once per second.
/// <para>Connectivity: union-find over components, joined by closed operable breakers/switches, intact harnesses,
/// outlets, repair jumpers and the direct bus connections of loads and sources. Rebuilt only when
/// <see cref="VehicleState.TopologyVersion"/> changes.</para>
/// <para>Per net: demand = Σ enabled operable loads; capacity = fuel cells (rated × efficiency, capped by the reactant
/// left in attached, non-isolated tanks for the step) + online batteries (up to max discharge). Utilisation u = demand / capacity gives
/// V = nominal − droop·u for u ≤ 1 and (nominal − droop) / u above, where delivered power is capped at capacity.
/// While V would fall below the bus brownout voltage, loads are shed in <see cref="VehicleLayout.ShedOrder"/>.
/// Fuel cells carry the load first, batteries the remainder; surplus fuel-cell capacity charges batteries that accept
/// charge. Fuel cells draw O2/H2 and produce their mass as water; powered loads release heat into their thermal node.</para>
/// </summary>
public sealed class ElectricalSolver : ISimSystem
{
    // Water is 16/18 oxygen by mass, so a fuel cell burns O2 and H2 at 8:1.
    private const double O2MassFraction = 8.0 / 9.0;

    private readonly VehicleState _state;
    private readonly VehicleLayout _layout;
    private readonly int[] _parent;
    private readonly int[] _net;
    private readonly bool[] _hasBus;
    private readonly double[] _nominalV;
    private readonly double[] _droopV;
    private readonly double[] _brownoutV;
    private readonly double[] _demandW;
    private readonly double[] _fuelCellCapacityW;
    private readonly double[] _batteryCapacityW;
    private readonly double[] _voltageV;
    private readonly double[] _servedFraction;
    private readonly double[] _fuelCellOutputW;
    private readonly double[] _batteryOutputW;
    private readonly double[] _surplusW;
    private readonly double[] _sourceCapacityW;
    private readonly double[] _chargeW;
    private readonly double[] _reactantDemandKg;
    private readonly bool[] _candidate;
    private readonly bool[] _shed;
    private long _builtVersion = long.MinValue;

    public ElectricalSolver(VehicleState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        _layout = state.Layout;
        var n = _layout.Count;
        _parent = new int[n];
        _net = new int[n];
        _hasBus = new bool[n];
        _nominalV = new double[n];
        _droopV = new double[n];
        _brownoutV = new double[n];
        _demandW = new double[n];
        _fuelCellCapacityW = new double[n];
        _batteryCapacityW = new double[n];
        _voltageV = new double[n];
        _servedFraction = new double[n];
        _fuelCellOutputW = new double[n];
        _batteryOutputW = new double[n];
        _surplusW = new double[n];
        _sourceCapacityW = new double[n];
        _chargeW = new double[n];
        _reactantDemandKg = new double[n];
        _candidate = new bool[n];
        _shed = new bool[n];

        // Settle the initial bus voltages and powered flags without consuming anything or raising events.
        Solve(1.0, MissionTime.Zero, integrate: false);
    }

    public int PeriodTicks => SimulationClock.TicksPerSecond;

    public void Step(in StepContext context) => Solve(context.DeltaSeconds, context.Now, integrate: true);

    private void Solve(double deltaSeconds, MissionTime now, bool integrate)
    {
        if (_state.TopologyVersion != _builtVersion) Rebuild();
        ClearNets();
        CollectBuses();
        CollectSources(deltaSeconds);
        CollectLoads();
        ShedLoads();
        SolveNets();
        ChargeBatteries(deltaSeconds);
        foreach (var node in _layout.ThermalNodes) _state.Mutable(node).HeatW = 0;

        var generationW = DispatchFuelCells(deltaSeconds, now, integrate) + DischargeBatteries(deltaSeconds, integrate);
        _state.PowerGenerationW = generationW;
        _state.PowerLoadW = UpdateLoads(now, integrate);
        UpdateBuses(now, integrate);
    }

    private void ClearNets()
    {
        Array.Clear(_hasBus);
        Array.Clear(_nominalV);
        Array.Clear(_droopV);
        Array.Clear(_brownoutV);
        Array.Clear(_demandW);
        Array.Clear(_fuelCellCapacityW);
        Array.Clear(_batteryCapacityW);
        Array.Clear(_chargeW);
        Array.Clear(_sourceCapacityW);
        Array.Clear(_reactantDemandKg);
    }

    private void CollectBuses()
    {
        foreach (var bus in _layout.Buses)
        {
            if (!_state.IsOperable(bus)) continue;
            var net = _net[bus];
            var spec = _layout[bus].Bus!;
            // A net joining several buses (bus tie closed) takes the highest-rated bus's curve.
            if (_hasBus[net] && spec.NominalV <= _nominalV[net]) continue;
            _hasBus[net] = true;
            _nominalV[net] = spec.NominalV;
            _droopV[net] = spec.DroopV;
            _brownoutV[net] = spec.BrownoutV;
        }
    }

    private void CollectSources(double deltaSeconds)
    {
        foreach (var cell in _layout.FuelCells)
        {
            ref readonly var state = ref _state[cell];
            if (!_state.IsOperable(cell) || !state.Online || !HasGas(_layout.O2TankOf(cell)) || !HasGas(_layout.H2TankOf(cell))) continue;
            var spec = _layout[cell].FuelCell!;
            var capacity = spec.MaxPowerW * state.Efficiency;
            _sourceCapacityW[cell] = capacity;
            var reactantKg = ReactantKg(capacity, deltaSeconds, spec);
            _reactantDemandKg[_layout.O2TankOf(cell)] += reactantKg * O2MassFraction;
            _reactantDemandKg[_layout.H2TankOf(cell)] += reactantKg * (1 - O2MassFraction);
        }

        // Cells sharing a tank split what it holds pro rata, so capacity never exceeds the reactant left for this step.
        foreach (var cell in _layout.FuelCells)
        {
            if (_sourceCapacityW[cell] <= 0) continue;
            var supplied = Math.Min(SupplyFraction(_layout.O2TankOf(cell)), SupplyFraction(_layout.H2TankOf(cell)));
            var capacity = _sourceCapacityW[cell] * supplied;
            _sourceCapacityW[cell] = capacity;
            _fuelCellCapacityW[_net[cell]] += capacity;
        }

        foreach (var battery in _layout.Batteries)
        {
            ref readonly var state = ref _state[battery];
            if (!_state.IsOperable(battery) || !state.Online || state.EnergyKwh <= 0) continue;
            var capacity = Math.Min(_layout[battery].Battery!.MaxDischargeW, VehicleState.JoulesFromKwh(state.EnergyKwh) / deltaSeconds);
            _sourceCapacityW[battery] = capacity;
            _batteryCapacityW[_net[battery]] += capacity;
        }
    }

    private void CollectLoads()
    {
        foreach (var load in _layout.Loads)
        {
            var net = _net[load];
            var candidate = _state.IsOperable(load) && _state[load].On && _hasBus[net];
            _candidate[load] = candidate;
            _shed[load] = false;
            if (candidate) _demandW[net] += _layout[load].Load!.PowerW;
        }
    }

    private void ShedLoads()
    {
        foreach (var load in _layout.ShedOrder)
        {
            if (!_candidate[load]) continue;
            var net = _net[load];
            var capacity = _fuelCellCapacityW[net] + _batteryCapacityW[net];
            var limitW = capacity > 0 ? capacity * (_nominalV[net] - _droopV[net]) / _brownoutV[net] : 0;
            if (_demandW[net] <= limitW) continue;
            _candidate[load] = false;
            _shed[load] = true;
            _demandW[net] -= _layout[load].Load!.PowerW;
        }
    }

    private void SolveNets()
    {
        for (var net = 0; net < _net.Length; net++)
        {
            if (_net[net] != net) continue;
            var capacity = _fuelCellCapacityW[net] + _batteryCapacityW[net];
            var demand = _demandW[net];
            var served = 0.0;
            if (!_hasBus[net] || capacity <= 0)
            {
                _voltageV[net] = 0;
            }
            else
            {
                var utilisation = demand / capacity;
                _voltageV[net] = utilisation <= 1
                    ? _nominalV[net] - _droopV[net] * utilisation
                    : (_nominalV[net] - _droopV[net]) / utilisation;
                served = Math.Min(demand, capacity);
            }

            _servedFraction[net] = demand > 0 ? served / demand : 1;
            _fuelCellOutputW[net] = Math.Min(served, _fuelCellCapacityW[net]);
            _batteryOutputW[net] = served - _fuelCellOutputW[net];
            _surplusW[net] = _fuelCellCapacityW[net] - _fuelCellOutputW[net];
        }
    }

    private void ChargeBatteries(double deltaSeconds)
    {
        foreach (var battery in _layout.Batteries)
        {
            var spec = _layout[battery].Battery!;
            ref var state = ref _state.Mutable(battery);
            var net = _net[battery];
            if (spec.MaxChargeW <= 0 || !_state.IsOperable(battery) || !state.Online || _surplusW[net] <= 0) continue;
            var roomW = VehicleState.JoulesFromKwh(spec.CapacityKwh - state.EnergyKwh) / deltaSeconds;
            var charge = Math.Min(spec.MaxChargeW, Math.Min(_surplusW[net], roomW));
            if (charge <= 0) continue;
            _chargeW[battery] = charge;
            _surplusW[net] -= charge;
            _fuelCellOutputW[net] += charge;
        }
    }

    private double DispatchFuelCells(double deltaSeconds, MissionTime now, bool integrate)
    {
        var totalW = 0.0;
        foreach (var cell in _layout.FuelCells)
        {
            var net = _net[cell];
            var outputW = _fuelCellCapacityW[net] > 0 ? _sourceCapacityW[cell] * _fuelCellOutputW[net] / _fuelCellCapacityW[net] : 0;
            var spec = _layout[cell].FuelCell!;

            if (integrate && outputW > 0)
            {
                var reactantKg = ReactantKg(outputW, deltaSeconds, spec);
                var o2Kg = _state.DrawFromTank(_layout.O2TankOf(cell), reactantKg * O2MassFraction, now);
                var h2Kg = _state.DrawFromTank(_layout.H2TankOf(cell), reactantKg * (1 - O2MassFraction), now);
                // Capacity is already capped by the reactant left, so this only guards rounding.
                var producedFraction = Math.Min(1, Math.Min(o2Kg / (reactantKg * O2MassFraction), h2Kg / (reactantKg * (1 - O2MassFraction))));
                outputW *= producedFraction;
                // Mass is conserved: the product water weighs what the reactants that combined weighed.
                var waterKg = reactantKg * producedFraction;
                _state.AddToTank(_layout.WaterTankOf(cell), waterKg, now);
            }

            _state.Mutable(cell).PowerW = outputW;
            AddHeat(_layout.HeatNodeOf(cell), outputW * spec.WasteHeatFraction);
            totalW += outputW;
        }
        return totalW;
    }

    private double DischargeBatteries(double deltaSeconds, bool integrate)
    {
        var totalW = 0.0;
        foreach (var battery in _layout.Batteries)
        {
            var net = _net[battery];
            var dischargeW = _batteryCapacityW[net] > 0 ? _sourceCapacityW[battery] * _batteryOutputW[net] / _batteryCapacityW[net] : 0;
            ref var state = ref _state.Mutable(battery);
            state.PowerW = dischargeW - _chargeW[battery];
            if (integrate) _state.SetEnergyKwh(battery, state.EnergyKwh - VehicleState.KwhFromJoules(state.PowerW * deltaSeconds));
            totalW += dischargeW;
        }
        return totalW;
    }

    private double UpdateLoads(MissionTime now, bool publish)
    {
        var totalW = 0.0;
        foreach (var load in _layout.Loads)
        {
            ref var state = ref _state.Mutable(load);
            var powered = false;
            if (!_state.IsAttached(load))
            {
                state.Energized = false;
                state.PowerW = 0;
            }
            else
            {
                var net = _net[load];
                var live = _hasBus[net] && _voltageV[net] >= _brownoutV[net] && _voltageV[net] > 0;
                powered = live && _candidate[load];
                // An isolated load is deliberately disconnected, so its feed is dead whatever the bus does.
                state.Energized = live && state.On && !_shed[load] && state.Condition != ComponentCondition.Isolated;

                var spec = _layout[load].Load!;
                var fraction = powered ? _servedFraction[net] : 0;
                state.PowerW = spec.PowerW * fraction;
                AddHeat(_layout.HeatNodeOf(load), (spec.HeatW ?? spec.PowerW) * fraction);
                totalW += state.PowerW;
            }

            if (state.Powered == powered) continue;
            state.Powered = powered;
            if (publish) _state.Changed(now, load, ComponentFields.Powered, powered);
        }
        return totalW;
    }

    private void UpdateBuses(MissionTime now, bool publish)
    {
        foreach (var bus in _layout.Buses)
        {
            ref var state = ref _state.Mutable(bus);
            var operable = _state.IsOperable(bus);
            var spec = _layout[bus].Bus!;
            state.VoltageV = operable ? _voltageV[_net[bus]] : 0;
            state.Powered = state.VoltageV >= spec.BrownoutV;
            var brownout = _state.IsAttached(bus) && !state.Powered;
            if (state.Brownout == brownout) continue;
            state.Brownout = brownout;
            if (publish) _state.Events.Publish(new BusBrownoutChanged(now, _layout.IdOf(bus), state.VoltageV, brownout));
        }
    }

    private void AddHeat(int node, double heatW)
    {
        if (node >= 0 && heatW > 0 && _state.IsAttached(node)) _state.Mutable(node).HeatW += heatW;
    }

    private bool HasGas(int tank) =>
        _state.IsAttached(tank) && _state[tank].Condition != ComponentCondition.Isolated && _state[tank].QuantityKg > 0;

    /// <summary>Share of this step's reactant demand on <paramref name="tank"/> that it can actually supply.</summary>
    private double SupplyFraction(int tank)
    {
        var demandKg = _reactantDemandKg[tank];
        return demandKg > 0 ? Math.Min(1, _state[tank].QuantityKg / demandKg) : 1;
    }

    private static double ReactantKg(double powerW, double deltaSeconds, FuelCellSpec spec) =>
        VehicleState.KwhFromJoules(powerW * deltaSeconds) * spec.ReactantKgPerKwh;

    private void Rebuild()
    {
        for (var i = 0; i < _parent.Length; i++) _parent[i] = i;

        foreach (var sw in _layout.Switches)
        {
            if (!_state.IsOperable(sw) || !_state[sw].Closed) continue;
            Join(sw, _layout.FromOf(sw));
            Join(sw, _layout.ToOf(sw));
        }

        foreach (var harness in _layout.Harnesses)
        {
            if (!_state.IsOperable(harness)) continue;
            var from = _layout.FromOf(harness);
            if (!Joinable(from)) continue;
            Join(harness, from);
            Join(from, _layout.ToOf(harness));
        }

        foreach (var outlet in _layout.Outlets)
        {
            if (_state.IsOperable(outlet)) Join(outlet, _layout.BusOf(outlet));
        }

        foreach (var load in _layout.Loads)
        {
            if (!_state.IsAttached(load)) continue;
            Join(load, _layout.BusOf(load));
            Join(load, _state[load].Jumper);
        }

        foreach (var cell in _layout.FuelCells) JoinSource(cell);
        foreach (var battery in _layout.Batteries) JoinSource(battery);

        for (var i = 0; i < _net.Length; i++) _net[i] = Find(i);
        _builtVersion = _state.TopologyVersion;
    }

    private void JoinSource(int source)
    {
        if (_state.IsOperable(source) && _state[source].Online) Join(source, _layout.BusOf(source));
    }

    /// <summary>
    /// Junction components (buses, switches, harnesses, outlets) conduct only when operable; loads when attached and not
    /// isolated (an isolated load is deliberately disconnected, whether fed directly, through a breaker or a jumper).
    /// </summary>
    private bool Joinable(int index)
    {
        if (index < 0) return false;
        return _layout[index].Family is ComponentFamily.Bus or ComponentFamily.Switch or ComponentFamily.Breaker
            or ComponentFamily.Harness or ComponentFamily.Outlet
            ? _state.IsOperable(index)
            : _state.IsAttached(index) && _state[index].Condition != ComponentCondition.Isolated;
    }

    private void Join(int a, int b)
    {
        if (!Joinable(a) || !Joinable(b)) return;
        var rootA = Find(a);
        var rootB = Find(b);
        if (rootA == rootB) return;
        if (rootA < rootB) _parent[rootB] = rootA;
        else _parent[rootA] = rootB;
    }

    private int Find(int index)
    {
        var root = index;
        while (_parent[root] != root) root = _parent[root];
        while (_parent[index] != root)
        {
            var next = _parent[index];
            _parent[index] = root;
            index = next;
        }
        return root;
    }
}
