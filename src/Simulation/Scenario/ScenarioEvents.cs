using Usa.Simulation.Kernel;

namespace Usa.Simulation.Scenario;

public sealed record IncidentTriggered(MissionTime Time, IncidentId IncidentId) : ISimEvent;

public sealed record PhaseChanged(MissionTime Time, string From, string To) : ISimEvent;

/// <param name="Cause">Null when the objective returned to achievable.</param>
/// <param name="Reason">The feasibility rule or mission rule id that caused the change.</param>
public sealed record ObjectiveFeasibilityChanged(
    MissionTime Time,
    ObjectiveId ObjectiveId,
    ObjectiveState From,
    ObjectiveState To,
    FeasibilityCause? Cause,
    string Reason) : ISimEvent;

public sealed record ObjectiveCompleted(MissionTime Time, ObjectiveId ObjectiveId) : ISimEvent;

public sealed record MissionRuleActivated(
    MissionTime Time,
    string RuleId,
    FeasibilityCause Cause,
    ReturnRequirement? RequireReturn) : ISimEvent;

public sealed record MissionEventRaised(MissionTime Time, string EventId) : ISimEvent;

public sealed record MissionEnded(MissionTime Time, MissionOutcomeKind Outcome, string EndConditionId) : ISimEvent;
