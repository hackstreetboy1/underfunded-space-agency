using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;
using Usa.Simulation.Scenario.Model;

namespace Usa.Simulation.Scenario;

/// <summary>
/// Runs the mission script each second (SCRIPTING 11.4): mission timers, conditional mission events, latched mission rules,
/// feasibility, objective completion, phases and end conditions, in that order. Ends only when an end condition
/// fires (recovery, or every crew member lost, D10); a single death does not end the mission.
/// State is served under <c>mission.</c> by <see cref="MissionStateProvider"/>.
/// </summary>
/// <remarks>
/// An event-triggered phase transition fires only for events raised after the current phase was entered (by
/// raise order, not MET), so a phase re-entered by a condition is never left again on an older event.
/// </remarks>
public sealed class MissionDirector : ISimSystem, IHashContributor, IMissionEventSink
{
    private const long NotSet = -1;

    private readonly Mission _mission;
    private readonly IStateQuery _state;
    private readonly EventBus _events;
    private readonly Dictionary<string, int> _eventIndex = new(StringComparer.Ordinal);
    private readonly long[] _eventFiredMs;
    private readonly long[] _eventSequence;
    private readonly MissionEventSpec[] _eventSpecs;
    private readonly string[] _phases;
    private readonly PhaseTransition[] _transitions;
    private readonly bool[][] _transitionFrom;
    private readonly int[] _transitionTo;
    private readonly int[] _transitionEvent;
    private readonly EndCondition[] _endConditions;
    private readonly int[] _endEvent;
    private readonly MissionRule[] _rules;
    private readonly MissionRuleActivation?[] _activations;
    private readonly Objective[] _objectives;
    private readonly MissionTimer[] _timers;
    private readonly long[] _timerElapsedMs;
    private readonly bool[] _timerRunning;
    private readonly long[] _timerExpiredMs;
    private readonly int _endedPhase;
    private int _phase;
    private long _phaseEnteredMs;
    private long _phaseEnteredSequence;
    private long _lastEventSequence;
    private MissionTime _now;
    private MissionOutcomeKind? _outcome;
    private long _endedMs = NotSet;

    public MissionDirector(Mission mission, IStateQuery state, EventBus events)
    {
        _mission = mission ?? throw new ArgumentNullException(nameof(mission));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        Feasibility = new FeasibilityTracker(mission.Objectives, state, events);

        _eventSpecs = mission.Events.ToArray();
        _eventFiredMs = new long[_eventSpecs.Length];
        _eventSequence = new long[_eventSpecs.Length];
        for (var i = 0; i < _eventSpecs.Length; i++)
        {
            _eventIndex.Add(_eventSpecs[i].Id, i);
            _eventFiredMs[i] = NotSet;
        }

        _phases = mission.Phases.ToArray();
        _transitions = mission.Transitions.ToArray();
        _transitionFrom = new bool[_transitions.Length][];
        _transitionTo = new int[_transitions.Length];
        _transitionEvent = new int[_transitions.Length];
        for (var t = 0; t < _transitions.Length; t++)
        {
            _transitionFrom[t] = new bool[_phases.Length];
            foreach (var from in _transitions[t].From) _transitionFrom[t][PhaseIndex(from)] = true;
            _transitionTo[t] = PhaseIndex(_transitions[t].To);
            _transitionEvent[t] = EventIndexOrNone(_transitions[t].Trigger);
        }

        _endConditions = mission.EndConditions.ToArray();
        _endEvent = new int[_endConditions.Length];
        for (var e = 0; e < _endConditions.Length; e++) _endEvent[e] = EventIndexOrNone(_endConditions[e].Trigger);

        _rules = mission.Rules.ToArray();
        _activations = new MissionRuleActivation?[_rules.Length];
        _objectives = mission.Objectives.ToArray();
        _timers = mission.Timers.ToArray();
        _timerElapsedMs = new long[_timers.Length];
        _timerRunning = new bool[_timers.Length];
        _timerExpiredMs = new long[_timers.Length];
        _timerExpiredMs.AsSpan().Fill(NotSet);
        _endedPhase = PhaseIndex(mission.EndedPhase);
        _phase = PhaseIndex(mission.StartPhase);
        _now = mission.StartTime;
        _phaseEnteredMs = _now.Milliseconds;
    }

