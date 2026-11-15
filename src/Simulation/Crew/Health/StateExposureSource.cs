using Usa.Simulation.Data;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Crew.Health;

/// <summary>
/// Reads exposures from the state paths configured in <c>config/health.json</c>. An empty path reads as nominal;
/// a configured path that no provider serves is an error on <see cref="Read"/>, never a silent nominal value
/// (call <see cref="Validate"/> at load to report every such path at once).
/// </summary>
/// <remarks>
/// Built from a <see cref="CrewRoster"/>, each member's paths follow where they are: <c>{cabin}</c> becomes the cabin
/// vehicle of the member's current <see cref="CrewLocationDefinition"/> and <c>{vehicle}</c> the vehicle they ride
/// (<see cref="CrewLocationDefinition.MotionVehicle"/>). A field that needs a cabin or vehicle the location does not have
/// reads the nominal (suit) value, and the angular rate is scaled by the location's
/// <see cref="CrewLocationDefinition.AngularRateFactor"/>. Built from crew ids alone, only <c>{crew}</c> is substituted.
/// </remarks>
public sealed class StateExposureSource : ICrewExposureSource
{
    public const string CrewPlaceholder = "{crew}";
    public const string CabinPlaceholder = "{cabin}";
    public const string VehiclePlaceholder = "{vehicle}";

    private const int FieldCount = 10;
    private const int AngularRateField = 4;

    /// <summary>Gas, pressure and temperature: what the member breathes, read from the cabin of their place.</summary>
    private const int CabinFieldCount = 4;

    private const string EnvironmentSegment = "." + CrewEnvironmentStateProvider.Segment;

    private static readonly string[] FieldNames =
    [
        "ppco2Mmhg", "ppo2Kpa", "pressureKpa", "temperatureC", "angularRateDps",
        "gLoad", "hydrationDeficitL", "nutritionDeficitKcal", "fatigue", "injury",
    ];

    private readonly IStateQuery _state;
    private readonly CrewId[] _crew;
    private readonly CrewRoster? _roster;
    private readonly CrewLocations? _locations;
    private readonly string[] _templates;
    private readonly bool _selfReferencing;

    /// <summary>[crew][location][field]; null reads nominal. One location column when built without a roster.</summary>
    private readonly string?[][][] _paths;

