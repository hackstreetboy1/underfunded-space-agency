namespace Usa.Game.Presentation.Audio.Mixing;

/// <summary>
/// Turns alarm state into sound actions (UI.md 3.10): advisory = tick; caution = chime once;
/// warning = pulse burst repeated until acknowledged; master = continuous until acknowledged, then
/// an attenuated reminder while the condition persists. Lower alarms stay silent while the master
/// sounds so tones never stack into noise.
/// </summary>
public sealed class AlarmSequencer
{
    private readonly AlarmCadence _cadence;
    private readonly Dictionary<string, Entry> _alarms = new(StringComparer.Ordinal);
    private bool _masterLooping;
    private bool _advisoryPending;
    private bool _cautionPending;
    private double _warningCountdown;
    private double _reminderCountdown;

    public AlarmSequencer(AlarmCadence cadence)
    {
        ArgumentNullException.ThrowIfNull(cadence);
        _cadence = cadence;
        _reminderCountdown = cadence.ReminderIntervalSeconds;
    }

    /// <summary>
    /// Raises an alarm or changes its priority. The host forwards every simulation <c>AlarmRaised</c>, de-escalations
    /// included: re-raising at the same priority keeps the acknowledgement, while a different priority (Master to
    /// Warning, Caution to Warning, ...) replaces the entry unacknowledged, so the new level sounds and a master loop
    /// that no longer applies stops.
    /// </summary>
    public void Raise(string alarmId, AlarmPriority priority)
    {
        ArgumentException.ThrowIfNullOrEmpty(alarmId);
        if (priority == AlarmPriority.Advisory)
        {
            _advisoryPending = true;
            return;
        }

        if (_alarms.TryGetValue(alarmId, out var existing) && existing.Priority == priority) return;
        _alarms[alarmId] = new Entry(priority, Acknowledged: false);
        if (priority == AlarmPriority.Caution) _cautionPending = true;
        if (priority == AlarmPriority.Warning) _warningCountdown = 0.0;
    }

    public void Clear(string alarmId) => _alarms.Remove(alarmId);

    /// <summary>
    /// Forgets every alarm and cadence (quit to menu, new mission). Emits nothing: the caller has already silenced the
    /// voices, so the next master alarm starts its loop again.
    /// </summary>
    public void Reset()
    {
        _alarms.Clear();
        _masterLooping = false;
        _advisoryPending = false;
        _cautionPending = false;
        _warningCountdown = 0.0;
        _reminderCountdown = _cadence.ReminderIntervalSeconds;
    }

    /// <summary>Acknowledges every active alarm. Acknowledging never fixes anything.</summary>
    public void AcknowledgeAll()
    {
        foreach (var id in _alarms.Keys.ToArray())
        {
            _alarms[id] = _alarms[id] with { Acknowledged = true };
        }
    }

    public void Advance(double deltaSeconds, ICollection<AlarmAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        var unackedMaster = false;
        var unackedWarning = false;
        var ackedMaster = false;
        foreach (var entry in _alarms.Values)
        {
            unackedMaster |= entry is { Priority: AlarmPriority.Master, Acknowledged: false };
            unackedWarning |= entry is { Priority: AlarmPriority.Warning, Acknowledged: false };
            ackedMaster |= entry is { Priority: AlarmPriority.Master, Acknowledged: true };
        }

        if (unackedMaster != _masterLooping)
        {
            _masterLooping = unackedMaster;
            actions.Add(new AlarmAction(unackedMaster ? AlarmActionKind.StartLoop : AlarmActionKind.StopLoop, AudioCue.AlarmMaster));
        }

        if (_advisoryPending && !_masterLooping) actions.Add(new AlarmAction(AlarmActionKind.PlayOnce, AudioCue.AlarmAdvisory));
        if (_cautionPending && !_masterLooping) actions.Add(new AlarmAction(AlarmActionKind.PlayOnce, AudioCue.AlarmCaution));
        _advisoryPending = _cautionPending = false;

        if (unackedWarning && !_masterLooping)
        {
            _warningCountdown -= deltaSeconds;
            if (_warningCountdown <= 0.0)
            {
                actions.Add(new AlarmAction(AlarmActionKind.PlayOnce, AudioCue.AlarmWarning));
                _warningCountdown = _cadence.WarningRepeatSeconds;
            }
        }
        else
        {
            // A warning unmasked by a cleared master sounds at once.
            _warningCountdown = 0.0;
        }

        if (ackedMaster && !_masterLooping)
        {
            _reminderCountdown -= deltaSeconds;
            if (_reminderCountdown <= 0.0)
            {
                actions.Add(new AlarmAction(AlarmActionKind.PlayOnce, AudioCue.AlarmReminder));
                _reminderCountdown = _cadence.ReminderIntervalSeconds;
            }
        }
        else
        {
            _reminderCountdown = _cadence.ReminderIntervalSeconds;
        }
    }

    private readonly record struct Entry(AlarmPriority Priority, bool Acknowledged);
}
