using Usa.Simulation.Attitude;
using Usa.Simulation.Commands;
using Usa.Simulation.Crew;
using Usa.Simulation.Crew.Health;
using Usa.Simulation.Crew.Tasks;
using Usa.Simulation.Data;
using Usa.Simulation.Engineering;
using Usa.Simulation.Expressions;
using Usa.Simulation.Flight;
using Usa.Simulation.Forecast;
using Usa.Simulation.Guidance;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;
using Usa.Simulation.LifeSupport;
using Usa.Simulation.LifeSupport.State;
using Usa.Simulation.Ports;
using Usa.Simulation.Scenario;
using Usa.Simulation.Scoring;
using Usa.Simulation.Time;
using Usa.Simulation.Vehicle;
using Usa.Simulation.Vehicle.Docking;
using Usa.Simulation.Vehicle.Power;
using Usa.Simulation.Vehicle.Propulsion;
using Usa.Simulation.Vehicle.Sensors;

namespace Usa.Simulation.Session;

/// <summary>
/// The composition root of the simulation (SCRIPTING 7.6): builds every area from <see cref="MissionData"/>, registers every
/// state provider and effect handler, and fixes the tick order.
/// <para><b>Tick order</b> (systems with equal periods run in this order within a tick):</para>
/// <list type="number">
/// <item>50 ms: attitude (bridge fires every vehicle thruster, drawing propellant), then the crewed vehicle's peak roll.</item>
/// <item>100 ms: flight (samples session burns), burn controller (attitude hold, retro mass), docking coordinator (pending
/// undock), entry-checklist chutes, run tracker.</item>
/// <item>1 s: electrical and thruster activity per vehicle, docking load, attitude mass sync, life support, health, crew tasks,
/// glance reads, knowledge staleness, ground reports (attitude mode, recovery report), downlink, incidents, mission director, mission events (recovery), alarms, guidance
/// inputs, guidance, tutorial. Truth first, then what crew and ground learn, then scenario, then annunciation and guidance,
/// so every 1 s system sees this tick's physics.</item>
/// <item>30 s (forecast.json): survival forecast. 60 s: state hash, last.</item>
/// </list>
/// </summary>
internal static class SessionComposer
{
    /// <param name="validate">Fail on unknown health exposure paths; the integrity check turns this off to report them with everything else.</param>
    /// <exception cref="DefinitionException">An exposure path is unknown (when validating).</exception>
    public static SessionParts Compose(MissionData data, ulong seed, IExpressionCompiler compiler, bool validate = true)
    {
        RequireFlightVehicles(data);
        var crewedVehicle = data.CrewedVehicleId;
        var exposure = data.ExposurePaths;
        var events = new EventBus();
        var state = new CompositeStateQuery();
        var router = new EffectRouter(state);
        var mission = data.Scenario.Mission;

        state.Register(new StaticStateProvider(StatePaths.Difficulty, data.Difficulty.Values));
        var variants = new VariantPicker(data.Scenario.Incidents.Variants, seed);
        state.Register(variants);

        var vehicles = new VehicleSet(data.Vehicles, events);
        var roster = new CrewRoster(data.Crew, data.Health, data.Locations);
        state.Register(roster);
        router.Register(new CrewRationHandler(roster));
        router.Register(new CrewMoveHandler(roster));

        var environment = new LifeSupportEnvironment(vehicles, roster);
        var lifeSupport = LifeSupportSystem.Create(
            data.LifeSupport,
            LifeSupportSpecs.Create(data.Vehicles),
            roster.Members.Select(m => m.Id),
            data.Difficulty.GetNumber("resource_margin", 0),
            environment,
            events);
        environment.Attach(lifeSupport);
        var cabin = new CabinStateProvider(lifeSupport.Cabins);
        var thermal = new ThermalStateProvider(lifeSupport.Thermal);
        var provisions = new ProvisionsStateProvider(lifeSupport.Provisions);
        foreach (var provider in (IStateProvider[])[cabin, thermal, provisions]) state.Register(provider);
        foreach (var handler in (IEffectHandler[])[cabin, thermal, provisions]) router.Register(handler);

        foreach (var vehicle in vehicles.States)
        {
            var fallback = new ComponentFieldFallback(vehicle) { Cabin = cabin, CabinEffects = cabin, Thermal = thermal, ThermalEffects = thermal };
            var paths = new VehicleComponentPaths(vehicle, fallback, fallback);
            state.Register(paths);
            router.Register(paths);
        }
        var totals = new VehicleTotalsPaths(vehicles.States);
        state.Register(totals);
        router.Register(totals);

        var attitudeBridge = new AttitudeBridge(vehicles);
        var attitude = new AttitudeSystem(data.Attitude, vehicles.States.Select(AttitudeSpecs.Create).ToArray(), events, attitudeBridge);
        attitudeBridge.Attach(attitude);
        // session.json sun overrides flight.json sun; mission 1 has neither, so it serves no Sun paths and heats no node by sunlight.
        var sunDefinition = data.SessionConfig.Sun ?? data.Flight.Definition.Sun;
        state.Register(new AttitudeStateProvider(attitude, sunDefinition?.InertialAngleDeg));
        var peakRoll = new PeakRollTracker(attitude, crewedVehicle);
        state.Register(peakRoll);
        router.Register(new AttitudeEffectHandler(attitude));

        var director = new MissionDirector(mission, state, events);
        state.Register(new MissionStateProvider(director));
        var missionEvents = new MissionEventBridge(director, vehicles, crewedVehicle, events);

        var burns = new BurnController(
            vehicles, attitude, crewedVehicle, data.SessionConfig.BurnEngines, MissionSessionConfig.PathFor(data.MissionId));
        var flightInputs = new FlightInputs(vehicles) { Burns = burns };
        var flight = new FlightSystem(data.Flight, flightInputs, events, mission.StartTime);
        burns.Attach(flight);
        var sun = sunDefinition is null ? null : new SunModel(sunDefinition, flight);
        environment.Solar = Solar(data, sun, attitude, vehicles, lifeSupport);
        state.Register(new FlightStateProvider(flight, sun));
        state.Register(new GroundReportPaths(attitude, data.Attitude, flight, state));
        router.Register(new FlightEffectHandler(flight));

        var dockingModel = data.TargetVehicleId is { } targetVehicle && data.Flight.Vehicles.TargetDocks
            ? new DockingModel(vehicles.Get(targetVehicle), vehicles.Get(crewedVehicle), state)
            : null;
        var targetControlSet = data.Attitude.Vehicles.FirstOrDefault(v => v.Id == data.TargetVehicleId)?.InitialControlSet;
        var docking = new DockingCoordinator(flight, attitude, dockingModel, burns, events, targetControlSet);
        router.Register(docking);
        router.Register(new RetrofireGate(burns));

        // Every component can be confirmed by inspection or test; the vehicle "revealable" flag only marks 3D objects.
        var knowledge = new KnowledgeBase(data.Facts, new ObservationAccess(roster, vehicles), events, vehicles.AllComponents().ToArray());
        state.Register(knowledge);

        // X8: what the ground knows of mass and orbits; reads truth only to fix, writes nothing the simulation reads back.
        var massLedgers = new GroundMassLedgers(data.Vehicles, data.Facts, knowledge, state, burns, flight, data.Navigation.LedgerFraction);
        var tracking = new GroundTracking(
            flight, data.Navigation, seed, data.Difficulty.GetNumber(DifficultyProfile.NavUncertaintyScale, 1), massLedgers,
            vehicle => attitude.TryGetVehicle(vehicle, out var held) && held.Mode != AttitudeMode.Off && held.ControlSetId.Length > 0);
        burns.CommandedBurn = tracking.RecordCommandedBurn;
        SimulationClock? navClock = null;
        state.Register(new NavStatePaths(tracking, massLedgers, data.Flight.Vehicles.Ids, () => navClock?.Now ?? mission.StartTime));

        var inventory = new Inventory(data.Parts, data.Inventory, events);
        state.Register(inventory);
        router.Register(inventory);

        var reader = new FactValueReader(data.Facts, vehicles, state, roster, compiler, Pcg32.ForStream(seed, SensorModel.NoiseStream));
        var binder = new TaskBinder(compiler, data.Parts.IsTool);
        var runner = new CrewTaskRunner(
            events, Pcg32.ForStream(seed, CrewTaskRunner.ErrorStreamName), roster, data.Crew.Performance,
            new TaskOutcomeSink(router, knowledge, reader, events));
        var starter = new CrewTaskStarter(data.Tasks, binder, runner, state);

        // Raw templates: {cabin} and {vehicle} resolve per member and place; health and crew.<id>.env.* read the same source.
        var exposurePaths = new StateExposureSource(state, data.Health.ExposurePaths, roster);
        var exposures = new SessionExposureSource(exposurePaths, lifeSupport.Provisions, data.Health.ExposurePaths);
        foreach (var provider in CrewEnvironmentStateProvider.ForRoster(roster, exposures)) state.Register(provider);
        var health = new HealthModel(data.Health, roster, exposures, events);
        var telemetry = new InstrumentTelemetry(vehicles, state, reader, Pcg32.ForStream(seed, InstrumentTelemetry.NoiseStreamName));
        var telemetrySampler = TelemetrySampler(data, knowledge, telemetry, compiler);

        var coordinator = new VehicleEventCoordinator(vehicles, attitude, flight, lifeSupport.Thermal, events);
        var chutes = new ChuteSequencer(flight, coordinator, router, data.Tasks, binder, data.SessionConfig.EntryChecklist);
        var repairs = new RepairWorkspace(data.Parts, data.Repairs, inventory, vehicles, knowledge, events);
        SimulationClock? clock = null;
        var session = new SessionPaths(burns, repairs, chutes, () => clock?.Now ?? mission.StartTime);
        state.Register(session);
        router.Register(session);

        // Read() falls back to nominal for an unknown path, which would hide a broken exposure (review X3).
        var exposureErrors = new DefinitionErrors();
        exposurePaths.Validate(exposureErrors);
        if (validate) exposureErrors.ThrowIfAny();

        var failures = new FailureDirector(data.Scenario.Incidents, variants, seed, state, router, events, director);
        var alarms = new AlarmMonitor(state, data.Health, exposure, data.Crew.Performance, data.Facts, vehicles, crewedVehicle, events);

        var executed = new ExecutedCommands();
        var log = new CommandLog();
        var remote = new EffectRouterRemoteControl(router);
        var crewIds = roster.Members.Select(m => m.Id).OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
        var meta = new MetaCommandExecutor(runner, alarms, knowledge, repairs, events, data.Facts, telemetry, crewIds);
        var validator = new CommandValidator(
            data.Actions, state, new CrewDirectory(roster, runner, data.Tasks), executed, remote, starter,
            new ImpactAnalyzer(mission, state, data.Tasks, binder), meta);
        var glances = new GlanceReadQueue(knowledge, reader, roster);
        var processor = new CommandProcessor(validator, executed, log, events, starter, remote, glances, meta);

        var guidanceSettings = GuidanceSettings.FromDifficulty(state);
        var guidance = new GuidanceDirector(data.Hints, guidanceSettings, state, events);
        var guidanceBridge = new GuidanceBridge(guidance, runner, burns, alarms, events);
        var tutorial = new TutorialDirector(data.Tutorial, state, events, data.Difficulty.GetBool("tutorial_default", true));
        var forecast = new ForecastRunner(new SurvivalForecast(data.Forecast), data.Facts, knowledge, reader, roster, lifeSupport.Provisions);
        var tracker = new RunTracker(flight, alarms, vehicles.Get(crewedVehicle), events);

        var hashOrder = new List<IHashContributor>();
        var hashes = new HashRecorder(() => StateHasher.Compute(hashOrder), events);

        var tickOrder = new List<ISimSystem> { attitude, peakRoll, flight, tracking, burns, docking, chutes, tracker };
        foreach (var vehicle in vehicles.States) tickOrder.Add(new ElectricalSolver(vehicle));
        foreach (var vehicle in vehicles.States) tickOrder.Add(new ThrusterActivityModel(vehicle));
        if (dockingModel is not null) tickOrder.Add(dockingModel);
        tickOrder.AddRange(
        [
            coordinator, lifeSupport, health, runner, glances, knowledge,
        ]);
        tickOrder.Add(new GroundReportRecorder(knowledge, data.Facts, state, compiler));
        if (telemetrySampler is not null) tickOrder.Add(telemetrySampler);
        tickOrder.AddRange(
        [
            new DownlinkSampler(knowledge, data.Facts, state),
            failures, director, missionEvents, alarms, guidanceBridge, guidance, tutorial, forecast, hashes,
        ]);
        clock = new SimulationClock(tickOrder, mission.StartTime);
        navClock = clock;

        hashOrder.Add(new ClockHash(clock));
        hashOrder.AddRange(vehicles.States);
        hashOrder.AddRange(
        [
            attitude, attitudeBridge, flight, tracking, burns, docking, chutes, tracker, lifeSupport, environment, roster, runner, glances,
            knowledge, new RngHash(reader), inventory, repairs, variants, failures, director, missionEvents, alarms, guidance,
            tutorial, forecast, log, executed, peakRoll, telemetry,
        ]);

        var parts = new SessionParts
        {
            Data = data,
            Seed = seed,
            Compiler = compiler,
            Events = events,
            State = state,
            Router = router,
            Clock = clock,
            TickOrder = tickOrder,
            HashOrder = hashOrder,
            Vehicles = vehicles,
            Attitude = attitude,
            AttitudeBridge = attitudeBridge,
            Flight = flight,
            Burns = burns,
            Tracking = tracking,
            MassLedgers = massLedgers,
            Docking = docking,
            LifeSupport = lifeSupport,
            Roster = roster,
            Health = health,
            ExposurePaths = exposurePaths,
            Runner = runner,
            Binder = binder,
            Starter = starter,
            Knowledge = knowledge,
            FactReader = reader,
            Inventory = inventory,
            Repairs = repairs,
            Variants = variants,
            Director = director,
            Failures = failures,
            MissionEvents = missionEvents,
            Executed = executed,
            Log = log,
            Validator = validator,
            Processor = processor,
            Alarms = alarms,
            Guidance = guidance,
            GuidanceBridge = guidanceBridge,
            Tutorial = tutorial,
            Forecast = forecast,
            Tracker = tracker,
            Scoring = new MissionScoring(data.Scoring),
            TimeCompression = new TimeCompressionDirector(data.TimeCompression),
            Hashes = hashes,
            Glances = glances,
            DockingTasks = new HashSet<TaskId>(data.SessionConfig.DockingTasks ?? []),
            ReadCache = new SessionReadCache(data, roster, guidanceSettings),
        };
        clock.CoarseSteps = new AfloatCoarseSteps(parts);
        return parts;
    }

