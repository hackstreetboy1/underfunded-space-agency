using Usa.Simulation.Attitude;
using Usa.Simulation.Commands;
using Usa.Simulation.Crew;
using Usa.Simulation.Crew.Health;
using Usa.Simulation.Crew.Tasks;
using Usa.Simulation.Engineering;
using Usa.Simulation.Flight;
using Usa.Simulation.Guidance;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;
using Usa.Simulation.LifeSupport;
using Usa.Simulation.Ports;
using Usa.Simulation.Scenario;
using Usa.Simulation.Scoring;
using Usa.Simulation.Time;

namespace Usa.Simulation.Session;

/// <summary>Everything <see cref="SessionComposer"/> builds; the facade and the integrity check read it.</summary>
internal sealed class SessionParts
{
    public required MissionData Data { get; init; }
    public required ulong Seed { get; init; }
    public required IExpressionCompiler Compiler { get; init; }
    public required EventBus Events { get; init; }
    public required CompositeStateQuery State { get; init; }
    public required EffectRouter Router { get; init; }
    public required SimulationClock Clock { get; init; }
    public required IReadOnlyList<ISimSystem> TickOrder { get; init; }
    public required IReadOnlyList<IHashContributor> HashOrder { get; init; }
    public required VehicleSet Vehicles { get; init; }
    public required AttitudeSystem Attitude { get; init; }
    public required AttitudeBridge AttitudeBridge { get; init; }
    public required FlightSystem Flight { get; init; }
    public required BurnController Burns { get; init; }
    public required GroundTracking Tracking { get; init; }
    public required GroundMassLedgers MassLedgers { get; init; }
    public required DockingCoordinator Docking { get; init; }
    public required LifeSupportSystem LifeSupport { get; init; }
    public required CrewRoster Roster { get; init; }
    public required HealthModel Health { get; init; }
    public required StateExposureSource ExposurePaths { get; init; }
    public required CrewTaskRunner Runner { get; init; }
    public required TaskBinder Binder { get; init; }
    public required CrewTaskStarter Starter { get; init; }
    public required KnowledgeBase Knowledge { get; init; }
    public required FactValueReader FactReader { get; init; }
    public required Inventory Inventory { get; init; }
    public required RepairWorkspace Repairs { get; init; }
    public required VariantPicker Variants { get; init; }
    public required MissionDirector Director { get; init; }
    public required FailureDirector Failures { get; init; }
    public required MissionEventBridge MissionEvents { get; init; }
    public required ExecutedCommands Executed { get; init; }
    public required CommandLog Log { get; init; }
    public required CommandValidator Validator { get; init; }
    public required CommandProcessor Processor { get; init; }
    public required AlarmMonitor Alarms { get; init; }
    public required GuidanceDirector Guidance { get; init; }
    public required GuidanceBridge GuidanceBridge { get; init; }
    public required TutorialDirector Tutorial { get; init; }
    public required ForecastRunner Forecast { get; init; }
    public required RunTracker Tracker { get; init; }
    public required MissionScoring Scoring { get; init; }
    public required TimeCompressionDirector TimeCompression { get; init; }
    public required HashRecorder Hashes { get; init; }
    public required GlanceReadQueue Glances { get; init; }

    /// <summary>Host-read data resolved once (snapshot facts, objective paths, pacing).</summary>
    public required SessionReadCache ReadCache { get; init; }

    /// <summary>Crew tasks that count as docking activity (<c>session.json</c> <c>dockingTasks</c>).</summary>
    public required IReadOnlySet<TaskId> DockingTasks { get; init; }
}
