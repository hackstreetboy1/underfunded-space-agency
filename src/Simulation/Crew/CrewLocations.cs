using System.Globalization;
using Usa.Simulation.Data;

namespace Usa.Simulation.Crew;

/// <summary>
/// The validated places a mission's crew can be, sorted by id. Tasks name one of them as their location (or
/// <see cref="CurrentId"/>), and health reads each member's exposure from the cabin of the place they are in.
/// </summary>
public sealed class CrewLocations
{
    /// <summary>Mission 1's cabin location.</summary>
    public const string CabinId = "cabin";

    /// <summary>The suited location; EVA tasks always take place here.</summary>
    public const string EvaId = "eva";

    /// <summary>Task location meaning "wherever the assignee already is"; never a location of its own.</summary>
    public const string CurrentId = "current";

    private readonly CrewLocationDefinition[] _locations;

    private CrewLocations(CrewLocationDefinition[] locations)
    {
        _locations = locations;
        DefaultIndex = Array.FindIndex(locations, l => l.Default);
    }

    public IReadOnlyList<CrewLocationDefinition> Locations => _locations;

    public int Count => _locations.Length;

    public int DefaultIndex { get; }

    public CrewLocationDefinition Default => _locations[DefaultIndex];

    public CrewLocationDefinition this[int index] => _locations[index];

    /// <summary>
    /// Mission 1's places: <c>cabin</c> (the default, inside <paramref name="vehicle"/>) and <c>eva</c> (suited, tethered to it).
    /// Without a vehicle the cabin's vehicle is '' (enough for the roster, not for exposure paths that use <c>{cabin}</c>).
    /// </summary>
    public static CrewLocations Basic(string? vehicle = null) => new(
    [
        new CrewLocationDefinition(CabinId, Cabin: vehicle ?? "", Default: true),
        new CrewLocationDefinition(EvaId, Vehicle: vehicle),
    ]);

    /// <exception cref="DefinitionException">Every problem, reported together.</exception>
    public static CrewLocations Create(IReadOnlyList<CrewLocationDefinition> definitions, string file, string jsonPath = "$.locations")
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var errors = new DefinitionErrors();
        if (definitions.Count == 0) errors.Add(file, jsonPath, "At least one crew location is required.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var defaults = 0;
        for (var i = 0; i < definitions.Count; i++)
        {
            var location = definitions[i];
            var path = string.Create(CultureInfo.InvariantCulture, $"{jsonPath}[{i}]");
            if (!IsName(location.Id)) errors.Add(file, path + ".id", $"'{location.Id}' is not a snake_case id.");
            else if (location.Id == CurrentId) errors.Add(file, path + ".id", $"'{CurrentId}' is reserved for tasks that keep the assignee where they are.");
            else if (!ids.Add(location.Id)) errors.Add(file, path + ".id", $"Duplicate crew location '{location.Id}'.");
            if (location.Cabin is { } cabin && string.IsNullOrWhiteSpace(cabin)) errors.Add(file, path + ".cabin", "Cabin is empty; leave it out for a suited location.");
            if (location.Vehicle is { } vehicle && string.IsNullOrWhiteSpace(vehicle)) errors.Add(file, path + ".vehicle", "Vehicle is empty.");
            if (location.Id == EvaId && !location.IsSuited) errors.Add(file, path + ".cabin", $"'{EvaId}' is the suited location and has no cabin.");
            if (!(location.AngularRateFactor >= 0 && double.IsFinite(location.AngularRateFactor)))
                errors.Add(file, path + ".angularRateFactor", "Must be a finite number >= 0.");
            if (!location.Default) continue;
            defaults++;
            if (location.IsSuited) errors.Add(file, path + ".default", "The default location must be inside a cabin.");
        }
        if (definitions.Count > 0 && defaults != 1) errors.Add(file, jsonPath, "Exactly one crew location is the default.");
        errors.ThrowIfAny();

        var sorted = definitions.ToArray();
        Array.Sort(sorted, (a, b) => string.CompareOrdinal(a.Id, b.Id));
        return new CrewLocations(sorted);
    }

    /// <returns>The index, or -1.</returns>
    public int IndexOf(string id)
    {
        for (var i = 0; i < _locations.Length; i++)
        {
            if (string.Equals(_locations[i].Id, id, StringComparison.Ordinal)) return i;
        }
        return -1;
    }

    public bool Contains(string id) => IndexOf(id) >= 0;

    /// <summary>True for a location id or <see cref="CurrentId"/>, i.e. anything a task may name as its location.</summary>
    public bool IsTaskLocation(string id) => id == CurrentId || Contains(id);

    /// <summary>Reports cabins and vehicles that are not mission vehicles.</summary>
    public void CheckVehicles(IReadOnlyCollection<string> vehicles, DefinitionErrors errors, string file, string jsonPath = "$.locations")
    {
        ArgumentNullException.ThrowIfNull(vehicles);
        ArgumentNullException.ThrowIfNull(errors);
        foreach (var location in _locations)
        {
            if (location.Cabin is { } cabin && !vehicles.Contains(cabin))
                errors.Add(file, jsonPath, $"Crew location '{location.Id}' names unknown cabin vehicle '{cabin}'.");
            if (location.Vehicle is { } vehicle && !vehicles.Contains(vehicle))
                errors.Add(file, jsonPath, $"Crew location '{location.Id}' names unknown vehicle '{vehicle}'.");
        }
    }

    internal static bool IsName(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        foreach (var c in value)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')) return false;
        }
        return true;
    }
}
