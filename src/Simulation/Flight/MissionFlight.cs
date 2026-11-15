using Usa.Simulation.Data;
using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Flight;

/// <summary>Validated flight configuration of one mission: its vehicles, body, start orbit, recovery zones and entry limits.</summary>
public sealed class MissionFlight
{
    public const string FileName = "flight.json";

    private MissionFlight(
        MissionFlightDefinition definition, CelestialBody body, FlightVehicles vehicles, RetroSequence retro, IReadOnlyList<DefinitionError> warnings)
    {
        Definition = definition;
        Body = body;
        Vehicles = vehicles;
        RetroSequence = retro;
        Warnings = warnings;
        Zones = new RecoveryZones(body.Id, definition.EarthRotationAngleAtMetZeroDeg * Angles.DegToRad,
            body.RotationRateRadPerS, definition.RecoveryZones, definition.OutsideZonesWaitHours);
        Stations = definition.Tracking is { } tracking ? [.. tracking.Stations.OrderBy(s => s.Id, StringComparer.Ordinal)] : [];
    }

    public MissionFlightDefinition Definition { get; }

    public CelestialBody Body { get; }

    /// <summary>Which vehicle is crewed, which (if any) is the target, and whether they dock.</summary>
    public FlightVehicles Vehicles { get; }

    public RecoveryZones Zones { get; }

    public EntryCorridor Corridor => Definition.EntryCorridor;

    public CapsuleAerodynamics Capsule => Definition.Capsule;

    /// <summary>
    /// The crewed vehicle's retro rockets in firing order: the vehicle data selected by <c>retroRockets</c>, or the
    /// deprecated <c>retroSequence</c> while no vehicle data is supplied.
    /// </summary>
    public RetroSequence RetroSequence { get; }

    /// <summary>Deprecations found while loading (e.g. the legacy <c>retroSequence</c>); the data still loaded.</summary>
    public IReadOnlyList<DefinitionError> Warnings { get; }

    public PlanningSettings Planning => Definition.Planning;

    /// <summary>Ground tracking stations sorted by id; empty when the mission has none.</summary>
    public IReadOnlyList<TrackingStation> Stations { get; }

    public double TrackingMinElevationDeg => Definition.Tracking?.MinElevationDeg ?? 0;

    /// <summary>Loads <c>missions/&lt;missionId&gt;/flight.json</c>.</summary>
    /// <param name="vehicleRetroRockets">
    /// Every retro rocket of the mission's vehicles as the vehicle definitions describe them (see
    /// <see cref="RetroRocket.FromVehicleSpec"/>); <c>retroRockets</c> picks from these. Null keeps the deprecated
    /// <c>retroSequence</c> path for callers that don't supply vehicle data yet.
    /// </param>
    public static MissionFlight Load(
        IDataSource source, CelestialBodies bodies, string missionId, IReadOnlyList<RetroRocket>? vehicleRetroRockets = null)
    {
        var path = $"missions/{missionId}/{FileName}";
        var definition = new DefinitionLoader(source).Load<MissionFlightDefinition>(path, MissionFlightDefinition.SchemaTag);
        return FromDefinition(definition, bodies, path, vehicleRetroRockets);
    }

    public static MissionFlight FromDefinition(
        MissionFlightDefinition definition, CelestialBodies bodies, string fileLabel, IReadOnlyList<RetroRocket>? vehicleRetroRockets = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(bodies);
        var errors = new DefinitionErrors();
        if (!bodies.TryGet(definition.Body, out var body))
        {
            errors.Add(fileLabel, "$.body", $"Unknown body '{definition.Body}'.");
            errors.ThrowIfAny();
        }

        ValidateStartOrbit(definition.StartOrbit, body, fileLabel, errors);
        var vehicles = FlightVehicles.Create(definition.Vehicles, definition.DockingPair, fileLabel, errors);
        if (definition.UndockSeparationSpeedMps < 0) errors.Add(fileLabel, "$.undockSeparationSpeedMps", "Must not be negative.");
        ValidateZones(definition, fileLabel, errors);
        ValidateCorridor(definition.EntryCorridor, body, fileLabel, errors);
        ValidateCapsule(definition.Capsule, fileLabel, errors);
        var warnings = new List<DefinitionError>();
        var retro = ResolveRetro(definition, vehicleRetroRockets, fileLabel, errors, warnings);
        if (retro is not null)
        {
            var retroPath = ReferenceEquals(retro, definition.RetroSequence) ? "$.retroSequence.rockets" : "$.retroRockets";
            ValidateRetro(retro, vehicles?.CrewedId, retroPath, fileLabel, errors);
        }
        ValidatePlanning(definition.Planning, fileLabel, errors);
        ValidateTracking(definition.Tracking, fileLabel, errors);
        definition.Sun?.Validate(fileLabel, "$.sun", errors);
        errors.ThrowIfAny();
        return new MissionFlight(definition, body, vehicles!, retro!, warnings);
    }

