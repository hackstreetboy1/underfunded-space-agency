using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;
using Usa.Simulation.Vehicle.Definitions;
using Usa.Simulation.Vehicle.Propulsion;
using Usa.Simulation.Vehicle.Sensors;

namespace Usa.Simulation.Vehicle;

/// <summary>
/// Serves and mutates <c>&lt;componentId&gt;.&lt;field&gt;</c> paths of one vehicle (prefix <c>tandem/</c>, <c>lodestar/</c>).
/// <para><b>Read fields.</b> Every component: <c>condition</c> ('nominal'|'degraded'|'failed'|'isolated'), <c>fault</c>
/// (text, '' when none), <c>attached</c>, <c>family</c>.
/// Thruster/engine: <c>valve_open</c>, <c>stuck_fraction</c>, <c>temperature_c</c>, <c>available</c>, <c>firing</c>, <c>activity</c>.
/// Thruster group: <c>armed</c>, <c>available_count</c>, <c>drivers_powered</c> (every member's driver powered, i.e. the
/// ring would respond to a test), <c>activity</c>, <c>activity_level</c> ('idle'|'low'|'high').
/// Tank: <c>quantity_kg</c> (alias <c>prop_kg</c>), <c>capacity_kg</c>, <c>fraction</c>, <c>depleted</c>.
/// Breaker, switch, hatch, valve: <c>closed</c>.
/// Bus: <c>voltage_v</c>, <c>powered</c>, <c>brownout</c>.
/// Fuel cell: <c>online</c>, <c>output_w</c>, <c>output_kw</c>, <c>efficiency</c>, <c>max_power_w</c>.
/// Battery: <c>online</c>, <c>energy_kwh</c>, <c>capacity_kwh</c>, <c>fraction</c>, <c>output_w</c> (discharge minus charge).
/// Any component with a load: <c>on</c>, <c>powered</c>, <c>energized</c>, <c>power_w</c>, <c>jumper</c> (source id or '').
/// Driver: <c>shorted</c>. Harness: <c>continuity</c>. Outlet: <c>live</c>.
/// Sensor: <c>online</c>, <c>bias</c>, and each channel's field (truth of a measured path of this vehicle; none when offline).
/// Docking port: <c>latched</c>, <c>load_fraction</c>, <c>load_pct</c>, <c>overstress_s</c>, <c>overstress_alarm</c>, <c>damaged</c>.
/// Parachute: <c>state</c> ('stowed'|'deployed'|'cut'), <c>deployed</c>. Retro rocket: <c>fired</c>, <c>burning</c>, <c>burn_remaining_s</c>.
/// Thermal node: <c>heat_w</c> (electrical heat released into it).</para>
/// <para><b>Effects.</b> Set: <c>condition</c>, <c>fault</c>, <c>closed</c>, <c>armed</c>, <c>on</c>, <c>online</c>,
/// <c>latched</c>, <c>state</c>/<c>deployed</c> (parachute), <c>fired</c> (retro, true only), <c>jumper</c> (bus or outlet id, '' removes).
/// Set/Add/Multiply: <c>stuck_fraction</c> (fraction a shorted driver holds the valve open; <c>valve_open</c> is the
/// actual opening and read-only), <c>temperature_c</c> (thruster), <c>quantity_kg</c>/<c>prop_kg</c>, <c>energy_kwh</c>,
/// <c>efficiency</c>, <c>bias</c>. Numeric effects reject NaN and ±∞ (operand and result).</para>
/// <para>Fields of these components owned by other areas (cabin pressure, node temperatures...) are delegated to the
/// optional fallback provider and effect handler.</para>
/// </summary>
public sealed class VehicleComponentPaths : IStateProvider, IEffectHandler
{
    private readonly VehicleState _state;
    private readonly VehicleLayout _layout;
    private readonly PropulsionModel _propulsion;
    private readonly SensorModel _sensors;
    private readonly IStateProvider? _fallback;
    private readonly IEffectHandler? _fallbackEffects;

    public VehicleComponentPaths(VehicleState state, IStateProvider? fallback = null, IEffectHandler? fallbackEffects = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        _layout = state.Layout;
        _propulsion = new PropulsionModel(state);
        _sensors = new SensorModel(state);
        _fallback = fallback;
        _fallbackEffects = fallbackEffects;
    }