    public int PeriodTicks => SimulationClock.TicksPerSecond;

    public Mission Mission => _mission;

    public FeasibilityTracker Feasibility { get; }

    /// <summary>MET of the latest step or raised event.</summary>
    public MissionTime Now => _now;

    public string Phase => _phases[_phase];

    public MissionTime PhaseEnteredAt => new(_phaseEnteredMs);

    public bool IsEnded => _endedMs != NotSet;

    public MissionOutcomeKind? Outcome => _outcome;

    public MissionTime? EndedAt => IsEnded ? new MissionTime(_endedMs) : null;

    /// <summary>
    /// The return demand of the active mission rules, or null. <see cref="ReturnRequirement"/> has one member today;
    /// rank the members here when a second one is added.
    /// </summary>
    public ReturnRequirement? RequiredReturn
    {
        get
        {
            for (var r = 0; r < _rules.Length; r++)
            {
                if (_activations[r] is not null && _rules[r].RequireReturn is { } requirement) return requirement;
            }
            return null;
        }
    }

    public bool RaiseEvent(string eventId, MissionTime now)
    {
        ArgumentNullException.ThrowIfNull(eventId);
        if (!_eventIndex.TryGetValue(eventId, out var index))
            throw new ArgumentException($"Unknown mission event '{eventId}'.", nameof(eventId));
        if (IsEnded || _eventFiredMs[index] != NotSet) return false;

        _eventFiredMs[index] = now.Milliseconds;
        _eventSequence[index] = ++_lastEventSequence;
        if (now > _now) _now = now;
        _events.Publish(new MissionEventRaised(now, eventId));
        return true;
    }

    public bool HasFired(string eventId) => _eventFiredMs[EventIndex(eventId)] != NotSet;

    public MissionTime? FiredAt(string eventId)
    {
        var ms = _eventFiredMs[EventIndex(eventId)];
        return ms == NotSet ? null : new MissionTime(ms);
    }

    public MissionRuleActivation? GetRuleActivation(string ruleId)
    {
        for (var r = 0; r < _rules.Length; r++)
        {
            if (string.Equals(_rules[r].Id, ruleId, StringComparison.Ordinal)) return _activations[r];
        }
        throw new ArgumentException($"Unknown mission rule '{ruleId}'.", nameof(ruleId));
    }

    public void Step(in StepContext context)
    {
        if (IsEnded) return;
        if (context.Now > _now) _now = context.Now;

        UpdateTimers((long)Math.Round(context.DeltaSeconds * 1000.0));
        RaiseConditionalEvents();
        ActivateRules();
        Feasibility.Evaluate(_now);
        CompleteObjectives();
        AdvancePhase();
        CheckEndConditions();
    }

    /// <summary>Summary for Scoring and the debrief; allocates, so call it on demand rather than per tick.</summary>
    public MissionOutcome GetOutcome()
    {
        var objectives = new ObjectiveOutcome[_objectives.Length];
        for (var i = 0; i < objectives.Length; i++) objectives[i] = Feasibility.GetOutcome(i);

        var rules = new List<MissionRuleActivation>();
        foreach (var activation in _activations)
        {
            if (activation is not null) rules.Add(activation);
        }

        var events = new List<MissionEventRecord>();
        for (var i = 0; i < _eventSpecs.Length; i++)
        {
            if (_eventFiredMs[i] != NotSet) events.Add(new MissionEventRecord(_eventSpecs[i].Id, new MissionTime(_eventFiredMs[i])));
        }
        events.Sort((a, b) => a.Time != b.Time ? a.Time.CompareTo(b.Time) : string.CompareOrdinal(a.EventId, b.EventId));

        return new MissionOutcome(_mission.Id, IsEnded, _outcome, EndedAt, objectives, Feasibility.History.ToArray(), rules, events);
    }

