using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.Rendering;
using Usa.Game.Presentation.Spacecraft;
using Usa.Simulation.Crew;
using Usa.Simulation.Forecast;
using Usa.Simulation.Guidance;
using Usa.Simulation.Session;
using Usa.Simulation.Time;
using Usa.Simulation.Vehicle.Definitions;
using DiagramState = Usa.Game.Presentation.Diagram.DiagramLookState;
using SimComponentState = Usa.Simulation.Knowledge.ComponentKnowledgeState;

namespace Usa.Game.Integration.Adapters;

/// <summary>
/// Every simulation → presentation enum copy, by name with explicit switches (never by ordinal), so reordering
/// either side breaks a test instead of silently shifting meaning.
/// </summary>
public static class AdapterMaps
{
    public static AlarmPriority Priority(AlarmLevel level) => level switch
    {
        AlarmLevel.Caution => AlarmPriority.Caution,
        AlarmLevel.Warning => AlarmPriority.Warning,
        AlarmLevel.Master => AlarmPriority.Master,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };

    public static CompressionCause Cause(CompressionReason reason) => reason switch
    {
        CompressionReason.CriticalAlarm => CompressionCause.CriticalAlarm,
        CompressionReason.HighAngularRate => CompressionCause.HighAngularRate,
        CompressionReason.BurnInProgress => CompressionCause.BurnInProgress,
        CompressionReason.Docking => CompressionCause.Docking,
        CompressionReason.EntryBelowInterface => CompressionCause.EntryBelowInterface,
        CompressionReason.ParachutePhase => CompressionCause.ParachutePhase,
        CompressionReason.EventApproaching => CompressionCause.EventApproaching,
        CompressionReason.WorseningCondition => CompressionCause.WorseningCondition,
        CompressionReason.CrewTaskRunning => CompressionCause.CrewTaskRunning,
        CompressionReason.StableCoast => CompressionCause.StableCoast,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    /// <summary>Mission phase id (mission.json) → header chip, one chip per id; an id the console does not know reads as docked ops.</summary>
    public static MissionPhase Phase(string phaseId) => phaseId switch
    {
        "docking_ops" => MissionPhase.DockingOps,
        "docked_ops" => MissionPhase.DockedOps,
        "emergency" => MissionPhase.Emergency,
        "return_prep" => MissionPhase.ReturnPrep,
        "entry" => MissionPhase.Entry,
        "recovery" => MissionPhase.Recovery,
        "ended" => MissionPhase.Ended,
        _ => MissionPhase.DockedOps,
    };

    /// <summary>Objective state word (<c>mission.objective.&lt;id&gt;.state</c>); still achievable at the end means abandoned.</summary>
    public static ObjectiveState Objective(string state, bool missionEnded) => state switch
    {
        "completed" => ObjectiveState.Completed,
        "unacceptable_risk" => ObjectiveState.UnacceptableRisk,
        "impossible" => ObjectiveState.Impossible,
        _ => missionEnded ? ObjectiveState.Abandoned : ObjectiveState.Open,
    };

    public static LoadPriorityGroup Priority(LoadPriority priority) => priority switch
    {
        LoadPriority.Survival => LoadPriorityGroup.Survival,
        LoadPriority.Return => LoadPriorityGroup.Return,
        LoadPriority.Support => LoadPriorityGroup.Support,
        LoadPriority.Discretionary => LoadPriorityGroup.Discretionary,
        _ => throw new ArgumentOutOfRangeException(nameof(priority), priority, null),
    };

    public static TipStage Stage(HintStage stage) => stage switch
    {
        HintStage.None => TipStage.None,
        HintStage.Nudge => TipStage.Nudge,
        HintStage.Investigate => TipStage.Investigate,
        HintStage.Connect => TipStage.Connect,
        HintStage.Recommend => TipStage.Recommend,
        HintStage.Direct => TipStage.Direct,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
    };

    public static ForecastConfidenceLevel Confidence(ForecastConfidence confidence) => confidence switch
    {
        ForecastConfidence.Low => ForecastConfidenceLevel.Low,
        ForecastConfidence.Medium => ForecastConfidenceLevel.Medium,
        ForecastConfidence.High => ForecastConfidenceLevel.High,
        _ => throw new ArgumentOutOfRangeException(nameof(confidence), confidence, null),
    };

    public static ForecastCoverage Coverage(PlanCoverage coverage) => coverage switch
    {
        PlanCoverage.NoPlanEnd => ForecastCoverage.NoPlanEnd,
        PlanCoverage.Covered => ForecastCoverage.Covered,
        PlanCoverage.AtRisk => ForecastCoverage.AtRisk,
        PlanCoverage.NotCovered => ForecastCoverage.NotCovered,
        _ => throw new ArgumentOutOfRangeException(nameof(coverage), coverage, null),
    };

    public static ForecastInputKnowledge Knowledge(InputKnowledge knowledge) => knowledge switch
    {
        InputKnowledge.Fresh => ForecastInputKnowledge.Fresh,
        InputKnowledge.Stale => ForecastInputKnowledge.Stale,
        InputKnowledge.Unknown => ForecastInputKnowledge.Unknown,
        _ => throw new ArgumentOutOfRangeException(nameof(knowledge), knowledge, null),
    };

    /// <summary>Knowledge of a component as a console condition; a confirmed damage reads its confirmed condition word.</summary>
    public static ComponentCondition Condition(SimComponentState state, string? conditionWord) => state switch
    {
        SimComponentState.Unknown => ComponentCondition.Unknown,
        SimComponentState.Suspected => ComponentCondition.Suspected,
        SimComponentState.ConfirmedNominal => ComponentCondition.Nominal,
        SimComponentState.ConfirmedDamaged => conditionWord switch
        {
            "isolated" => ComponentCondition.Isolated,
            "degraded" => ComponentCondition.Degraded,
            _ => ComponentCondition.Failed,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    public static DiagramState Diagram(SimComponentState state) => state switch
    {
        SimComponentState.Unknown => DiagramState.Unknown,
        SimComponentState.Suspected => DiagramState.Suspected,
        SimComponentState.ConfirmedNominal => DiagramState.ConfirmedNominal,
        SimComponentState.ConfirmedDamaged => DiagramState.ConfirmedDamaged,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    public static ComponentLookState Look(SimComponentState state) => state switch
    {
        SimComponentState.Unknown => ComponentLookState.Unknown,
        SimComponentState.Suspected => ComponentLookState.Suspected,
        SimComponentState.ConfirmedNominal => ComponentLookState.ConfirmedNominal,
        SimComponentState.ConfirmedDamaged => ComponentLookState.ConfirmedDamaged,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    /// <summary>Parachute <c>state</c> word → visible chute (MODELING 7: stowed → Stowed, deployed → Open, cut → Released).</summary>
    public static ChuteDeployState Chute(string stateWord) => stateWord switch
    {
        "deployed" => ChuteDeployState.Open,
        "cut" => ChuteDeployState.Released,
        _ => ChuteDeployState.Stowed,
    };

    public static string BandWord(HealthBand band) => band switch
    {
        HealthBand.Stable => "stable",
        HealthBand.Stressed => "stressed",
        HealthBand.Impaired => "impaired",
        HealthBand.Critical => "critical",
        HealthBand.Deceased => "deceased",
        _ => throw new ArgumentOutOfRangeException(nameof(band), band, null),
    };
}
