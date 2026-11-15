using Usa.Simulation.Ports;

namespace Usa.Simulation.Scenario;

/// <summary>
/// Serves <c>mission.</c> paths: <c>met_s</c>, <c>phase</c>, <c>phase_entered_met_s</c>, <c>ended</c>,
/// <c>outcome</c> ('' | 'recovered' | 'all_crew_lost'), <c>return_required</c>, <c>return_target</c>
/// ('' | 'next_suitable_zone'), <c>objective.&lt;id&gt;.state|cause</c>, <c>rule.&lt;id&gt;.active|cause|met_s</c>,
/// <c>event.&lt;id&gt;.fired|met_s</c>, <c>condition.&lt;id&gt;</c> (named conditions, evaluated on read) and
/// <c>timer.&lt;id&gt;.s|running</c> plus, for deadlines, <c>timer.&lt;id&gt;.remaining_s|expired</c> (as of the latest step).
/// Times not yet reached read as -1; causes not set read as ''.
/// </summary>
public sealed class MissionStateProvider : IStateProvider
{
    private const double NotSetS = -1;

    private readonly MissionDirector _director;
    private readonly MissionPathTable _paths;

    public MissionStateProvider(MissionDirector director)
    {
        _director = director ?? throw new ArgumentNullException(nameof(director));
        _paths = new MissionPathTable(director.Mission);
    }

    public string Prefix => StatePaths.Mission;

    public bool IsKnownPath(string path) => _paths.Contains(path);

    public bool TryGet(string path, out StateValue value)
    {
        if (!_paths.TryGet(path, out var entry))
        {
            value = StateValue.None;
            return false;
        }
        value = Read(entry);
        return true;
    }

    private StateValue Read(MissionPath entry)
    {
        var i = entry.Index;
        switch (entry.Kind)
        {
            case MissionPathKind.MetS: return _director.Now.Seconds;
            case MissionPathKind.Phase: return _director.Phase;
            case MissionPathKind.PhaseEnteredMetS: return _director.PhaseEnteredAt.Seconds;
            case MissionPathKind.Ended: return _director.IsEnded;
            case MissionPathKind.Outcome: return ScenarioText.Of(_director.Outcome);
            case MissionPathKind.ReturnRequired: return _director.RequiredReturn is not null;
            case MissionPathKind.ReturnTarget: return ScenarioText.Of(_director.RequiredReturn);
            case MissionPathKind.ObjectiveState: return ScenarioText.Of(_director.Feasibility.StateAt(i));
            case MissionPathKind.ObjectiveCause: return ScenarioText.Of(_director.Feasibility.CauseAt(i));
            case MissionPathKind.RuleActive: return _director.RuleActivation(i) is not null;
            case MissionPathKind.RuleCause: return ScenarioText.Of(_director.RuleActivation(i)?.Cause);
            case MissionPathKind.RuleMetS: return _director.RuleActivation(i)?.Time.Seconds ?? NotSetS;
            case MissionPathKind.EventFired: return _director.EventFiredMs(i) >= 0;
            case MissionPathKind.EventMetS:
                var firedMs = _director.EventFiredMs(i);
                return firedMs >= 0 ? firedMs / 1000.0 : NotSetS;
            case MissionPathKind.Condition: return _director.EvaluateCondition(i);
            case MissionPathKind.TimerS: return _director.TimerElapsedMs(i) / 1000.0;
            case MissionPathKind.TimerRunning: return _director.TimerRunning(i);
            case MissionPathKind.TimerRemainingS: return _director.TimerRemainingMs(i) / 1000.0;
            case MissionPathKind.TimerExpired: return _director.TimerExpired(i);
            default: throw new InvalidOperationException($"Unhandled mission path kind {entry.Kind}.");
        }
    }
}
