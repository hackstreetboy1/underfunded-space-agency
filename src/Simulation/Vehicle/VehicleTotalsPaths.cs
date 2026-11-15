using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Vehicle;

/// <summary>
/// Serves <c>vehicle.&lt;vehicleId&gt;.&lt;field&gt;</c>: <c>mass_kg</c>, <c>power_gen_w</c>, <c>power_load_w</c>,
/// <c>inertia_roll_kgm2</c>, <c>inertia_pitch_kgm2</c>, <c>inertia_yaw_kgm2</c>, <c>section.&lt;sectionId&gt;.attached</c>.
/// Accepts the effect <c>vehicle.&lt;vehicleId&gt;.jettison</c> = '&lt;jettison event id&gt;' (Set), which stages that section.
/// </summary>
public sealed class VehicleTotalsPaths : IStateProvider, IEffectHandler
{
    public const string MassKg = "mass_kg";
    public const string PowerGenW = "power_gen_w";
    public const string PowerLoadW = "power_load_w";
    public const string InertiaRollKgM2 = "inertia_roll_kgm2";
    public const string InertiaPitchKgM2 = "inertia_pitch_kgm2";
    public const string InertiaYawKgM2 = "inertia_yaw_kgm2";
    public const string Jettison = "jettison";

    private const string SectionPrefix = "section.";
    private const string AttachedSuffix = ".attached";

    private readonly VehicleState[] _vehicles;

    public VehicleTotalsPaths(IEnumerable<VehicleState> vehicles)
    {
        ArgumentNullException.ThrowIfNull(vehicles);
        _vehicles = vehicles.OrderBy(v => v.VehicleId, StringComparer.Ordinal).ToArray();
    }

    public string Prefix => StatePaths.Vehicle;

    public bool TryGet(string path, out StateValue value)
    {
        ArgumentNullException.ThrowIfNull(path);
        value = StateValue.None;
        if (!TrySplit(path, out var vehicle, out var field)) return false;

        switch (field)
        {
            case MassKg: value = vehicle.ComputeMassProperties().MassKg; return true;
            case PowerGenW: value = vehicle.PowerGenerationW; return true;
            case PowerLoadW: value = vehicle.PowerLoadW; return true;
            case InertiaRollKgM2: value = vehicle.ComputeMassProperties().InertiaKgM2.X; return true;
            case InertiaPitchKgM2: value = vehicle.ComputeMassProperties().InertiaKgM2.Y; return true;
            case InertiaYawKgM2: value = vehicle.ComputeMassProperties().InertiaKgM2.Z; return true;
        }

        if (field.Length > SectionPrefix.Length + AttachedSuffix.Length
            && field.StartsWith(SectionPrefix, StringComparison.Ordinal) && field.EndsWith(AttachedSuffix, StringComparison.Ordinal))
        {
            var sectionId = field.Slice(SectionPrefix.Length, field.Length - SectionPrefix.Length - AttachedSuffix.Length);
            for (var s = 0; s < vehicle.Layout.Sections.Count; s++)
            {
                if (!sectionId.SequenceEqual(vehicle.Layout.Sections[s].Id)) continue;
                value = vehicle.IsSectionAttached(s);
                return true;
            }
        }
        return false;
    }

    public bool IsKnownPath(string path) => TryGet(path, out _);

    public bool CanApply(string target, EffectOperation operation) =>
        operation == EffectOperation.Set && TrySplit(target, out _, out var field) && field.SequenceEqual(Jettison);

    /// <exception cref="InvalidOperationException">The vehicle has no attached section with that jettison id.</exception>
    public void Apply(string target, EffectOperation operation, StateValue value, MissionTime now)
    {
        if (!CanApply(target, operation)) throw new UnknownStatePathException(target);
        TrySplit(target, out var vehicle, out _);
        var eventId = value.AsText();
        if (!vehicle.Jettison(eventId, now))
            throw new InvalidOperationException($"'{vehicle.VehicleId}' has no attached section that jettisons on '{eventId}'.");
    }

    private bool TrySplit(string path, out VehicleState vehicle, out ReadOnlySpan<char> field)
    {
        vehicle = null!;
        field = default;
        if (!path.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var dot = path.IndexOf('.', Prefix.Length);
        if (dot < 0 || dot == path.Length - 1) return false;
        var id = path.AsSpan(Prefix.Length, dot - Prefix.Length);
        foreach (var candidate in _vehicles)
        {
            if (!id.SequenceEqual(candidate.VehicleId)) continue;
            vehicle = candidate;
            field = path.AsSpan(dot + 1);
            return true;
        }
        return false;
    }
}
