using System.Text.Json;
using Usa.Simulation.Crew;
using Usa.Simulation.Crew.Health;
using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Scenario;
using Usa.Simulation.Scoring;

namespace Usa.Simulation.Session;

/// <summary>
/// Builds the scoring input from a run, and owns the explicit Scenario → Scoring mappings (review X6): enums are mapped by
/// name in switches, never cast; weights in percent become shares; return objectives are scored through Safe return and Crew
/// outcome, so only primary objectives enter the objectives list.
/// </summary>
internal static class MissionRecordBuilder
{
    /// <param name="rules">The mission's <c>session.json</c> <c>record</c> rules (docking damage rule, expected staging).</param>
    public static MissionRecord Build(
        RecordRulesDefinition? rules,
        MissionDirector director,
        CrewRoster roster,
        HealthModel health,
        FlightSystem flight,
        RunTracker tracker,
        double avoidablePropellantKg)
    {
        var outcome = director.GetOutcome();

        var crew = roster.Members
            .Select(m => new CrewHealthRecord(m.Id, m.IsAlive, m.LaunchHealth, m.WorstHealth, m.Health))
            .ToArray();
        var losses = new List<HealthLossRecord>();
        foreach (var summary in health.Summarize())
        {
            foreach (var cause in summary.Causes)
                losses.Add(new HealthLossRecord(summary.Crew, JsonNamingPolicy.SnakeCaseLower.ConvertName(cause.Cause.ToString()), cause.Damage));
        }

        var landing = flight.Landing;
        var retrofireMs = Fired(director, MissionEventIds.Retrofire)?.Milliseconds;
        // Without an expected staging event the return has nothing to jettison first.
        var stagedFirst = rules?.StagingBeforeRetrofireEvent is not { } staging
            || (Fired(director, staging)?.Milliseconds is { } stagedMs && (retrofireMs is not { } r || stagedMs <= r));
        var chutesOk = tracker.ChuteResults.Count > 0 && tracker.ChuteResults.All(r => r is ChuteDeployResult.Deployed or ChuteDeployResult.AlreadyDeployed);
        var ret = new ReturnRecord(
            landing is not null,
            landing?.Zone.ZoneId,
            landing?.Zone.RecoveryWaitHours ?? 0,
            tracker.EntryPeakG,
            stagedFirst,
            chutesOk);

        var objectives = new List<ObjectiveRecord>();
        var avoidable = new List<WeightedDecision>();
        var good = new List<WeightedDecision>();
        foreach (var objective in outcome.Objectives)
        {
            if (objective.Kind != ObjectiveKind.Primary) continue;
            var share = objective.WeightPct / 100.0;
            var transitions = outcome.FeasibilityHistory
                .Where(t => t.ObjectiveId == objective.Id)
                .Select(t => new ObjectiveTransition(t.Time.Seconds, ToFeasibility(t.To), ToCause(t.Cause)))
                .ToArray();
            var cause = ToCause(objective.LossCause);
            objectives.Add(new ObjectiveRecord(objective.Id, share, ToFinalState(objective.State), cause, transitions));
            if (cause == ObjectiveLossCause.PlayerAvoidable)
                avoidable.Add(new WeightedDecision($"objective_lost.{objective.Id}", share, objective.LostAt?.Seconds));
            if (objective.State == ObjectiveState.Completed)
                good.Add(new WeightedDecision($"objective_completed.{objective.Id}", share, objective.CompletedAt?.Seconds));
        }

        var redundancy = !tracker.AnyCriticalEverFailed ? RedundancyStatus.NeverLost
            : tracker.CriticalRemaining == tracker.CriticalTotal ? RedundancyStatus.Restored
            : RedundancyStatus.Lost;
        // Docking damage the mission judged a scenario loss (the crew had no known way to stop the roll) is not held against them.
        var dockingDamage = rules?.DockingDamageRule is { } rule &&
                            outcome.FeasibilityHistory.Any(t => t.Reason == rule && t.Cause == FeasibilityCause.Scenario)
            ? 0
            : tracker.DockingDamageCount;
        var vehicle = new VehicleConditionRecord(tracker.CriticalTotal, tracker.CriticalRemaining, redundancy, dockingDamage);
        var decisions = new DecisionMetrics(avoidablePropellantKg, tracker.MasterAlarmSeconds, avoidable, good);
        return new MissionRecord(crew, losses, ret, objectives, vehicle, decisions);
    }

    private static MissionTime? Fired(MissionDirector director, string eventId) =>
        director.Mission.Events.Any(e => e.Id == eventId) ? director.FiredAt(eventId) : null;

    public static ObjectiveFinalState ToFinalState(ObjectiveState state) => state switch
    {
        ObjectiveState.Completed => ObjectiveFinalState.Completed,
        ObjectiveState.Impossible => ObjectiveFinalState.Impossible,
        ObjectiveState.UnacceptableRisk => ObjectiveFinalState.UnacceptableRisk,
        ObjectiveState.Achievable => ObjectiveFinalState.Abandoned,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    public static ObjectiveFeasibility ToFeasibility(ObjectiveState state) => state switch
    {
        ObjectiveState.Achievable => ObjectiveFeasibility.Achievable,
        ObjectiveState.Completed => ObjectiveFeasibility.Completed,
        ObjectiveState.Impossible => ObjectiveFeasibility.Impossible,
        ObjectiveState.UnacceptableRisk => ObjectiveFeasibility.UnacceptableRisk,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    public static ObjectiveLossCause? ToCause(FeasibilityCause? cause) => cause switch
    {
        null => null,
        FeasibilityCause.Scenario => ObjectiveLossCause.Scenario,
        FeasibilityCause.PlayerAvoidable => ObjectiveLossCause.PlayerAvoidable,
        _ => throw new ArgumentOutOfRangeException(nameof(cause), cause, null),
    };
}
