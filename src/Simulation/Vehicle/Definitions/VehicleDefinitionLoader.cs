using Usa.Simulation.Data;

namespace Usa.Simulation.Vehicle.Definitions;

/// <summary>Loads and validates <c>vehicle/1</c> files.</summary>
public sealed class VehicleDefinitionLoader(DefinitionLoader loader)
{
    public const string Schema = "vehicle/1";

    /// <summary>The file of vehicle <paramref name="vehicleId"/>, relative to the data root.</summary>
    public static string PathFor(string vehicleId) => $"vehicles/{vehicleId}.json";

    /// <summary>The files of the given vehicles (a mission's <c>mission.json</c> <c>vehicles</c>), in the same order.</summary>
    public static IReadOnlyList<string> FilesFor(IEnumerable<string> vehicleIds)
    {
        ArgumentNullException.ThrowIfNull(vehicleIds);
        return vehicleIds.Select(PathFor).ToArray();
    }

    /// <exception cref="DefinitionException">Every validation error in the file.</exception>
    public VehicleDefinition Load(string relativePath)
    {
        var vehicle = loader.Load<VehicleDefinition>(relativePath, Schema);
        var errors = new DefinitionErrors();
        VehicleDefinitionValidator.Validate(vehicle, relativePath, errors);
        errors.ThrowIfAny();
        return vehicle;
    }

    /// <summary>Loads several vehicles, validating each and the docking partners between them.</summary>
    /// <exception cref="DefinitionException">Every validation error across the files.</exception>
    public IReadOnlyList<VehicleDefinition> LoadAll(IReadOnlyList<string> relativePaths)
    {
        ArgumentNullException.ThrowIfNull(relativePaths);
        var errors = new DefinitionErrors();
        var loaded = new List<(VehicleDefinition Vehicle, string File)>(relativePaths.Count);
        foreach (var path in relativePaths)
        {
            var vehicle = loader.Load<VehicleDefinition>(path, Schema);
            VehicleDefinitionValidator.Validate(vehicle, path, errors);
            loaded.Add((vehicle, path));
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (vehicle, file) in loaded)
        {
            if (!ids.Add(vehicle.Id)) errors.Add(file, "$.id", $"Vehicle id '{vehicle.Id}' is loaded twice.");
        }

        VehicleDefinitionValidator.ValidateDockingPartners(loaded, errors);
        errors.ThrowIfAny();
        return loaded.ConvertAll(entry => entry.Vehicle);
    }
}
