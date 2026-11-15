using Usa.Simulation.Kernel;
using Usa.Simulation.LifeSupport.Thermal;
using Usa.Simulation.Ports;

namespace Usa.Simulation.LifeSupport.State;

/// <summary>
/// True thermal state at <c>thermal.&lt;componentId&gt;.&lt;field&gt;</c>, e.g. <c>thermal.tandem/thermal/cabin.temp_c</c>.
/// <para>Thermal nodes:</para>
/// <list type="table">
/// <item><term>temp_c</term><description>Temperature. Effects: set/add (scenario heat soak).</description></item>
/// <item><term>trend_c_per_min</term><description>Average rate over the last step.</description></item>
/// <item><term>time_to_warning_s</term><description>Seconds until the safe range is left at this trend; 0 outside it; +∞ when stable or recovering.</description></item>
/// <item><term>safe_min_c, safe_max_c</term><description>Safe range.</description></item>
/// <item><term>in_range</term><description>True inside the safe range.</description></item>
/// <item><term>band</term><description>'nominal' | 'below_safe' | 'above_safe'.</description></item>
/// <item><term>net_heat_w</term><description>Average net heat into the node over the last step.</description></item>
/// <item><term>extra_heat_w</term><description>Persistent heat injected by effects. Effects: set/add/multiply.</description></item>
/// <item><term>solar_factor</term><description>Persistent multiplier (≥ 0, default 1) on solar heat (sunshades, difficulty). Effects: set/add/multiply.</description></item>
/// <item><term>solar_w</term><description>Solar heat absorbed over the last step.</description></item>
/// <item><term>attached</term><description>False after its section was jettisoned. Effect: set false.</description></item>
/// </list>
/// <para>Radiators:</para>
/// <list type="table">
/// <item><term>rejection_w</term><description>Average heat rejected over the last step.</description></item>
/// <item><term>condition_factor</term><description>0-1 persistent degradation. Effects: set/add/multiply.</description></item>
/// </list>
/// </summary>
public sealed class ThermalStateProvider(ThermalNetwork network) : IStateProvider, IEffectHandler
{
    public const string PathPrefix = "thermal.";

    private readonly ThermalNetwork _network = network ?? throw new ArgumentNullException(nameof(network));

    public string Prefix => PathPrefix;

    public bool IsKnownPath(string path) => TryGet(path, out _);

    public bool TryGet(string path, out StateValue value)
    {
        value = StateValue.None;
        if (!StatePathParts.TrySplit(path, PathPrefix, out var owner, out var field) || owner.IsEmpty) return false;

        var node = FindNode(owner);
        if (node >= 0)
        {
            switch (field)
            {
                case "temp_c": value = _network.TemperatureC(node); return true;
                case "trend_c_per_min": value = _network.TrendCPerMin(node); return true;
                case "time_to_warning_s": value = _network.TimeToWarningS(node); return true;
                case "safe_min_c": value = _network.SafeMinC(node); return true;
                case "safe_max_c": value = _network.SafeMaxC(node); return true;
                case "in_range": value = _network.Band(node) == ThermalBand.Nominal; return true;
                case "band": value = BandText(_network.Band(node)); return true;
                case "net_heat_w": value = _network.NetHeatW(node); return true;
                case "extra_heat_w": value = _network.ExtraHeatW(node); return true;
                case "solar_factor": value = _network.SolarFactor(node); return true;
                case "solar_w": value = _network.SolarHeatW(node); return true;
                case "attached": value = _network.IsAttached(node); return true;
                default: return false;
            }
        }

        var radiator = FindRadiator(owner);
        if (radiator < 0) return false;
        switch (field)
        {
            case "rejection_w": value = _network.RadiatorRejectionW(radiator); return true;
            case "condition_factor": value = _network.RadiatorConditionFactor(radiator); return true;
            default: return false;
        }
    }

    public bool CanApply(string target, EffectOperation operation)
    {
        if (!StatePathParts.TrySplit(target, PathPrefix, out var owner, out var field) || owner.IsEmpty) return false;
        if (FindNode(owner) >= 0)
        {
            return field switch
            {
                "temp_c" => operation is EffectOperation.Set or EffectOperation.Add,
                "extra_heat_w" => true,
                "solar_factor" => true,
                "attached" => operation == EffectOperation.Set,
                _ => false,
            };
        }
        return FindRadiator(owner) >= 0 && field.SequenceEqual("condition_factor");
    }

    public void Apply(string target, EffectOperation operation, StateValue value, MissionTime now)
    {
        if (!CanApply(target, operation)) throw new UnknownStatePathException(target);
        StatePathParts.TrySplit(target, PathPrefix, out var owner, out var field);

        var node = FindNode(owner);
        if (node < 0)
        {
            var radiator = FindRadiator(owner);
            _network.SetRadiatorConditionFactor(
                radiator, StatePathParts.Combine(target, operation, _network.RadiatorConditionFactor(radiator), value.AsNumber()));
            return;
        }

        switch (field)
        {
            case "temp_c":
                _network.SetTemperatureC(node, StatePathParts.Combine(target, operation, _network.TemperatureC(node), value.AsNumber()));
                break;
            case "extra_heat_w":
                _network.SetExtraHeatW(node, StatePathParts.Combine(target, operation, _network.ExtraHeatW(node), value.AsNumber()));
                break;
            case "solar_factor":
                _network.SetSolarFactor(node, StatePathParts.Combine(target, operation, _network.SolarFactor(node), value.AsNumber()));
                break;
            default:
                if (value.AsBool())
                {
                    if (!_network.IsAttached(node))
                        throw new InvalidOperationException($"Thermal node '{_network.NodeId(node)}' cannot be re-attached.");
                }
                else
                {
                    _network.Detach(node);
                }
                break;
        }
    }

    private static string BandText(ThermalBand band) => band switch
    {
        ThermalBand.BelowSafe => "below_safe",
        ThermalBand.AboveSafe => "above_safe",
        _ => "nominal",
    };

    private int FindNode(ReadOnlySpan<char> id)
    {
        for (var i = 0; i < _network.NodeCount; i++)
        {
            if (id.SequenceEqual(_network.NodeId(i).Value)) return i;
        }
        return -1;
    }

    private int FindRadiator(ReadOnlySpan<char> id)
    {
        for (var r = 0; r < _network.RadiatorCount; r++)
        {
            if (id.SequenceEqual(_network.RadiatorId(r).Value)) return r;
        }
        return -1;
    }
}
