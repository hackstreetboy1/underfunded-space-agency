using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Guidance;

/// <summary>
/// Guided first run (GDD 9.4, D19). Shows one callout at a time; beats that trigger while another is up
/// wait in a queue in trigger order (authored order breaks ties). Beats already satisfied when their turn
/// comes are skipped silently. A callout stays until the player closes it or its step is done (or moot); mission data sets no
/// display limits. It can be switched off at any time and never pauses the simulation; a callout waiting for the player may
/// cap time compression (<see cref="ActiveMaxTimeRate"/>).
/// </summary>
public sealed class TutorialDirector : ISimSystem, IHashContributor
{
    private readonly TutorialScript _script;
    private readonly IStateQuery _state;
    private readonly EventBus _events;
    private readonly TutorialBeatStatus[] _status;
    private readonly List<int> _queue;
    private readonly long _minDisplayMs;
    private int _active = -1;
    private long _activeSinceMs;

    public TutorialDirector(TutorialScript script, IStateQuery state, EventBus events, bool enabled)
    {
        _script = script ?? throw new ArgumentNullException(nameof(script));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _status = new TutorialBeatStatus[script.Beats.Count];
        _queue = new List<int>(script.Beats.Count);
        _minDisplayMs = (long)Math.Round(script.MinDisplayS * 1000.0);
        IsEnabled = enabled;
    }

    public int PeriodTicks => SimulationClock.TicksPerSecond;

    public bool IsEnabled { get; private set; }

    public bool IsFinished { get; private set; }

    /// <summary>Id of the callout on screen, or null.</summary>
    public string? ActiveBeatId => _active >= 0 ? _script.Beats[_active].Id : null;

    /// <summary>
    /// The time-compression cap of the callout on screen (<c>maxTimeRate</c>), so time does not race past a step the player is
    /// being walked through; null when no callout asks for one.
    /// </summary>
    public double? ActiveMaxTimeRate => IsEnabled && _active >= 0 ? _script.Beats[_active].MaxTimeRate : null;

    /// <summary>When the callout on screen appeared; null when none is up.</summary>
    public MissionTime? ActiveSince => _active >= 0 ? new MissionTime(_activeSinceMs) : null;

    public TutorialBeatStatus StatusOf(string beatId) => _status[IndexOf(beatId)];

    public void Step(in StepContext context)
    {
        if (!IsEnabled || IsFinished) return;

        if (_script.EndWhen is { } endWhen && GuidanceConditions.Holds(endWhen, _state))
        {
            Finish(context.Now);
            return;
        }

        var beats = _script.Beats;
        for (var i = 0; i < beats.Count; i++)
        {
            if (_status[i] != TutorialBeatStatus.Waiting) continue;
            var after = beats[i].AfterIndex;
            if (after >= 0 && _status[after] != TutorialBeatStatus.Done) continue;
            if (!GuidanceConditions.Holds(beats[i].Trigger, _state)) continue;
            _status[i] = TutorialBeatStatus.Queued;
            _queue.Add(i);
        }

        if (_active >= 0) CheckActive(context.Now);
        if (_active < 0) ShowNext(context.Now);
    }

    /// <summary>The player closed the callout; the beat counts as done.</summary>
    public bool Dismiss(MissionTime now)
    {
        if (_active < 0) return false;
        Close(now, TutorialCloseReason.Dismissed);
        return true;
    }

    /// <summary>
    /// "Got it" on <paramref name="beatId"/>: closes it only while it is still the callout on screen, so a click that crossed the
    /// beat's own close (completed or timed out in the same frame) never dismisses the next one.
    /// </summary>
    public bool Dismiss(string beatId, MissionTime now) =>
        _active >= 0 && string.Equals(_script.Beats[_active].Id, beatId, StringComparison.Ordinal) && Dismiss(now);

    /// <summary>Turning the tutorial off closes the callout on screen; turning it on resumes the remaining beats.</summary>
    public void SetEnabled(bool enabled, MissionTime now)
    {
        if (!enabled && _active >= 0) Close(now, TutorialCloseReason.Disabled);
        IsEnabled = enabled;
    }

    public void WriteHash(StateHashWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(IsEnabled);
        writer.Write(IsFinished);
        foreach (var status in _status) writer.Write((long)status);
        writer.Write(_queue.Count);
        foreach (var index in _queue) writer.Write(index);
        writer.Write(_active);
        writer.Write(_activeSinceMs);
    }

    private void CheckActive(MissionTime now)
    {
        var beat = _script.Beats[_active];
        var shownMs = now.Milliseconds - _activeSinceMs;
        if (beat.Completion is { } completion && shownMs >= _minDisplayMs && GuidanceConditions.Holds(completion, _state))
            Close(now, TutorialCloseReason.Completed);
        else if (beat.MaxDisplayS is { } maxS && shownMs >= (long)Math.Round(maxS * 1000.0))
            Close(now, TutorialCloseReason.TimedOut);
    }

    private void ShowNext(MissionTime now)
    {
        while (_queue.Count > 0)
        {
            var index = _queue[0];
            _queue.RemoveAt(0);
            var beat = _script.Beats[index];
            if (beat.Completion is { } completion && GuidanceConditions.Holds(completion, _state))
            {
                _status[index] = TutorialBeatStatus.Done;
                continue;
            }

            _status[index] = TutorialBeatStatus.Shown;
            _active = index;
            _activeSinceMs = now.Milliseconds;
            _events.Publish(new TutorialCalloutShown(now, beat.Id, beat.Anchor, beat.ResolveText(_state), beat.Title, beat.ResolveOrder(_state)));
            return;
        }
    }

    private void Close(MissionTime now, TutorialCloseReason reason)
    {
        var index = _active;
        _active = -1;
        _status[index] = TutorialBeatStatus.Done;
        _events.Publish(new TutorialCalloutClosed(now, _script.Beats[index].Id, reason));
    }

    private void Finish(MissionTime now)
    {
        if (_active >= 0) Close(now, TutorialCloseReason.Ended);
        _queue.Clear();
        for (var i = 0; i < _status.Length; i++) _status[i] = TutorialBeatStatus.Done;
        IsFinished = true;
        _events.Publish(new TutorialFinished(now));
    }

    private int IndexOf(string beatId)
    {
        for (var i = 0; i < _script.Beats.Count; i++)
        {
            if (string.Equals(_script.Beats[i].Id, beatId, StringComparison.Ordinal)) return i;
        }
        throw new ArgumentException($"Unknown tutorial beat '{beatId}'.", nameof(beatId));
    }
}
