using Usa.Simulation.Kernel;

namespace Usa.Simulation.Scenario;

/// <summary>One entry of the feasibility timeline (debrief and scoring). Completion is recorded too.</summary>
public sealed record FeasibilityTransition(
    MissionTime Time,
    ObjectiveId ObjectiveId,
    ObjectiveState From,
    ObjectiveState To,
    FeasibilityCause? Cause,
    string Reason);

public sealed record MissionRuleActivation(string RuleId, MissionTime Time, FeasibilityCause Cause);

public sealed record MissionEventRecord(string EventId, MissionTime Time);

/// <param name="LossCause">
/// Cause of the change that took the objective away from achievable and stuck; later worsening
/// (unacceptable_risk to impossible) keeps the original cause. Null while achievable or completed.
/// </param>
public sealed record ObjectiveOutcome(
    ObjectiveId Id,
    ObjectiveKind Kind,
    double WeightPct,
    ObjectiveState State,
    FeasibilityCause? LossCause,
    MissionTime? LostAt,
    MissionTime? CompletedAt);

/// <summary>Scenario summary handed to Scoring and the debrief.</summary>
public sealed record MissionOutcome(
    string MissionId,
    bool Ended,
    MissionOutcomeKind? Outcome,
    MissionTime? EndedAt,
    IReadOnlyList<ObjectiveOutcome> Objectives,
    IReadOnlyList<FeasibilityTransition> FeasibilityHistory,
    IReadOnlyList<MissionRuleActivation> ActivatedRules,
    IReadOnlyList<MissionEventRecord> Events);