    public StateExposureSource(IStateQuery state, CrewExposurePaths paths, IEnumerable<CrewId> crew)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(crew);
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _crew = crew.ToArray();
        var templates = _templates = Templates(paths);
        _selfReferencing = Array.Exists(templates, IsCrewEnvironment);
        _paths = new string?[_crew.Length][][];
        for (var c = 0; c < _crew.Length; c++) _paths[c] = [Resolve(templates, _crew[c], location: null)];
    }

    /// <summary>Resolves every member's paths for every place they can be; see the remarks.</summary>
    public StateExposureSource(IStateQuery state, CrewExposurePaths paths, CrewRoster roster)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _roster = roster ?? throw new ArgumentNullException(nameof(roster));
        _locations = roster.Locations;
        var members = roster.Members;
        _crew = new CrewId[members.Count];
        var templates = _templates = Templates(paths);
        _selfReferencing = Array.Exists(templates, IsCrewEnvironment);
        _paths = new string?[_crew.Length][][];
        for (var c = 0; c < _crew.Length; c++)
        {
            _crew[c] = members[c].Id;
            _paths[c] = new string?[_locations.Count][];
            for (var l = 0; l < _locations.Count; l++) _paths[c][l] = Resolve(templates, _crew[c], _locations[l]);
        }
    }

    public CrewExposure Read(CrewId crew)
    {
        // A crew environment path would read this source again: fail here rather than overflow the stack (Validate reports it at load).
        if (_selfReferencing) throw new InvalidOperationException("An exposure path reads crew.<id>.env.*, which this source serves.");
        var index = Array.IndexOf(_crew, crew);
        if (index < 0) return CrewExposure.Nominal;
        var location = 0;
        var rateFactor = 1.0;
        if (_roster is not null && _roster.TryFind(crew, out var member))
        {
            location = member.LocationIndex;
            rateFactor = member.CurrentLocation.AngularRateFactor;
        }
        var p = _paths[index][location];
        var n = CrewExposure.Nominal;
        return new CrewExposure(
            Ppco2Mmhg: Number(p[0], n.Ppco2Mmhg),
            Ppo2Kpa: Number(p[1], n.Ppo2Kpa),
            PressureKpa: Number(p[2], n.PressureKpa),
            TemperatureC: Number(p[3], n.TemperatureC),
            AngularRateDps: Math.Abs(Number(p[AngularRateField], n.AngularRateDps)) * rateFactor,
            GLoad: Number(p[5], n.GLoad),
            HydrationDeficitL: Number(p[6], n.HydrationDeficitL),
            NutritionDeficitKcal: Number(p[7], n.NutritionDeficitKcal),
            Fatigue: Number(p[8], n.Fatigue),
            Injury: Number(p[9], n.Injury));
    }

    /// <summary>
    /// Reports every configured path that no state provider serves, for every member and place (call once at load); any path
    /// under <c>crew.&lt;id&gt;.env.</c> (that is this source's own output, so reading it would recurse without end); and, when
    /// built from a roster, a gas, pressure or temperature field that names <c>{vehicle}</c> (a suited member would breathe that
    /// vehicle's cabin air: use <c>{cabin}</c>).
    /// </summary>
    public void Validate(DefinitionErrors errors, string file = HealthConfig.DefaultPath)
    {
        ArgumentNullException.ThrowIfNull(errors);
        for (var f = 0; f < FieldCount; f++)
        {
            if (IsCrewEnvironment(_templates[f]))
                errors.Add(file, $"$.exposurePaths.{FieldNames[f]}",
                    $"'{_templates[f]}' is a crew environment path, which this exposure source itself serves; read the cabin, attitude or provisions path instead.");
            if (_locations is not null && f < CabinFieldCount && _templates[f].Contains(VehiclePlaceholder, StringComparison.Ordinal))
                errors.Add(file, $"$.exposurePaths.{FieldNames[f]}",
                    $"'{_templates[f]}' uses {VehiclePlaceholder}; cabin fields use {CabinPlaceholder} so a suited member reads suit values.");
        }
        for (var c = 0; c < _crew.Length; c++)
        {
            for (var l = 0; l < _paths[c].Length; l++)
            {
                var where = _locations is null ? "" : $" (at '{_locations[l].Id}')";
                for (var f = 0; f < FieldCount; f++)
                {
                    if (_paths[c][l][f] is { } path && !IsCrewEnvironment(path) && !_state.IsKnownPath(path))
                        errors.Add(file, $"$.exposurePaths.{FieldNames[f]}", $"Unknown state path '{path}'{where}.");
                }
            }
        }
    }

    /// <summary>True for <c>crew.&lt;anything&gt;.env.&lt;field&gt;</c> (placeholders included).</summary>
    private static bool IsCrewEnvironment(string path) =>
        path.StartsWith(StatePaths.Crew, StringComparison.Ordinal) &&
        path.Contains(EnvironmentSegment, StringComparison.Ordinal);

    private static string[] Templates(CrewExposurePaths paths) =>
    [
        paths.Ppco2Mmhg, paths.Ppo2Kpa, paths.PressureKpa, paths.TemperatureC, paths.AngularRateDps,
        paths.GLoad, paths.HydrationDeficitL, paths.NutritionDeficitKcal, paths.Fatigue, paths.Injury,
    ];

    /// <param name="location">Null: substitute <c>{crew}</c> only.</param>
    private static string?[] Resolve(string[] templates, CrewId crew, CrewLocationDefinition? location)
    {
        var resolved = new string?[FieldCount];
        for (var f = 0; f < FieldCount; f++)
        {
            var template = templates[f];
            if (template.Length == 0) continue;
            var path = template.Replace(CrewPlaceholder, crew.Value, StringComparison.Ordinal);
            if (location is not null)
            {
                if (path.Contains(CabinPlaceholder, StringComparison.Ordinal))
                {
                    if (location.Cabin is not { } cabin) continue;
                    path = path.Replace(CabinPlaceholder, cabin, StringComparison.Ordinal);
                }
                if (path.Contains(VehiclePlaceholder, StringComparison.Ordinal))
                {
                    if (location.MotionVehicle is not { } vehicle) continue;
                    path = path.Replace(VehiclePlaceholder, vehicle, StringComparison.Ordinal);
                }
            }
            resolved[f] = path;
        }
        return resolved;
    }

    private double Number(string? path, double nominal)
    {
        if (path is null) return nominal;
        if (!_state.TryGet(path, out var value)) throw new UnknownStatePathException(path);
        return value.Kind is StateValueKind.Number or StateValueKind.Bool
            ? value.AsNumber()
            : throw new InvalidOperationException($"Exposure path '{path}' serves {value.Kind}, not a number.");
    }
}
