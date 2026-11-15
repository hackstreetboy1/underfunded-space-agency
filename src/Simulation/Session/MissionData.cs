using Usa.Simulation.Attitude;
using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Crew;
using Usa.Simulation.Crew.Health;
using Usa.Simulation.Crew.Tasks;
using Usa.Simulation.Data;
using Usa.Simulation.Engineering;
using Usa.Simulation.Flight;
using Usa.Simulation.Forecast;
using Usa.Simulation.Guidance;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge.Catalog;
using Usa.Simulation.LifeSupport.Config;
using Usa.Simulation.Ports;
using Usa.Simulation.Scenario;
using Usa.Simulation.Scoring;
using Usa.Simulation.Time;
using Usa.Simulation.Vehicle.Definitions;

namespace Usa.Simulation.Session;

/// <summary>
/// Every data file a mission session needs, loaded and validated on its own (cross-area checks come later). Files are read
/// through the mission's <see cref="MissionDataSource"/>, so the catalogs and the crew hold the shared entries plus the
/// mission's own; <see cref="Crew"/> is narrowed to the crew <c>mission.json</c> names.
/// </summary>
public sealed record MissionData(
    string MissionId,
    DifficultyProfile Difficulty,
    LoadedScenario Scenario,
    IReadOnlyList<VehicleDefinition> Vehicles,
    AttitudeConfigDefinition Attitude,
    CelestialBodies Bodies,
    MissionFlight Flight,
    LifeSupportConfig LifeSupport,
    CrewCatalog Crew,
    HealthConfig Health,
    TaskCatalog Tasks,
    FactCatalog Facts,
    ActionCatalog Actions,
    PartCatalog Parts,
    InventoryDefinition Inventory,
    RepairCatalog Repairs,
    ForecastConfig Forecast,
    ScoringConfig Scoring,
    TimeCompressionConfig TimeCompression,
    HintTree Hints,
    TutorialScript Tutorial,
    MissionSessionConfig SessionConfig)
{
    /// <summary>Placeholder in <c>config/health.json</c> exposure paths for the vehicle whose motion the crew feels.</summary>
    public const string VehiclePlaceholder = StateExposureSource.VehiclePlaceholder;

    /// <summary>Placeholder in <c>config/health.json</c> exposure paths for the vehicle whose cabin air the crew breathes.</summary>
    public const string CabinPlaceholder = StateExposureSource.CabinPlaceholder;

    /// <summary>Where the crew can be (<c>mission.json</c> <c>locations</c>, or a cabin in the crewed vehicle plus <c>eva</c>).</summary>
    public required CrewLocations Locations { get; init; }

    /// <summary>Ground tracking and the ground mass ledger's tolerances (<c>config/navigation.json</c>, X8).</summary>
    public required NavigationConfig Navigation { get; init; }

    /// <summary>The vehicle the crew flies (<c>flight.json</c> role <c>crewed</c>).</summary>
    public string CrewedVehicleId => Flight.Vehicles.CrewedId;

    /// <summary>The co-orbiting vehicle (<c>flight.json</c> role <c>target</c>), or null.</summary>
    public string? TargetVehicleId => Flight.Vehicles.TargetId;

    /// <summary>
    /// <c>config/health.json</c> exposure paths resolved for the default crew location (its cabin and the vehicle it rides), as
    /// the annunciator watches them. Health itself resolves them per member and place (<see cref="StateExposureSource"/>);
    /// <c>{crew}</c> stays for later.
    /// </summary>
    public CrewExposurePaths ExposurePaths =>
        ResolveExposurePaths(Health.ExposurePaths, Locations.Default.MotionVehicle ?? CrewedVehicleId, Locations.Default.Cabin ?? CrewedVehicleId);

    /// <param name="cabinId">Replaces <see cref="CabinPlaceholder"/>; defaults to <paramref name="vehicleId"/>.</param>
    public static CrewExposurePaths ResolveExposurePaths(CrewExposurePaths paths, string vehicleId, string? cabinId = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        string R(string path) => path
            .Replace(VehiclePlaceholder, vehicleId, StringComparison.Ordinal)
            .Replace(CabinPlaceholder, cabinId ?? vehicleId, StringComparison.Ordinal);
        return paths with
        {
            Ppco2Mmhg = R(paths.Ppco2Mmhg),
            Ppo2Kpa = R(paths.Ppo2Kpa),
            PressureKpa = R(paths.PressureKpa),
            TemperatureC = R(paths.TemperatureC),
            AngularRateDps = R(paths.AngularRateDps),
            GLoad = R(paths.GLoad),
            HydrationDeficitL = R(paths.HydrationDeficitL),
            NutritionDeficitKcal = R(paths.NutritionDeficitKcal),
            Fatigue = R(paths.Fatigue),
            Injury = R(paths.Injury),
        };
    }

    public static string MissionDirectory(string missionId) => $"missions/{missionId}";

    /// <summary>Loads every file, collecting every problem before failing.</summary>
    /// <exception cref="DefinitionException">Every problem found across the files.</exception>
    public static MissionData Load(IDataSource source, string missionId, string difficultyId, IExpressionCompiler compiler)
    {
        var errors = new DefinitionErrors();
        var data = TryLoad(source, missionId, difficultyId, compiler, errors);
        errors.ThrowIfAny();
        return data!;
    }

    /// <summary>Loads what it can; every failure is added to <paramref name="errors"/>.</summary>
    /// <returns>Null when any file failed to load.</returns>
    public static MissionData? TryLoad(IDataSource source, string missionId, string difficultyId, IExpressionCompiler compiler, DefinitionErrors errors)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(errors);
        source = MissionDataSource.For(source, missionId);
        var loader = new DefinitionLoader(source);
        var directory = MissionDirectory(missionId);

        var difficulty = Try(errors, DifficultyProfile.PathFor(difficultyId), () => DifficultyProfile.Load(loader, difficultyId));
        var scenario = Try(errors, $"{directory}/mission.json", () => new ScenarioLoader(source, compiler).Load(directory));
        var vehicles = scenario is null
            ? null
            : Try(errors, "vehicles", () => new VehicleDefinitionLoader(loader).LoadAll(VehicleDefinitionLoader.FilesFor(scenario.Mission.Vehicles)));
        var attitude = Try(errors, AttitudeConfigLoader.RelativePath, () => AttitudeConfigLoader.Load(source));
        var bodies = Try(errors, CelestialBodies.DefaultPath, () => CelestialBodies.Load(source));
        var flight = bodies is null || vehicles is null
            ? null
            : Try(errors, $"{directory}/{MissionFlight.FileName}", () => MissionFlight.Load(source, bodies, missionId, VehicleRetroRockets.From(vehicles)));
        var lifeSupport = Try(errors, LifeSupportConfig.RelativePath, () => LifeSupportConfigLoader.Load(source));
        var crew = scenario is null ? null : Try(errors, CrewCatalog.DefaultPath, () => LoadCrew(loader, scenario.Mission.Crew));
        var health = Try(errors, HealthConfig.DefaultPath, () => HealthConfig.Load(loader));
        var tasks = Try(errors, TaskCatalog.DefaultPath, () => TaskCatalog.Load(loader));
        var facts = Try(errors, FactCatalog.RelativePath, () => FactCatalog.Load(source));
        var actions = Try(errors, ActionsFile.DefaultPath, () => ActionCatalog.Load(loader, compiler));
        var parts = Try(errors, PartCatalog.DefaultPath, () => PartCatalog.Load(loader));
        var inventoryPath = InventoryDefinition.PathFor(missionId, difficultyId);
        var inventory = parts is null ? null : Try(errors, inventoryPath, () => InventoryDefinition.Load(loader, inventoryPath, parts));
        var repairs = parts is null
            ? null
            : Try(errors, RepairCatalog.PathFor(missionId), () => RepairCatalog.Load(loader, RepairCatalog.PathFor(missionId), parts, compiler));
        var forecast = Try(errors, ForecastConfig.RelativePath, () => ForecastConfigLoader.Load(source));
        var scoring = Try(errors, ScoringConfig.RelativePath, () => ScoringConfigLoader.Load(loader));
        var compression = Try(errors, TimeCompressionConfig.DefaultPath, () => TimeCompressionConfig.Load(loader));
        var hints = Try(errors, HintTreeLoader.PathFor(missionId), () => HintTreeLoader.Load(loader, missionId, compiler));
        var tutorial = Try(errors, TutorialLoader.PathFor(missionId), () => TutorialLoader.Load(loader, missionId, compiler));
        var sessionConfig = scenario is null
            ? null
            : Try(errors, MissionSessionConfig.PathFor(missionId), () => MissionSessionConfig.Load(loader, missionId, scenario.Mission.Vehicles));

        var navigation = Try(errors, NavigationConfig.RelativePath, () => NavigationConfig.Load(source));
        var locations = scenario is null || vehicles is null || flight is null
            ? null
            : Try(errors, $"{directory}/mission.json", () => LoadLocations(scenario.Mission, vehicles, flight.Vehicles.CrewedId, $"{directory}/mission.json"));

        if (difficulty is null || scenario is null || vehicles is null || attitude is null || bodies is null || flight is null ||
            lifeSupport is null || crew is null || health is null || tasks is null || facts is null || actions is null || parts is null ||
            inventory is null || repairs is null || forecast is null || scoring is null || compression is null || hints is null ||
            tutorial is null || sessionConfig is null || locations is null || navigation is null)
            return null;

        return new MissionData(missionId, difficulty, scenario, vehicles, attitude, bodies, flight, lifeSupport, crew, health, tasks, facts,
            actions, parts, inventory, repairs, forecast, scoring, compression, hints, tutorial, sessionConfig)
        {
            Locations = locations,
            Navigation = navigation,
        };
    }

    /// <summary>
    /// The mission's crew locations: <c>mission.json</c> <c>locations</c>, or a cabin in the crewed vehicle plus a suited
    /// <c>eva</c> tethered to it (rate factor 1) when it has none. Every cabin must be a mission vehicle that has a cabin.
    /// </summary>
    /// <exception cref="DefinitionException">Every problem with the locations.</exception>
    private static CrewLocations LoadLocations(Scenario.Model.Mission mission, IReadOnlyList<VehicleDefinition> vehicles, string crewedVehicle, string file)
    {
        var locations = mission.Locations.Count == 0
            ? CrewLocations.Basic(crewedVehicle)
            : CrewLocations.Create(
                [.. mission.Locations.Select(l => new CrewLocationDefinition(l.Id, l.Cabin, l.Vehicle, l.Default, l.AngularRateFactor))], file);
        var errors = new DefinitionErrors();
        locations.CheckVehicles(vehicles.Select(v => v.Id).ToArray(), errors, file);
        for (var i = 0; i < locations.Count; i++)
        {
            if (locations[i].Cabin is { } cabin && vehicles.FirstOrDefault(v => v.Id == cabin) is { Cabin: null })
                errors.Add(file, "$.locations", $"Crew location '{locations[i].Id}' names vehicle '{cabin}', which has no cabin.");
        }
        errors.ThrowIfAny();
        return locations;
    }

    /// <summary>The shared and mission crew files merged, narrowed to the members the mission flies (in catalog order).</summary>
    private static CrewCatalog LoadCrew(DefinitionLoader loader, IReadOnlyList<CrewId> missionCrew)
    {
        var file = loader.Load<CrewFileDefinition>(CrewCatalog.DefaultPath, CrewCatalog.SchemaTag);
        var flying = missionCrew.ToHashSet();
        return CrewCatalog.Create(file with { Members = file.Members.Where(m => flying.Contains(m.Id)).ToArray() }, CrewCatalog.DefaultPath);
    }

    private static T? Try<T>(DefinitionErrors errors, string file, Func<T> load) where T : class
    {
        try
        {
            return load();
        }
        catch (DefinitionException ex)
        {
            if (ex.Errors.Count == 0)
            {
                var prefix = $"{ex.File} {ex.JsonPath}: ";
                var message = ex.Message.StartsWith(prefix, StringComparison.Ordinal) ? ex.Message[prefix.Length..] : ex.Message;
                errors.Add(ex.File.Length > 0 ? ex.File : file, ex.JsonPath.Length > 0 ? ex.JsonPath : "$", message);
            }
            foreach (var error in ex.Errors) errors.Add(error.File, error.JsonPath, error.Message);
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException or ExpressionException)
        {
            errors.Add(file, "$", $"{ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }
}