    public void WriteHash(StateHashWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(_now.Milliseconds);
        writer.Write(Phase);
        writer.Write(_phaseEnteredMs);
        writer.Write(_phaseEnteredSequence);
        writer.Write(_endedMs);
        writer.Write(_outcome is { } outcome ? (long)outcome : NotSet);
        writer.Write(_lastEventSequence);
        for (var i = 0; i < _eventFiredMs.Length; i++)
        {
            writer.Write(_eventFiredMs[i]);
            writer.Write(_eventSequence[i]);
        }
        for (var r = 0; r < _activations.Length; r++)
        {
            var activation = _activations[r];
            writer.Write(activation?.Time.Milliseconds ?? NotSet);
            writer.Write(activation is null ? NotSet : (long)activation.Cause);
        }
        for (var t = 0; t < _timers.Length; t++)
        {
            writer.Write(_timerElapsedMs[t]);
            writer.Write(_timerRunning[t]);
            writer.Write(_timerExpiredMs[t]);
        }
        Feasibility.WriteHash(writer);
    }

    internal long EventFiredMs(int index) => _eventFiredMs[index];

    internal MissionRuleActivation? RuleActivation(int index) => _activations[index];

    internal bool EvaluateCondition(int index) => _mission.Conditions[index].When.IsTrue(_state);

    internal long TimerElapsedMs(int index) => _timerElapsedMs[index];

    internal bool TimerRunning(int index) => _timerRunning[index];

    internal long TimerRemainingMs(int index) => Math.Max(0, (_timers[index].DurationMs ?? 0) - _timerElapsedMs[index]);

    internal bool TimerExpired(int index) => _timerExpiredMs[index] != NotSet;

    /// <summary>Elapsed running time of a timer; throws for an unknown id.</summary>
    public double TimerSeconds(string timerId) => _timerElapsedMs[TimerIndex(timerId)] / 1000.0;

    /// <summary>MET at which a deadline expired, or null.</summary>
    public MissionTime? TimerExpiredAt(string timerId)
    {
        var ms = _timerExpiredMs[TimerIndex(timerId)];
        return ms == NotSet ? null : new MissionTime(ms);
    }

    /// <summary>
    /// Resets, then accumulates <paramref name="deltaMs"/> into every running timer, and expires deadlines that ran out. A timer
    /// runs once its start event fired, before its stop event, while <c>runWhile</c> holds; a reset also re-arms a deadline.
    /// Expire events are raised after every timer has stepped, so a timer started by another's expiry begins on the next step
    /// whatever the declaration order.
    /// </summary>
    private void UpdateTimers(long deltaMs)
    {
        var expiredAny = false;
        for (var t = 0; t < _timers.Length; t++)
        {
            var timer = _timers[t];
            var reset = timer.ResetWhen is { } resetWhen && resetWhen.IsTrue(_state);
            if (reset)
            {
                _timerElapsedMs[t] = 0;
                _timerExpiredMs[t] = NotSet;
            }
            var running = !reset
                          && (timer.StartEvent is not { } start || HasFired(start))
                          && (timer.StopEvent is not { } stop || !HasFired(stop))
                          && (timer.RunWhile is not { } runWhile || runWhile.IsTrue(_state));
            _timerRunning[t] = running;
            if (running) _timerElapsedMs[t] += deltaMs;

            if (timer.DurationMs is not { } durationMs || _timerExpiredMs[t] != NotSet || _timerElapsedMs[t] < durationMs) continue;
            _timerExpiredMs[t] = _now.Milliseconds;
            expiredAny |= timer.ExpireEvent is not null;
        }
        if (!expiredAny) return;
        for (var t = 0; t < _timers.Length; t++)
        {
            if (_timerExpiredMs[t] == _now.Milliseconds && _timers[t].ExpireEvent is { } expire && !HasFired(expire)) RaiseEvent(expire, _now);
        }
    }

