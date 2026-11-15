using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;
using Usa.Simulation.Scenario.Model;

namespace Usa.Simulation.Scenario;

/// <summary>
/// Fires authored incidents (SCRIPTING 11.4). Every seeded window is drawn once at construction from the
/// <c>incidents</c> stream, in incident id order and trigger order, so the timing depends only on the seed and
/// the moment the trigger's event fired, never on what else happened. Mission event times are read through
/// <c>mission.event.&lt;id&gt;.fired/met_s</c>; effects go through <see cref="EffectRouter"/>.
/// </summary>
public sealed class FailureDirector : ISimSystem, IHashContributor
{
    public const string RngStream = "incidents";

    private const long NotSet = -1;

    private readonly Incident[] _incidents;
    private readonly EffectDefinition[][] _effects;
    private readonly bool[] _enabled;
    private readonly long[] _firedMs;
    private readonly int[] _firstLeaf;
    private readonly IncidentTrigger[] _leaves;
    private readonly long[] _drawnMs;
    private readonly long[] _conditionSinceMs;
    private readonly string?[] _eventFiredPath;
    private readonly string?[] _eventMetPath;
    private readonly Dictionary<IncidentId, int> _index = [];
    private readonly IStateQuery _state;
    private readonly EffectRouter _router;
    private readonly EventBus _events;
    private readonly IMissionEventSink _missionEvents;

    public FailureDirector(
        IncidentSet incidents,
        VariantPicker variants,
        ulong scenarioSeed,
        IStateQuery state,
        EffectRouter router,
        EventBus events,
        IMissionEventSink missionEvents)
    {
        ArgumentNullException.ThrowIfNull(incidents);
        ArgumentNullException.ThrowIfNull(variants);
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _missionEvents = missionEvents ?? throw new ArgumentNullException(nameof(missionEvents));

        _incidents = incidents.Incidents.ToArray();
        Array.Sort(_incidents, (a, b) => a.Id.CompareTo(b.Id));
        _effects = new EffectDefinition[_incidents.Length][];
        _enabled = new bool[_incidents.Length];
        _firedMs = new long[_incidents.Length];
        _firstLeaf = new int[_incidents.Length + 1];

        var leaves = new List<IncidentTrigger>();
        for (var i = 0; i < _incidents.Length; i++)
        {
            var incident = _incidents[i];
            _index.Add(incident.Id, i);
            _firedMs[i] = NotSet;
            _enabled[i] = incident.RequiresVariant is not { } guard || variants.IsPicked(guard.VariantId, guard.Option);
            _effects[i] = incident.Effects
                .Select(effect => effect with { Target = EffectTargets.Resolve(effect.Target, variants) })
                .ToArray();
            _firstLeaf[i] = leaves.Count;
            leaves.AddRange(incident.Triggers);
        }
        _firstLeaf[_incidents.Length] = leaves.Count;

        _leaves = leaves.ToArray();
        _drawnMs = new long[_leaves.Length];
        _conditionSinceMs = new long[_leaves.Length];
        _eventFiredPath = new string?[_leaves.Length];
        _eventMetPath = new string?[_leaves.Length];
        var rng = Pcg32.ForStream(scenarioSeed, RngStream);
        for (var l = 0; l < _leaves.Length; l++)
        {
            var leaf = _leaves[l];
            _conditionSinceMs[l] = NotSet;
            _drawnMs[l] = leaf.Window is { } window ? MissionTime.FromSeconds(rng.Range(window.Min, window.Max)).Milliseconds : 0;
            if (leaf.EventId is { } eventId)
            {
                _eventFiredPath[l] = $"{StatePaths.Mission}event.{eventId}.fired";
                _eventMetPath[l] = $"{StatePaths.Mission}event.{eventId}.met_s";
            }
        }
    }

    public int PeriodTicks => SimulationClock.TicksPerSecond;

    public bool IsEnabled(IncidentId id) => _enabled[IndexOf(id)];

    public bool HasFired(IncidentId id) => _firedMs[IndexOf(id)] != NotSet;

    public MissionTime? FiredAt(IncidentId id)
    {
        var ms = _firedMs[IndexOf(id)];
        return ms == NotSet ? null : new MissionTime(ms);
    }

    /// <summary>Effect targets of enabled incidents that no registered handler accepts; empty when the wiring is complete.</summary>
    public IReadOnlyList<string> FindUnroutableEffects()
    {
        var unroutable = new List<string>();
        for (var i = 0; i < _incidents.Length; i++)
        {
            if (!_enabled[i]) continue;
            foreach (var effect in _effects[i])
            {
                if (!_router.CanApply(effect)) unroutable.Add($"{_incidents[i].Id}: {effect.Target} ({effect.Operation})");
            }
        }
        return unroutable;
    }

    public void Step(in StepContext context)
    {
        var nowMs = context.Now.Milliseconds;
        for (var i = 0; i < _incidents.Length; i++)
        {
            if (!_enabled[i] || _firedMs[i] != NotSet) continue;
            var due = false;
            for (var l = _firstLeaf[i]; l < _firstLeaf[i + 1]; l++)
            {
                if (IsLeafDue(l, nowMs)) due = true;
            }
            if (due) Fire(i, context.Now);
        }
    }

    public void WriteHash(StateHashWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        for (var i = 0; i < _incidents.Length; i++)
        {
            writer.Write(_incidents[i].Id.Value);
            writer.Write(_enabled[i]);
            writer.Write(_firedMs[i]);
        }
        for (var l = 0; l < _leaves.Length; l++)
        {
            writer.Write(_drawnMs[l]);
            writer.Write(_conditionSinceMs[l]);
        }
    }

    // Every leaf is evaluated (no early exit) so condition hold timers stay current across alternatives.
    private bool IsLeafDue(int leaf, long nowMs)
    {
        var trigger = _leaves[leaf];
        switch (trigger.Type)
        {
            case IncidentTriggerType.AtTime:
                return nowMs >= _drawnMs[leaf];

            case IncidentTriggerType.AfterEvent:
                if (!_state.GetBool(_eventFiredPath[leaf]!)) return false;
                var eventMs = MissionTime.FromSeconds(_state.GetNumber(_eventMetPath[leaf]!)).Milliseconds;
                return nowMs >= eventMs + _drawnMs[leaf];

            case IncidentTriggerType.Condition:
                if (!trigger.When!.IsTrue(_state))
                {
                    _conditionSinceMs[leaf] = NotSet;
                    return false;
                }
                if (_conditionSinceMs[leaf] == NotSet) _conditionSinceMs[leaf] = nowMs;
                return nowMs - _conditionSinceMs[leaf] >= MissionTime.FromSeconds(trigger.HoldS).Milliseconds;

            default:
                throw new InvalidOperationException($"Trigger type {trigger.Type} cannot be evaluated directly.");
        }
    }

    private void Fire(int index, MissionTime now)
    {
        _firedMs[index] = now.Milliseconds;
        var effects = _effects[index];
        _router.ApplyAll(effects, now);
        var incident = _incidents[index];
        _events.Publish(new IncidentTriggered(now, incident.Id));
        if (incident.RaisesEvent is { } eventId) _missionEvents.RaiseEvent(eventId, now);
    }

    private int IndexOf(IncidentId id) =>
        _index.TryGetValue(id, out var index) ? index : throw new ArgumentException($"Unknown incident '{id}'.", nameof(id));
}
