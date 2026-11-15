using Usa.Simulation.Kernel;
using Usa.Simulation.LifeSupport;
using Usa.Simulation.Ports;
using Usa.Simulation.Vehicle;
using Usa.Simulation.Vehicle.Definitions;

namespace Usa.Simulation.Session;

/// <summary>
/// Fallback of <see cref="VehicleComponentPaths"/> for component fields other areas own (STATE_PATHS "Delegated component
/// fields"): <c>&lt;cabin compartment&gt;.&lt;f&gt;</c> is <c>cabin.&lt;vehicle&gt;.&lt;f&gt;</c> plus the derived <c>ppo2_kpa</c>, and
/// <c>&lt;thermal node or radiator&gt;.&lt;f&gt;</c> is <c>thermal.&lt;id&gt;.&lt;f&gt;</c>, for reads and effects. Sensor channels and
/// the health exposure paths rely on it. Resolutions are cached per path, so repeated reads do not allocate.
/// </summary>
internal sealed class ComponentFieldFallback : IStateProvider, IEffectHandler
{
    private const string PpO2KpaField = "ppo2_kpa";
    private const string PpO2MmhgField = "ppo2_mmhg";
    private const string CabinPrefix = "cabin.";
    private const string ThermalPrefix = "thermal.";

    private readonly VehicleState _vehicle;
    private readonly ComponentId? _cabinCompartment;
    private readonly Dictionary<string, Resolution?> _cache = new(StringComparer.Ordinal);

    public ComponentFieldFallback(VehicleState vehicle)
    {
        _vehicle = vehicle;
        _cabinCompartment = vehicle.Layout.Definition.Cabin?.Compartment;
    }

    public string Prefix => _vehicle.Layout.PathPrefix;

    public IStateProvider? Cabin { get; set; }

    public IEffectHandler? CabinEffects { get; set; }

    public IStateProvider? Thermal { get; set; }

    public IEffectHandler? ThermalEffects { get; set; }

    public bool TryGet(string path, out StateValue value)
    {
        value = StateValue.None;
        if (Resolve(path) is not { } r) return false;
        var provider = r.Cabin ? Cabin : Thermal;
        if (provider is null || !provider.TryGet(r.Target, out value)) return false;
        if (r.ToKpa) value = value.AsNumber() * GasConstants.PaPerMmhg / 1000.0;
        return true;
    }

    public bool IsKnownPath(string path) => TryGet(path, out _);

    public bool CanApply(string target, EffectOperation operation) =>
        Resolve(target) is { ToKpa: false } r && (r.Cabin ? CabinEffects : ThermalEffects) is { } effects && effects.CanApply(r.Target, operation);

    public void Apply(string target, EffectOperation operation, StateValue value, MissionTime now)
    {
        if (!CanApply(target, operation)) throw new UnknownStatePathException(target);
        var r = Resolve(target)!;
        (r.Cabin ? CabinEffects : ThermalEffects)!.Apply(r.Target, operation, value, now);
    }

    private Resolution? Resolve(string path)
    {
        if (_cache.TryGetValue(path, out var cached)) return cached;
        var resolution = Create(path);
        _cache[path] = resolution;
        return resolution;
    }

    private Resolution? Create(string path)
    {
        var dot = path.LastIndexOf('.');
        if (dot <= 0 || dot == path.Length - 1) return null;
        if (!_vehicle.Layout.TryGetIndex(path.AsSpan(0, dot), out var index)) return null;
        var field = path[(dot + 1)..];
        var component = _vehicle.Layout[index];

        if (component.Id == _cabinCompartment)
        {
            var owner = $"{CabinPrefix}{_vehicle.VehicleId}.";
            return field == PpO2KpaField ? new Resolution(owner + PpO2MmhgField, true, true) : new Resolution(owner + field, true, false);
        }
        return component.Family is ComponentFamily.ThermalNode or ComponentFamily.Radiator
            ? new Resolution($"{ThermalPrefix}{component.Id.Value}.{field}", false, false)
            : null;
    }

    private sealed record Resolution(string Target, bool Cabin, bool ToKpa);
}
