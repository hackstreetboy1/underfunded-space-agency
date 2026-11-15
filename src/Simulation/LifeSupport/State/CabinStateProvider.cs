using Usa.Simulation.Kernel;
using Usa.Simulation.LifeSupport.Atmosphere;
using Usa.Simulation.Ports;

namespace Usa.Simulation.LifeSupport.State;

/// <summary>
/// True cabin state (independent of sensors) at <c>cabin.&lt;vehicleId&gt;.&lt;field&gt;</c>, e.g. <c>cabin.tandem.ppco2_mmhg</c>.
/// <list type="table">
/// <item><term>ppo2_mmhg, ppco2_mmhg, inert_mmhg</term><description>Partial pressures, mmHg. Effects: set/add/multiply ppo2 and ppco2.</description></item>
/// <item><term>ppo2_kpa</term><description>ppO2 in kPa (read-only; the health exposure unit).</description></item>
/// <item><term>pressure_kpa</term><description>Total cabin pressure.</description></item>
/// <item><term>temp_c</term><description>Cabin air temperature (the cabin thermal node).</description></item>
/// <item><term>o2_kg</term><description>O2 mass in the cabin air.</description></item>
/// <item><term>lioh_pct</term><description>Canister capacity left, 0-100. Effects: set/add/multiply.</description></item>
/// <item><term>lioh_remaining_kg</term><description>CO2 the canister can still absorb.</description></item>
/// <item><term>scrubber_factor</term><description>0-1 removal factor from saturation (1 above breakthrough).</description></item>
/// <item><term>canisters_replaced</term><description>Fresh canisters installed so far.</description></item>
/// <item><term>leak_pct_per_h</term><description>Cabin gas lost per hour. Effects: set/add/multiply.</description></item>
/// <item><term>o2_supply_short</term><description>True while the regulator is starved by the O2 supply.</description></item>
/// <item><term>lioh_replace</term><description>Effect only: set true to install a fresh canister.</description></item>
/// </list>
/// </summary>
public sealed class CabinStateProvider : IStateProvider, IEffectHandler
{
    public const string PathPrefix = "cabin.";

    private readonly CabinAtmosphere[] _cabins;

    public CabinStateProvider(IEnumerable<CabinAtmosphere> cabins)
    {
        ArgumentNullException.ThrowIfNull(cabins);
        _cabins = cabins.OrderBy(c => c.Id, StringComparer.Ordinal).ToArray();
    }

    public string Prefix => PathPrefix;

    public bool IsKnownPath(string path) => TryGet(path, out _);

    public bool TryGet(string path, out StateValue value)
    {
        value = StateValue.None;
        if (!TryResolve(path, out var cabin, out var field)) return false;
        switch (field)
        {
            case "ppo2_mmhg": value = cabin.PpO2Mmhg; return true;
            case "ppco2_mmhg": value = cabin.PpCo2Mmhg; return true;
            case "inert_mmhg": value = cabin.InertMmhg; return true;
            case "ppo2_kpa": value = cabin.PpO2Kpa; return true;
            case "pressure_kpa": value = cabin.PressureKpa; return true;
            case "temp_c": value = cabin.TempC; return true;
            case "o2_kg": value = cabin.O2Kg; return true;
            case "lioh_pct": value = cabin.LiOhPct; return true;
            case "lioh_remaining_kg": value = cabin.LiOhRemainingKgCo2; return true;
            case "scrubber_factor": value = cabin.ScrubberCapacityFactor; return true;
            case "canisters_replaced": value = cabin.CanistersReplaced; return true;
            case "leak_pct_per_h": value = cabin.LeakPctPerHour; return true;
            case "o2_supply_short": value = cabin.O2SupplyShort; return true;
            default: return false;
        }
    }

    public bool CanApply(string target, EffectOperation operation)
    {
        if (!TryResolve(target, out _, out var field)) return false;
        return field switch
        {
            "ppo2_mmhg" or "ppco2_mmhg" or "lioh_pct" or "leak_pct_per_h" => true,
            "lioh_replace" => operation == EffectOperation.Set,
            _ => false,
        };
    }

    public void Apply(string target, EffectOperation operation, StateValue value, MissionTime now)
    {
        if (!TryResolve(target, out var cabin, out var field) || !CanApply(target, operation))
            throw new UnknownStatePathException(target);

        switch (field)
        {
            case "ppo2_mmhg": cabin.SetPpO2Mmhg(StatePathParts.Combine(target, operation, cabin.PpO2Mmhg, value.AsNumber())); break;
            case "ppco2_mmhg": cabin.SetPpCo2Mmhg(StatePathParts.Combine(target, operation, cabin.PpCo2Mmhg, value.AsNumber())); break;
            case "lioh_pct": cabin.SetLiOhPct(StatePathParts.Combine(target, operation, cabin.LiOhPct, value.AsNumber())); break;
            case "leak_pct_per_h":
                cabin.SetLeakPctPerHour(StatePathParts.Combine(target, operation, cabin.LeakPctPerHour, value.AsNumber()));
                break;
            default:
                if (value.AsBool()) cabin.ReplaceCanister();
                break;
        }
    }

    private bool TryResolve(string path, out CabinAtmosphere cabin, out ReadOnlySpan<char> field)
    {
        cabin = null!;
        if (!StatePathParts.TrySplit(path, PathPrefix, out var owner, out field) || owner.IsEmpty) return false;
        foreach (var candidate in _cabins)
        {
            if (!owner.SequenceEqual(candidate.Id)) continue;
            cabin = candidate;
            return true;
        }
        return false;
    }
}