    private enum Writable
    {
        None,
        Condition,
        Fault,
        Closed,
        Armed,
        On,
        Online,
        StuckFraction,
        TemperatureC,
        QuantityKg,
        EnergyKwh,
        Efficiency,
        Bias,
        Latched,
        ParachuteState,
        Deployed,
        Fired,
        Jumper,
    }

    public string Prefix => _layout.PathPrefix;

    public bool TryGet(string path, out StateValue value)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (TrySplit(path, out var index, out var field) && TryRead(index, field, out value)) return true;
        value = StateValue.None;
        return _fallback is not null && _fallback.TryGet(path, out value);
    }

    public bool IsKnownPath(string path) => TryGet(path, out _) || (_fallback?.IsKnownPath(path) ?? false);

    public bool CanApply(string target, EffectOperation operation)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (TrySplit(target, out var index, out var field))
        {
            var writable = WritableField(index, field);
            if (writable != Writable.None) return operation == EffectOperation.Set || IsNumeric(writable);
        }
        return _fallbackEffects?.CanApply(target, operation) ?? false;
    }

    public void Apply(string target, EffectOperation operation, StateValue value, MissionTime now)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!TrySplit(target, out var index, out var field) || WritableField(index, field) is var writable && writable == Writable.None)
        {
            if (_fallbackEffects is null) throw new UnknownStatePathException(target);
            _fallbackEffects.Apply(target, operation, value, now);
            return;
        }

        if (operation != EffectOperation.Set && !IsNumeric(writable))
            throw new InvalidOperationException($"'{target}' only accepts Set, not {operation}.");

        ref readonly var s = ref _state[index];
        switch (writable)
        {
            case Writable.Condition:
                _state.SetCondition(index, ParseCondition(target, value), now);
                break;
            case Writable.Fault:
                _state.SetFault(index, value.AsText(), now);
                break;
            case Writable.Closed:
                _state.SetClosed(index, value.AsBool(), now);
                break;
            case Writable.Armed:
                _state.SetArmed(index, value.AsBool(), now);
                break;
            case Writable.On:
                _state.SetOn(index, value.AsBool(), now);
                break;
            case Writable.Online:
                _state.SetOnline(index, value.AsBool(), now);
                break;
            case Writable.Latched:
                _state.SetLatched(index, value.AsBool(), now);
                break;
            case Writable.StuckFraction:
                _state.SetStuckFraction(index, Combine(target, operation, s.StuckFraction, value));
                break;
            case Writable.TemperatureC:
                _state.SetTemperatureC(index, Combine(target, operation, s.TemperatureC, value));
                break;
            case Writable.QuantityKg:
                _state.SetQuantityKg(index, Combine(target, operation, s.QuantityKg, value), now);
                break;
            case Writable.EnergyKwh:
                _state.SetEnergyKwh(index, Combine(target, operation, s.EnergyKwh, value));
                break;
            case Writable.Efficiency:
                _state.SetEfficiency(index, Combine(target, operation, s.Efficiency, value));
                break;
            case Writable.Bias:
                _state.SetBias(index, Combine(target, operation, s.Bias, value));
                break;
            case Writable.ParachuteState:
                if (!ComponentFields.TryParseParachute(value.AsText(), out var status))
                    throw new InvalidOperationException($"'{value}' is not a parachute state for '{target}'.");
                _state.SetParachute(index, status, now);
                break;
            case Writable.Deployed:
                if (value.AsBool()) _state.SetParachute(index, ParachuteStatus.Deployed, now);
                break;
            case Writable.Fired:
                if (!value.AsBool()) throw new InvalidOperationException($"A retro rocket cannot be un-fired ('{target}').");
                _state.IgniteRetro(index, now);
                break;
            case Writable.Jumper:
                _state.SetJumper(index, ParseJumperSource(target, value.AsText()), now);
                break;
        }
    }

    private bool TrySplit(string path, out int index, out ReadOnlySpan<char> field)
    {
        index = -1;
        field = default;
        if (!path.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var dot = path.LastIndexOf('.');
        if (dot <= 0 || dot == path.Length - 1) return false;
        field = path.AsSpan(dot + 1);
        return _layout.TryGetIndex(path.AsSpan(0, dot), out index);
    }

    private bool TryRead(int index, ReadOnlySpan<char> field, out StateValue value)
    {
        var c = _layout[index];
        ref readonly var s = ref _state[index];
        value = StateValue.None;

        switch (field)
        {
            case ComponentFields.Condition: value = ComponentFields.ConditionName(s.Condition); return true;
            case ComponentFields.Fault: value = s.Fault; return true;
            case ComponentFields.Attached: value = _state.IsAttached(index); return true;
            case ComponentFields.Family: value = ComponentFields.FamilyName(c.Family); return true;
        }

        if (_layout.ThrusterSpecOf(index) is not null && TryReadThruster(index, field, in s, out value)) return true;
        if (c.ThrusterGroup is { } group && TryReadGroup(index, group, field, in s, out value)) return true;
        if (c.Tank is { } tank && TryReadTank(tank, field, in s, out value)) return true;
        if (c.Bus is not null && TryReadBus(field, in s, out value)) return true;
        if (c.FuelCell is { } cell && TryReadFuelCell(cell, field, in s, out value)) return true;
        if (c.Battery is { } battery && TryReadBattery(battery, field, in s, out value)) return true;
        if (c.Load is not null && TryReadLoad(field, in s, out value)) return true;
        if (c.Sensor is { } sensor && TryReadSensor(index, sensor, field, in s, out value)) return true;
        if (c.Docking is not null && TryReadDocking(field, in s, out value)) return true;
        if (c.RetroRocket is not null && TryReadRetro(field, in s, out value)) return true;

        if (field.SequenceEqual(ComponentFields.Closed) && HasContacts(c.Family))
        {
            value = s.Closed;
            return true;
        }
        if (c.Family == ComponentFamily.Driver && field.SequenceEqual(ComponentFields.Shorted))
        {
            value = string.Equals(s.Fault, ComponentFields.ShortOnFault, StringComparison.Ordinal);
            return true;
        }
        if (c.Harness is not null && field.SequenceEqual(ComponentFields.Continuity))
        {
            value = _state.IsOperable(index);
            return true;
        }
        if (c.Outlet is not null && field.SequenceEqual(ComponentFields.Live))
        {
            value = _state.IsOperable(index) && _state[_layout.BusOf(index)].Powered;
            return true;
        }
        if (c.Parachute is not null)
        {
            if (field.SequenceEqual(ComponentFields.State)) value = ComponentFields.ParachuteName(s.Parachute);
            else if (field.SequenceEqual(ComponentFields.Deployed)) value = s.Parachute == ParachuteStatus.Deployed;
            if (value.Kind != StateValueKind.None) return true;
        }
        if (c.ThermalNode is not null && field.SequenceEqual(ComponentFields.HeatW))
        {
            value = s.HeatW;
            return true;
        }
        return false;
    }

    private bool TryReadThruster(int index, ReadOnlySpan<char> field, in ComponentState s, out StateValue value)
    {
        switch (field)
        {
            case ComponentFields.ValveOpen: value = s.ValveOpen; return true;
            case ComponentFields.StuckFraction: value = s.StuckFraction; return true;
            case ComponentFields.TemperatureC: value = s.TemperatureC; return true;
            case ComponentFields.Available: value = _propulsion.IsAvailable(index); return true;
            case ComponentFields.Firing: value = s.ValveOpen > 0; return true;
            case ComponentFields.Activity: value = s.Activity; return true;
            default: value = StateValue.None; return false;
        }
    }

    private bool TryReadGroup(int index, ThrusterGroupSpec spec, ReadOnlySpan<char> field, in ComponentState s, out StateValue value)
    {
        switch (field)
        {
            case ComponentFields.Armed: value = s.Armed; return true;
            case ComponentFields.Activity: value = s.Activity; return true;
            case ComponentFields.ActivityLevel:
                value = s.Activity > spec.HighActivityFraction ? "high" : s.Activity > spec.LowActivityFraction ? "low" : "idle";
                return true;
            case ComponentFields.AvailableCount:
                var available = 0;
                foreach (var member in _layout.MembersOf(index))
                {
                    if (_propulsion.IsAvailable(member)) available++;
                }
                value = available;
                return true;
            case ComponentFields.DriversPowered:
                var members = _layout.MembersOf(index);
                var powered = members.Length > 0;
                foreach (var member in members)
                {
                    var driver = _layout.DriverOf(member);
                    if (driver >= 0 && !_state[driver].Powered) powered = false;
                }
                value = powered;
                return true;
            default: value = StateValue.None; return false;
        }
    }

    private static bool TryReadTank(TankSpec spec, ReadOnlySpan<char> field, in ComponentState s, out StateValue value)
    {
        switch (field)
        {
            case ComponentFields.QuantityKg or ComponentFields.PropKg: value = s.QuantityKg; return true;
            case ComponentFields.CapacityKg: value = spec.CapacityKg; return true;
            case ComponentFields.Fraction: value = s.QuantityKg / spec.CapacityKg; return true;
            case ComponentFields.Depleted: value = s.Depleted; return true;
            default: value = StateValue.None; return false;
        }
    }

    private static bool TryReadBus(ReadOnlySpan<char> field, in ComponentState s, out StateValue value)
    {
        switch (field)
        {
            case ComponentFields.VoltageV: value = s.VoltageV; return true;
            case ComponentFields.Powered: value = s.Powered; return true;
            case ComponentFields.Brownout: value = s.Brownout; return true;
            default: value = StateValue.None; return false;
        }
    }

    private static bool TryReadFuelCell(FuelCellSpec spec, ReadOnlySpan<char> field, in ComponentState s, out StateValue value)
    {
        switch (field)
        {
            case ComponentFields.Online: value = s.Online; return true;
            case ComponentFields.OutputW: value = s.PowerW; return true;
            case ComponentFields.OutputKw: value = s.PowerW / 1000.0; return true;
            case ComponentFields.Efficiency: value = s.Efficiency; return true;
            case ComponentFields.MaxPowerW: value = spec.MaxPowerW; return true;
            default: value = StateValue.None; return false;
        }
    }

    private static bool TryReadBattery(BatterySpec spec, ReadOnlySpan<char> field, in ComponentState s, out StateValue value)
    {
        switch (field)
        {
            case ComponentFields.Online: value = s.Online; return true;
            case ComponentFields.EnergyKwh: value = s.EnergyKwh; return true;
            case ComponentFields.CapacityKwh: value = spec.CapacityKwh; return true;
            case ComponentFields.Fraction: value = s.EnergyKwh / spec.CapacityKwh; return true;
            case ComponentFields.OutputW: value = s.PowerW; return true;
            default: value = StateValue.None; return false;
        }
    }

    private bool TryReadLoad(ReadOnlySpan<char> field, in ComponentState s, out StateValue value)
    {
        switch (field)
        {
            case ComponentFields.On: value = s.On; return true;
            case ComponentFields.Powered: value = s.Powered; return true;
            case ComponentFields.Energized: value = s.Energized; return true;
            case ComponentFields.PowerW: value = s.PowerW; return true;
            case ComponentFields.Jumper: value = s.Jumper < 0 ? "" : _layout.IdOf(s.Jumper).Value; return true;
            default: value = StateValue.None; return false;
        }
    }

    private bool TryReadSensor(int index, SensorSpec spec, ReadOnlySpan<char> field, in ComponentState s, out StateValue value)
    {
        switch (field)
        {
            case ComponentFields.Online: value = _sensors.IsOnline(index); return true;
            case ComponentFields.Bias: value = s.Bias; return true;
        }

        var channels = spec.Channels;
        for (var i = 0; i < channels.Count; i++)
        {
            var channel = channels[i];
            if (!field.SequenceEqual(channel.Field)) continue;
            if (!_sensors.IsOnline(index))
            {
                value = StateValue.None;
                return true;
            }
            if (channel.Measures.StartsWith(Prefix, StringComparison.Ordinal) && TryGet(channel.Measures, out value)) return true;
        }
        value = StateValue.None;
        return false;
    }

    private static bool TryReadDocking(ReadOnlySpan<char> field, in ComponentState s, out StateValue value)
    {
        switch (field)
        {
            case ComponentFields.Latched: value = s.Latched; return true;
            case ComponentFields.LoadFraction: value = s.LoadFraction; return true;
            case ComponentFields.LoadPct: value = s.LoadFraction * 100.0; return true;
            case ComponentFields.OverstressS: value = s.OverstressS; return true;
            case ComponentFields.OverstressAlarm: value = s.OverstressAlarm; return true;
            // Isolation is a deliberate disconnection, not damage.
            case ComponentFields.Damaged: value = s.Condition is ComponentCondition.Degraded or ComponentCondition.Failed; return true;
            default: value = StateValue.None; return false;
        }
    }

    private static bool TryReadRetro(ReadOnlySpan<char> field, in ComponentState s, out StateValue value)
    {
        switch (field)
        {
            case ComponentFields.Fired: value = s.Fired; return true;
            case ComponentFields.Burning: value = s.Fired && s.BurnRemainingS > 0; return true;
            case ComponentFields.BurnRemainingS: value = s.BurnRemainingS; return true;
            default: value = StateValue.None; return false;
        }
    }

    private Writable WritableField(int index, ReadOnlySpan<char> field)
    {
        var c = _layout[index];
        switch (field)
        {
            case ComponentFields.Condition: return Writable.Condition;
            case ComponentFields.Fault: return Writable.Fault;
            case ComponentFields.Closed when HasContacts(c.Family): return Writable.Closed;
            case ComponentFields.Armed when c.ThrusterGroup is not null: return Writable.Armed;
            case ComponentFields.On when c.Load is not null: return Writable.On;
            case ComponentFields.Online when c.FuelCell is not null || c.Battery is not null: return Writable.Online;
            case ComponentFields.StuckFraction when _layout.ThrusterSpecOf(index) is not null: return Writable.StuckFraction;
            case ComponentFields.TemperatureC when _layout.ThrusterSpecOf(index) is not null: return Writable.TemperatureC;
            case ComponentFields.QuantityKg or ComponentFields.PropKg when c.Tank is not null: return Writable.QuantityKg;
            case ComponentFields.EnergyKwh when c.Battery is not null: return Writable.EnergyKwh;
            case ComponentFields.Efficiency when c.FuelCell is not null: return Writable.Efficiency;
            case ComponentFields.Bias when c.Sensor is not null: return Writable.Bias;
            case ComponentFields.Latched when c.Docking is not null: return Writable.Latched;
            case ComponentFields.State when c.Parachute is not null: return Writable.ParachuteState;
            case ComponentFields.Deployed when c.Parachute is not null: return Writable.Deployed;
            case ComponentFields.Fired when c.RetroRocket is not null: return Writable.Fired;
            case ComponentFields.Jumper when c.Load is not null: return Writable.Jumper;
            default: return Writable.None;
        }
    }

    private static bool IsNumeric(Writable field) =>
        field is Writable.StuckFraction or Writable.TemperatureC or Writable.QuantityKg or Writable.EnergyKwh
            or Writable.Efficiency or Writable.Bias;

    private static bool HasContacts(ComponentFamily family) =>
        family is ComponentFamily.Breaker or ComponentFamily.Switch or ComponentFamily.Hatch or ComponentFamily.Valve;

    /// <exception cref="ArgumentOutOfRangeException">The operand or the result is NaN or infinite.</exception>
    private static double Combine(string target, EffectOperation operation, double current, StateValue value)
    {
        var operand = value.AsNumber();
        if (!double.IsFinite(operand))
            throw new ArgumentOutOfRangeException(nameof(value), operand, $"Effect on {target} needs a finite number, got {operand}.");
        var result = operation switch
        {
            EffectOperation.Set => operand,
            EffectOperation.Add => current + operand,
            EffectOperation.Multiply => current * operand,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };
        return double.IsFinite(result)
            ? result
            : throw new ArgumentOutOfRangeException(nameof(value), result, $"Effect {operation} on {target} gives non-finite {result}.");
    }

    private static ComponentCondition ParseCondition(string target, StateValue value) =>
        ComponentFields.TryParseCondition(value.AsText(), out var condition)
            ? condition
            : throw new InvalidOperationException($"'{value}' is not a condition for '{target}'.");

    private int ParseJumperSource(string target, string source)
    {
        if (source.Length == 0) return -1;
        if (_layout.TryGetIndex(source, out var index) && _layout[index].Family is ComponentFamily.Bus or ComponentFamily.Outlet) return index;
        throw new InvalidOperationException($"Jumper source '{source}' for '{target}' must be a bus or outlet of '{_layout.VehicleId}'.");
    }
}
