using Usa.Simulation.Ports;
using Usa.Simulation.Scenario.Model;

namespace Usa.Simulation.Scenario;

internal enum MissionPathKind
{
    MetS,
    Phase,
    PhaseEnteredMetS,
    Ended,
    Outcome,
    ReturnRequired,
    ReturnTarget,
    ObjectiveState,
    ObjectiveCause,
    RuleActive,
    RuleCause,
    RuleMetS,
    EventFired,
    EventMetS,
    Condition,
    TimerS,
    TimerRunning,
    TimerRemainingS,
    TimerExpired,
}

internal readonly record struct MissionPath(MissionPathKind Kind, int Index);

/// <summary>Every <c>mission.</c> path of a mission, precomputed so lookups never parse or allocate.</summary>
internal sealed class MissionPathTable
{
    private readonly Dictionary<string, MissionPath> _paths = new(StringComparer.Ordinal);

    public MissionPathTable(Mission mission)
    {
        const string p = StatePaths.Mission;
        Add(p + "met_s", MissionPathKind.MetS);
        Add(p + "phase", MissionPathKind.Phase);
        Add(p + "phase_entered_met_s", MissionPathKind.PhaseEnteredMetS);
        Add(p + "ended", MissionPathKind.Ended);
        Add(p + "outcome", MissionPathKind.Outcome);
        Add(p + "return_required", MissionPathKind.ReturnRequired);
        Add(p + "return_target", MissionPathKind.ReturnTarget);
        for (var i = 0; i < mission.Objectives.Count; i++)
        {
            var id = mission.Objectives[i].Id.Value;
            Add($"{p}objective.{id}.state", MissionPathKind.ObjectiveState, i);
            Add($"{p}objective.{id}.cause", MissionPathKind.ObjectiveCause, i);
        }
        for (var i = 0; i < mission.Rules.Count; i++)
        {
            var id = mission.Rules[i].Id;
            Add($"{p}rule.{id}.active", MissionPathKind.RuleActive, i);
            Add($"{p}rule.{id}.cause", MissionPathKind.RuleCause, i);
            Add($"{p}rule.{id}.met_s", MissionPathKind.RuleMetS, i);
        }
        for (var i = 0; i < mission.Events.Count; i++)
        {
            var id = mission.Events[i].Id;
            Add($"{p}event.{id}.fired", MissionPathKind.EventFired, i);
            Add($"{p}event.{id}.met_s", MissionPathKind.EventMetS, i);
        }
        for (var i = 0; i < mission.Conditions.Count; i++)
        {
            Add($"{p}condition.{mission.Conditions[i].Id}", MissionPathKind.Condition, i);
        }
        for (var i = 0; i < mission.Timers.Count; i++)
        {
            var timer = mission.Timers[i];
            Add($"{p}timer.{timer.Id}.s", MissionPathKind.TimerS, i);
            Add($"{p}timer.{timer.Id}.running", MissionPathKind.TimerRunning, i);
            if (timer.DurationMs is null) continue;
            Add($"{p}timer.{timer.Id}.remaining_s", MissionPathKind.TimerRemainingS, i);
            Add($"{p}timer.{timer.Id}.expired", MissionPathKind.TimerExpired, i);
        }
    }

    public const string ConditionPrefix = StatePaths.Mission + "condition.";

    public IEnumerable<string> Paths => _paths.Keys;

    public bool TryGet(string path, out MissionPath entry) => _paths.TryGetValue(path, out entry);

    public bool Contains(string path) => _paths.ContainsKey(path);

    // Duplicate ids are reported by MissionCompiler; the first keeps the path so the remaining checks still run.
    private void Add(string path, MissionPathKind kind, int index = 0) => _paths.TryAdd(path, new MissionPath(kind, index));
}