    /// <summary>
    /// Solar heating from the mission's Sun (<c>flight.json</c> <c>sun</c>, or the <c>session.json</c> override) when it has one;
    /// a thermal node that takes solar heat without one is a data error.
    /// </summary>
    /// <exception cref="DefinitionException">A vehicle node has solar heating but the mission has no <c>sun</c>.</exception>
    private static SolarEnvironment? Solar(MissionData data, SunModel? sun, AttitudeSystem attitude, VehicleSet vehicles, LifeSupportSystem lifeSupport)
    {
        if (sun is not null) return new SolarEnvironment(sun, attitude, vehicles, lifeSupport.Thermal);
        if (!LifeSupportSpecs.HasSolarHeating(data.Vehicles)) return null;
        var errors = new DefinitionErrors();
        errors.Add($"missions/{data.MissionId}/{MissionFlight.FileName}", "$.sun", "A vehicle thermal node takes solar heat, but the mission declares no Sun.");
        errors.ThrowIfAny();
        return null;
    }

    /// <summary>The instrument downlink of <c>session.json</c> <c>telemetry</c>, or null when the mission has none.</summary>
    /// <exception cref="DefinitionException">A channel names no sensor channel or a fact that is not a telemetry number fact.</exception>
    private static TelemetryReadingSampler? TelemetrySampler(MissionData data, KnowledgeBase knowledge, InstrumentTelemetry telemetry, IExpressionCompiler compiler)
    {
        if (data.SessionConfig.Telemetry is not { } config) return null;
        var file = MissionSessionConfig.PathFor(data.MissionId);
        var errors = new DefinitionErrors();
        var channels = new TelemetryChannel[config.Channels.Count];
        for (var i = 0; i < channels.Length; i++)
        {
            var channel = config.Channels[i];
            if (!telemetry.AddChannel(channel.Path)) errors.Add(file, $"$.telemetry.channels[{i}].path", $"'{channel.Path}' is not a sensor channel (<sensor>.<channel field>).");
            channels[i] = new TelemetryChannel(channel.Fact, channel.Path, channel.ValidMax);
        }
        IExpression? active = null;
        try
        {
            active = compiler.Compile(config.Active);
        }
        catch (ExpressionException ex)
        {
            errors.Add(file, "$.telemetry.active", ex.Message);
        }
        errors.ThrowIfAny();
        try
        {
            return new TelemetryReadingSampler(knowledge, data.Facts, telemetry, channels, active);
        }
        catch (ArgumentException ex)
        {
            errors.Add(file, "$.telemetry.channels", ex.Message);
            errors.ThrowIfAny();
            return null;
        }
    }

    /// <summary>Flight's vehicles must be among the mission's (<c>mission.json</c> <c>vehicles</c>).</summary>
    /// <exception cref="DefinitionException">A flight vehicle is not a vehicle of the mission.</exception>
    private static void RequireFlightVehicles(MissionData data)
    {
        var errors = new DefinitionErrors();
        var missionVehicles = data.Vehicles.Select(v => v.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in data.Flight.Vehicles.Ids)
        {
            if (!missionVehicles.Contains(id))
                errors.Add($"{MissionData.MissionDirectory(data.MissionId)}/{MissionFlight.FileName}", "$.vehicles", $"'{id}' is not in mission.json vehicles.");
        }
        errors.ThrowIfAny();
    }

    private sealed class ClockHash(SimulationClock clock) : IHashContributor
    {
        public void WriteHash(StateHashWriter writer)
        {
            writer.Write(clock.Now.Milliseconds);
            writer.Write(clock.TickIndex);
        }
    }

    private sealed class RngHash(FactValueReader reader) : IHashContributor
    {
        public void WriteHash(StateHashWriter writer) => writer.Write(unchecked((long)reader.NoiseState));
    }
}
