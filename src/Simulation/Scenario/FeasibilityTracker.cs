using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;
using Usa.Simulation.Scenario.Model;

namespace Usa.Simulation.Scenario;

/// <summary>
/// Owns every objective's state and the feasibility timeline (GDD 10.3, SCRIPTING 11.4). Stepped each second by
/// <see cref="MissionDirector"/>. The worst matching rule wins (impossible over unacceptable_risk); its cause is
/// decided once, when the change happens. A non-latched unacceptable_risk returns to achievable when no rule holds.
/// </summary>
public sealed class FeasibilityTracker : IHashContributor
{
    private const long NotSet = -1;

    private readonly Objective[] _objectives;
    private readonly FeasibilityRule[][] _rules;
    private readonly ObjectiveState[] _states;
    private readonly FeasibilityCause?[] _causes;
    private readonly bool[] _latched;
    private readonly FeasibilityCause?[] _lossCauses;
    private readonly long[] _lostMs;
    private readonly long[] _completedMs;
    private readonly Dictionary<ObjectiveId, int> _index = [];
    private readonly List<FeasibilityTransition> _history = [];
    private readonly IStateQuery _state;
    private readonly EventBus _events;

    public FeasibilityTracker(IReadOnlyList<Objective> objectives, IStateQuery state, EventBus events)
    {
        ArgumentNullException.ThrowIfNull(objectives);
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _objectives = objectives.ToArray();
        _rules = new FeasibilityRule[_objectives.Length][];
        _states = new ObjectiveState[_objectives.Length];
        _causes = new FeasibilityCause?[_objectives.Length];
        _latched = new bool[_objectives.Length];
        _lossCauses = new FeasibilityCause?[_objectives.Length];
        _lostMs = new long[_objectives.Length];
        _completedMs = new long[_objectives.Length];
        for (var i = 0; i < _objectives.Length; i++)
        {
            _index.Add(_objectives[i].Id, i);
            _rules[i] = _objectives[i].Feasibility.ToArray();
            _lostMs[i] = NotSet;
            _completedMs[i] = NotSet;
        }
    }

    public int Count => _objectives.Length;

    public IReadOnlyList<FeasibilityTransition> History => _history;

    public ObjectiveState GetState(ObjectiveId id) => _states[IndexOf(id)];

    /// <summary>Cause of the latest change; null while achievable or completed.</summary>
    public FeasibilityCause? GetCause(ObjectiveId id) => _causes[IndexOf(id)];

    public ObjectiveOutcome GetOutcome(ObjectiveId id) => GetOutcome(IndexOf(id));

    public void WriteHash(StateHashWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        for (var i = 0; i < _objectives.Length; i++)
        {
            writer.Write(_objectives[i].Id.Value);
            writer.Write((long)_states[i]);
            writer.Write(_causes[i] is { } cause ? (long)cause : NotSet);
            writer.Write(_latched[i]);
            writer.Write(_lossCauses[i] is { } loss ? (long)loss : NotSet);
            writer.Write(_lostMs[i]);
            writer.Write(_completedMs[i]);
        }
        writer.Write((long)_history.Count);
    }

    internal ObjectiveState StateAt(int index) => _states[index];

    internal FeasibilityCause? CauseAt(int index) => _causes[index];

    internal ObjectiveOutcome GetOutcome(int i) => new(
        _objectives[i].Id,
        _objectives[i].Kind,
        _objectives[i].WeightPct,
        _states[i],
        _lossCauses[i],
        _lostMs[i] == NotSet ? null : new MissionTime(_lostMs[i]),
        _completedMs[i] == NotSet ? null : new MissionTime(_completedMs[i]));

    internal void Evaluate(MissionTime now)
    {
        for (var i = 0; i < _objectives.Length; i++)
        {
            var current = _states[i];
            if (current is ObjectiveState.Completed or ObjectiveState.Impossible) continue;

            FeasibilityRule? worst = null;
            var rules = _rules[i];
            for (var r = 0; r < rules.Length; r++)
            {
                var rule = rules[r];
                if ((worst is null || Severity(rule.State) > Severity(worst.State)) && rule.When.IsTrue(_state)) worst = rule;
            }
            var latched = worst is not null && LatchedAt(rules, worst);

            if (worst is null)
            {
                if (current == ObjectiveState.UnacceptableRisk && !_latched[i])
                    Transition(i, ObjectiveState.Achievable, null, "feasibility_restored", now);
                continue;
            }
            if (Severity(worst.State) <= Severity(current))
            {
                if (worst.State == current) _latched[i] |= latched;
                continue;
            }

            _latched[i] |= latched;
            // A loss that worsens keeps the cause decided when the objective was first lost (GDD 10.3).
            var cause = current == ObjectiveState.UnacceptableRisk && _lossCauses[i] is { } lost ? lost : worst.Cause.Decide(_state);
            Transition(i, worst.State, cause, worst.Id, now);
        }
    }

    /// <summary>Applies a mission rule that just activated: listed achievable objectives become latched unacceptable_risk.</summary>
    internal void ApplyRule(MissionRule rule, FeasibilityCause cause, MissionTime now)
    {
        foreach (var id in rule.ObjectivesUnacceptable)
        {
            var i = IndexOf(id);
            if (_states[i] == ObjectiveState.Achievable)
            {
                _latched[i] = true;
                Transition(i, ObjectiveState.UnacceptableRisk, cause, rule.Id, now);
            }
            else if (_states[i] == ObjectiveState.UnacceptableRisk)
            {
                _latched[i] = true;
            }
        }
    }

    internal void Complete(int index, MissionTime now)
    {
        if (_states[index] != ObjectiveState.Achievable)
            throw new InvalidOperationException($"Objective '{_objectives[index].Id}' is {_states[index]} and cannot complete.");
        _completedMs[index] = now.Milliseconds;
        Transition(index, ObjectiveState.Completed, null, "completed", now);
    }

    private void Transition(int i, ObjectiveState to, FeasibilityCause? cause, string reason, MissionTime now)
    {
        var from = _states[i];
        _states[i] = to;
        _causes[i] = cause;
        if (from == ObjectiveState.Achievable && to is ObjectiveState.UnacceptableRisk or ObjectiveState.Impossible)
        {
            _lossCauses[i] = cause;
            _lostMs[i] = now.Milliseconds;
        }
        else if (to == ObjectiveState.Achievable)
        {
            _lossCauses[i] = null;
            _lostMs[i] = NotSet;
        }

        var id = _objectives[i].Id;
        _history.Add(new FeasibilityTransition(now, id, from, to, cause, reason));
        if (to != ObjectiveState.Completed) _events.Publish(new ObjectiveFeasibilityChanged(now, id, from, to, cause, reason));
    }

    /// <summary>True when any rule that holds at <paramref name="worst"/>'s state latches, not only the first one found.</summary>
    private bool LatchedAt(FeasibilityRule[] rules, FeasibilityRule worst)
    {
        if (worst.Latched) return true;
        foreach (var rule in rules)
        {
            if (rule.Latched && rule.State == worst.State && !ReferenceEquals(rule, worst) && rule.When.IsTrue(_state)) return true;
        }
        return false;
    }

    private static int Severity(ObjectiveState state) => state switch
    {
        ObjectiveState.Impossible => 2,
        ObjectiveState.UnacceptableRisk => 1,
        _ => 0,
    };

    private int IndexOf(ObjectiveId id) =>
        _index.TryGetValue(id, out var index) ? index : throw new ArgumentException($"Unknown objective '{id}'.", nameof(id));
}