    private int TimerIndex(string timerId)
    {
        for (var t = 0; t < _timers.Length; t++)
        {
            if (string.Equals(_timers[t].Id, timerId, StringComparison.Ordinal)) return t;
        }
        throw new ArgumentException($"Unknown mission timer '{timerId}'.", nameof(timerId));
    }

    private void RaiseConditionalEvents()
    {
        for (var i = 0; i < _eventSpecs.Length; i++)
        {
            if (_eventFiredMs[i] == NotSet && _eventSpecs[i].When is { } when && when.IsTrue(_state))
                RaiseEvent(_eventSpecs[i].Id, _now);
        }
    }

    private void ActivateRules()
    {
        for (var r = 0; r < _rules.Length; r++)
        {
            if (_activations[r] is not null) continue;
            var rule = _rules[r];
            if (!rule.When.IsTrue(_state)) continue;

            var cause = rule.Cause.Decide(_state);
            _activations[r] = new MissionRuleActivation(rule.Id, _now, cause);
            _events.Publish(new MissionRuleActivated(_now, rule.Id, cause, rule.RequireReturn));
            Feasibility.ApplyRule(rule, cause, _now);
        }
    }

    private void CompleteObjectives()
    {
        for (var i = 0; i < _objectives.Length; i++)
        {
            if (Feasibility.StateAt(i) != ObjectiveState.Achievable || !_objectives[i].Complete.IsTrue(_state)) continue;
            Feasibility.Complete(i, _now);
            _events.Publish(new ObjectiveCompleted(_now, _objectives[i].Id));
        }
    }

    private void AdvancePhase()
    {
        // Each pass takes at most one transition; the bound stops badly authored cycles.
        for (var pass = 0; pass < _transitions.Length; pass++)
        {
            var moved = false;
            for (var t = 0; t < _transitions.Length; t++)
            {
                if (!_transitionFrom[t][_phase]) continue;
                var eventIndex = _transitionEvent[t];
                if (eventIndex >= 0)
                {
                    if (_eventSequence[eventIndex] <= _phaseEnteredSequence) continue;
                    ChangePhase(_transitionTo[t], _eventSequence[eventIndex]);
                }
                else
                {
                    if (!_transitions[t].Trigger.When!.IsTrue(_state)) continue;
                    ChangePhase(_transitionTo[t], _lastEventSequence);
                }
                moved = true;
                break;
            }
            if (!moved) return;
        }
    }

    private void CheckEndConditions()
    {
        for (var e = 0; e < _endConditions.Length; e++)
        {
            var eventIndex = _endEvent[e];
            var met = eventIndex >= 0
                ? _eventFiredMs[eventIndex] != NotSet
                : _endConditions[e].Trigger.When!.IsTrue(_state);
            if (!met) continue;

            var condition = _endConditions[e];
            ChangePhase(_endedPhase, _lastEventSequence);
            _outcome = condition.Outcome;
            _endedMs = _now.Milliseconds;
            _events.Publish(new MissionEnded(_now, condition.Outcome, condition.Id));
            return;
        }
    }

    /// <param name="enteredSequence">Events up to this raise sequence number cannot trigger transitions out of the new phase.</param>
    private void ChangePhase(int to, long enteredSequence)
    {
        if (to == _phase) return;
        var from = _phases[_phase];
        _phase = to;
        _phaseEnteredMs = _now.Milliseconds;
        _phaseEnteredSequence = enteredSequence;
        _events.Publish(new PhaseChanged(_now, from, _phases[to]));
    }

    private int EventIndexOrNone(MissionTrigger trigger) => trigger.EventId is { } id ? EventIndex(id) : -1;

    private int EventIndex(string eventId) =>
        _eventIndex.TryGetValue(eventId, out var index) ? index : throw new ArgumentException($"Unknown mission event '{eventId}'.", nameof(eventId));

    private int PhaseIndex(string phase)
    {
        var index = Array.IndexOf(_phases, phase);
        return index >= 0 ? index : throw new ArgumentException($"Unknown phase '{phase}'.", nameof(phase));
    }
}