    /// <summary>The vehicle's rockets named by <c>retroRockets</c>; the legacy <c>retroSequence</c> only without vehicle data.</summary>
    private static RetroSequence? ResolveRetro(
        MissionFlightDefinition definition, IReadOnlyList<RetroRocket>? vehicleRockets, string file,
        DefinitionErrors errors, List<DefinitionError> warnings)
    {
        var legacy = definition.RetroSequence;
        if (definition.RetroRockets is { } ids && vehicleRockets is not null)
        {
            var resolved = new List<RetroRocket>(ids.Count);
            for (var i = 0; i < ids.Count; i++)
            {
                RetroRocket? match = null;
                for (var r = 0; r < vehicleRockets.Count; r++)
                {
                    if (vehicleRockets[r].Id == ids[i]) match = vehicleRockets[r];
                }
                if (match is null) errors.Add(file, $"$.retroRockets[{i}]", $"No vehicle has a retro rocket '{ids[i]}'.");
                else resolved.Add(match);
            }
            if (legacy is not null)
            {
                var agrees = legacy.Rockets.SequenceEqual(resolved);
                warnings.Add(new DefinitionError(file, "$.retroSequence",
                    "Deprecated and ignored: the retro rockets come from the vehicle definition" +
                    (agrees ? "." : "; it also disagrees with the vehicle data. Delete it.")));
            }
            return new RetroSequence(resolved);
        }

        if (legacy is not null)
        {
            warnings.Add(new DefinitionError(file, "$.retroSequence",
                "Deprecated: list the crewed vehicle's retro-rocket ids in $.retroRockets and pass the vehicle's retro rockets to MissionFlight.Load."));
            return legacy;
        }

        errors.Add(file, "$.retroRockets", definition.RetroRockets is null
            ? "Name the crewed vehicle's retro rockets."
            : "The retro rockets are listed by id, but no vehicle retro-rocket data was supplied.");
        return null;
    }

    /// <summary>The crewed vehicle's orbit with its epoch at <paramref name="startTime"/>.</summary>
    public Conic StartConic(MissionTime startTime)
    {
        var orbit = Definition.StartOrbit;
        return Conic.FromApsides(
            Body.MuM3PerS2,
            Body.RadiusM + orbit.PeriapsisKm * 1000,
            Body.RadiusM + orbit.ApoapsisKm * 1000,
            orbit.ArgPeriapsisDeg * Angles.DegToRad,
            orbit.TrueAnomalyDeg * Angles.DegToRad,
            startTime,
            orbit.Direction == OrbitDirection.Prograde ? 1 : -1);
    }

    private static void ValidateStartOrbit(StartOrbitDefinition orbit, CelestialBody body, string file, DefinitionErrors errors)
    {
        if (orbit.PeriapsisKm * 1000 <= body.InterfaceAltitudeM)
            errors.Add(file, "$.startOrbit.periapsisKm", "Start periapsis must be above the atmosphere interface.");
        if (orbit.ApoapsisKm < orbit.PeriapsisKm) errors.Add(file, "$.startOrbit.apoapsisKm", "Apoapsis is below periapsis.");
        if ((body.RadiusM + orbit.ApoapsisKm * 1000) >= body.SoiRadiusM)
            errors.Add(file, "$.startOrbit.apoapsisKm", "Apoapsis lies outside the body's sphere of influence.");
    }

    private static void ValidateZones(MissionFlightDefinition definition, string file, DefinitionErrors errors)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < definition.RecoveryZones.Count; i++)
        {
            var zone = definition.RecoveryZones[i];
            var path = $"$.recoveryZones[{i}]";
            if (string.IsNullOrWhiteSpace(zone.Id)) errors.Add(file, path + ".id", "Zone id is empty.");
            else if (!ids.Add(zone.Id)) errors.Add(file, path + ".id", $"Duplicate zone id '{zone.Id}'.");
            if (zone.WestLongitudeDeg is < -180 or > 180) errors.Add(file, path + ".westLongitudeDeg", "Must be within [-180, 180].");
            if (zone.EastLongitudeDeg is < -180 or > 180) errors.Add(file, path + ".eastLongitudeDeg", "Must be within [-180, 180].");
            if (zone.WaitHours < 0) errors.Add(file, path + ".waitHours", "Must not be negative.");
        }
        if (definition.OutsideZonesWaitHours < 0) errors.Add(file, "$.outsideZonesWaitHours", "Must not be negative.");
    }

    private static void ValidateCorridor(EntryCorridor corridor, CelestialBody body, string file, DefinitionErrors errors)
    {
        if (corridor.InterfaceAltitudeM <= 0 || corridor.InterfaceAltitudeM > body.InterfaceAltitudeM)
            errors.Add(file, "$.entryCorridor.interfaceAltitudeM", "Must be positive and not above the body's atmosphere interface.");
        if (corridor.ShallowestFlightPathAngleDeg >= 0) errors.Add(file, "$.entryCorridor.shallowestFlightPathAngleDeg", "Must be negative (descending).");
        if (corridor.SteepestFlightPathAngleDeg >= corridor.ShallowestFlightPathAngleDeg)
            errors.Add(file, "$.entryCorridor.steepestFlightPathAngleDeg", "Must be steeper (more negative) than the shallowest limit.");
    }

    private static void ValidateCapsule(CapsuleAerodynamics capsule, string file, DefinitionErrors errors)
    {
        if (capsule.BallisticCoefficientKgPerM2 <= 0) errors.Add(file, "$.capsule.ballisticCoefficientKgPerM2", "Must be positive.");
        ValidateChute(capsule.Drogue, "$.capsule.drogue", file, errors);
        ValidateChute(capsule.Main, "$.capsule.main", file, errors);
        if (capsule.Drogue.BallisticCoefficientKgPerM2 >= capsule.BallisticCoefficientKgPerM2)
            errors.Add(file, "$.capsule.drogue.ballisticCoefficientKgPerM2", "The drogue must lower β below the bare capsule's.");
        if (capsule.Main.BallisticCoefficientKgPerM2 >= capsule.Drogue.BallisticCoefficientKgPerM2)
            errors.Add(file, "$.capsule.main.ballisticCoefficientKgPerM2", "The main chute must lower β below the drogue's.");
        if (capsule.Main.NominalDeployAltitudeM >= capsule.Drogue.NominalDeployAltitudeM)
            errors.Add(file, "$.capsule.main.nominalDeployAltitudeM", "The main chute deploys below the drogue.");
    }

    private static void ValidateChute(ChuteSpec chute, string path, string file, DefinitionErrors errors)
    {
        if (chute.BallisticCoefficientKgPerM2 <= 0) errors.Add(file, path + ".ballisticCoefficientKgPerM2", "Must be positive.");
        if (chute.NominalDeployAltitudeM <= 0) errors.Add(file, path + ".nominalDeployAltitudeM", "Must be positive.");
        if (chute.MaxDeployAltitudeM < chute.NominalDeployAltitudeM) errors.Add(file, path + ".maxDeployAltitudeM", "Must not be below the nominal altitude.");
        if (chute.MaxDeploySpeedMps <= 0) errors.Add(file, path + ".maxDeploySpeedMps", "Must be positive.");
    }

    /// <param name="crewedId">Retro rockets belong to the crewed vehicle (component ids are <c>&lt;vehicle&gt;/…</c>).</param>
    private static void ValidateRetro(RetroSequence retro, string? crewedId, string listPath, string file, DefinitionErrors errors)
    {
        if (retro.Rockets.Count == 0) errors.Add(file, listPath, "At least one retro rocket is required.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < retro.Rockets.Count; i++)
        {
            var rocket = retro.Rockets[i];
            var path = $"{listPath}[{i}]";
            if (!ids.Add(rocket.Id.Value)) errors.Add(file, path, $"Duplicate rocket '{rocket.Id}'.");
            if (crewedId is not null && !rocket.Id.Value.StartsWith(crewedId + "/", StringComparison.Ordinal))
                errors.Add(file, path, $"Retro rocket '{rocket.Id}' is not part of the crewed vehicle '{crewedId}'.");
            if (!(rocket.IgnitionOffsetS >= 0 && double.IsFinite(rocket.IgnitionOffsetS))) errors.Add(file, path, $"'{rocket.Id}': ignition offset must be finite and not negative.");
            if (!IsPositive(rocket.BurnDurationS)) errors.Add(file, path, $"'{rocket.Id}': burn duration must be positive.");
            if (!IsPositive(rocket.ThrustN)) errors.Add(file, path, $"'{rocket.Id}': thrust must be positive.");
            if (!IsPositive(rocket.IspS)) errors.Add(file, path, $"'{rocket.Id}': Isp must be positive and finite (propellant mass positive).");
            // Thrust is sampled once per flight step, so an edge between steps would quantise the impulse by up to one step.
            if (!IsWholeSteps(rocket.IgnitionOffsetS)) errors.Add(file, path, $"'{rocket.Id}': ignition offset must be a whole multiple of {FlightSystem.StepMs} ms.");
            if (!IsWholeSteps(rocket.BurnDurationS)) errors.Add(file, path, $"'{rocket.Id}': burn duration must be a whole multiple of {FlightSystem.StepMs} ms.");
        }
    }

    private static bool IsPositive(double value) => double.IsFinite(value) && value > 0;

    private static bool IsWholeSteps(double seconds)
    {
        if (!double.IsFinite(seconds)) return true;
        var steps = seconds * 1000 / FlightSystem.StepMs;
        return Math.Abs(steps - Math.Round(steps)) < 1e-6;
    }

    private static void ValidateTracking(TrackingDefinition? tracking, string file, DefinitionErrors errors)
    {
        if (tracking is null) return;
        if (tracking.MinElevationDeg is < 0 or >= 90) errors.Add(file, "$.tracking.minElevationDeg", "Must be within [0, 90).");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < tracking.Stations.Count; i++)
        {
            var station = tracking.Stations[i];
            var path = $"$.tracking.stations[{i}]";
            if (string.IsNullOrWhiteSpace(station.Id)) errors.Add(file, path + ".id", "Station id is empty.");
            else if (!ids.Add(station.Id)) errors.Add(file, path + ".id", $"Duplicate station id '{station.Id}'.");
            if (station.LongitudeDeg is < -180 or > 180) errors.Add(file, path + ".longitudeDeg", "Must be within [-180, 180].");
        }
    }

    private static void ValidatePlanning(PlanningSettings planning, string file, DefinitionErrors errors)
    {
        if (planning.PreviewHorizonS <= 0) errors.Add(file, "$.planning.previewHorizonS", "Must be positive.");
        if (planning.ZoneScanStepS <= 0) errors.Add(file, "$.planning.zoneScanStepS", "Must be positive.");
        if (planning.ZoneSearchWindowOrbits <= 0) errors.Add(file, "$.planning.zoneSearchWindowOrbits", "Must be positive.");
        if (planning.MaxBurnDurationS <= 0) errors.Add(file, "$.planning.maxBurnDurationS", "Must be positive.");
        if (planning.StartTimeToleranceS < 0.001) errors.Add(file, "$.planning.startTimeToleranceS", "Must be at least 1 ms.");
    }
}
