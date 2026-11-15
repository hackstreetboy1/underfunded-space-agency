using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Alarms;

/// <summary>
/// Active alarms in priority order (UI.md 3.10): pinned unacknowledged warning and master alarms first, then by
/// priority, unacknowledged before acknowledged, newest first. Acknowledging applies locally at once (so the master
/// alarm stops flashing on the click) and is sent as an <see cref="AcknowledgeAlarmsRequest"/>; a local
/// acknowledgement lasts until the alarm clears, changes level or is raised again.
/// </summary>
public sealed class AlarmListViewModel : ViewModelBase
{
    /// <summary>Unacknowledged alarms at or above this priority are pinned.</summary>
    public const AlarmPriority PinThreshold = AlarmPriority.Warning;

    private static readonly Comparison<AlarmRow> Order = CompareRows;

    private readonly StatusLanguage _status;
    private readonly ObservableList<AlarmRow> _rows = new();
    private readonly ObservableList<AlarmRow> _pinned = new();
    private readonly ObservableList<AlarmRow>[] _bySystem;
    private readonly ObservableValue<AlarmSummary> _summary = new(AlarmSummary.Empty);
    private readonly Signal<AcknowledgeAlarmsRequest> _acknowledgeRequested = new();
    private readonly HashSet<AcknowledgementKey> _acknowledgedLocally = [];
    private readonly List<AlarmRow> _sorted = [];
    private readonly List<AlarmRow> _subset = [];
    private readonly ConsoleWords? _words;
    private readonly ObservableValue<string> _ackWord = new(string.Empty);
    private readonly ObservableValue<string> _ackTooltip = new(string.Empty);
    private IReadOnlyList<AlarmSnapshot> _current = [];

    /// <param name="words">The ACK words; without them <see cref="AckWord"/> stays empty.</param>
    /// <param name="acknowledgesSingly">
    /// The session can acknowledge one alarm. Today <c>acknowledge_alarms</c> takes no arguments and acknowledges them all, so a
    /// row's ACK acknowledges every alarm and says so (<c>ACK ALL</c>) whenever more than one is unacknowledged.
    /// </param>
    public AlarmListViewModel(StatusLanguage status, ConsoleWords? words = null, bool acknowledgesSingly = false)
    {
        ArgumentNullException.ThrowIfNull(status);
        _status = status;
        _words = words;
        AcknowledgesSingly = acknowledgesSingly;
        var systems = Enum.GetValues<SystemsTab>();
        _bySystem = new ObservableList<AlarmRow>[systems.Length];
        for (var i = 0; i < _bySystem.Length; i++) _bySystem[i] = new ObservableList<AlarmRow>();
        RefreshAckWords();
    }

    /// <summary>All active alarms, ordered.</summary>
    public ObservableList<AlarmRow> Rows => _rows;

    /// <summary>The pinned head of <see cref="Rows"/>, also shown above the event log.</summary>
    public ObservableList<AlarmRow> Pinned => _pinned;

    public IReadOnlyObservable<AlarmSummary> Summary => _summary;

    public Signal<AcknowledgeAlarmsRequest> AcknowledgeRequested => _acknowledgeRequested;

    /// <summary>A row's ACK acknowledges only that alarm.</summary>
    public bool AcknowledgesSingly { get; }

    /// <summary>The word on a row's ACK button: <c>ACK</c>, or <c>ACK ALL</c> when it would acknowledge several alarms.</summary>
    public IReadOnlyObservable<string> AckWord => _ackWord;

    /// <summary>Tooltip of a row's ACK button, saying what it acknowledges.</summary>
    public IReadOnlyObservable<string> AckTooltip => _ackTooltip;

    /// <summary>The alarms of one systems-board tab, in the same order.</summary>
    public ObservableList<AlarmRow> RowsFor(SystemsTab system) => _bySystem[(int)system];

    public void Apply(IReadOnlyList<AlarmSnapshot> alarms)
    {
        ArgumentNullException.ThrowIfNull(alarms);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _current = alarms;
        _acknowledgedLocally.RemoveWhere(key => !IsActive(key));
        Rebuild();
    }

    /// <summary>Acknowledges every active alarm.</summary>
    /// <returns>Alarms that were unacknowledged; zero sends nothing.</returns>
    public int AcknowledgeAll()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        var count = 0;
        foreach (var alarm in _current)
        {
            if (IsAcknowledged(alarm)) continue;
            _acknowledgedLocally.Add(AcknowledgementKey.Of(alarm));
            count++;
        }
        if (count == 0) return 0;

        Rebuild();
        _acknowledgeRequested.Raise(new AcknowledgeAlarmsRequest(count));
        return count;
    }

    /// <summary>A row's ACK: that alarm when the session acknowledges singly, otherwise every active alarm.</summary>
    /// <returns>Alarms acknowledged; zero sends nothing.</returns>
    public int Acknowledge(string alarmId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alarmId);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (!AcknowledgesSingly) return AcknowledgeAll();
        foreach (var alarm in _current)
        {
            if (!string.Equals(alarm.AlarmId, alarmId, StringComparison.Ordinal) || IsAcknowledged(alarm)) continue;
            _acknowledgedLocally.Add(AcknowledgementKey.Of(alarm));
            Rebuild();
            _acknowledgeRequested.Raise(new AcknowledgeAlarmsRequest(1) { AlarmId = alarmId });
            return 1;
        }
        return 0;
    }

    private void Rebuild()
    {
        _sorted.Clear();
        foreach (var alarm in _current) _sorted.Add(ToRow(alarm));
        _sorted.Sort(Order);
        KeyedSync.SyncRows(_rows, _sorted, static row => row.AlarmId);

        SyncSubset(_pinned, static row => row.Pinned);
        var perSystem = AlarmSummary.EmptyPerSystem();
        for (var i = 0; i < _bySystem.Length; i++)
        {
            var system = perSystem[i].System;
            _subset.Clear();
            var unacknowledged = 0;
            AlarmPriority? highest = null;
            foreach (var row in _sorted)
            {
                if (row.System != system) continue;
                _subset.Add(row);
                if (!row.Acknowledged) unacknowledged++;
                if (highest is not { } h || row.Priority > h) highest = row.Priority;
            }
            KeyedSync.SyncRows(_bySystem[i], _subset, static row => row.AlarmId);
            perSystem[i] = new SystemAlarmCount(system, _subset.Count, unacknowledged, highest);
        }

        _summary.Value = Summarize(perSystem);
        RefreshAckWords();
    }

    private void RefreshAckWords()
    {
        if (_words is null) return;
        var all = !AcknowledgesSingly && _summary.Value.Unacknowledged > 1;
        _ackWord.Value = all ? _words.AlarmAckAll : _words.AlarmAck;
        _ackTooltip.Value = all ? _words.AlarmAckAllTooltip : _words.AlarmAckTooltip;
    }

    private AlarmSummary Summarize(SystemAlarmCount[] perSystem)
    {
        var unacknowledged = 0;
        var cautions = 0;
        AlarmPriority? highestUnacknowledged = null;
        foreach (var row in _sorted)
        {
            if (row.Priority == AlarmPriority.Caution) cautions++;
            if (row.Acknowledged) continue;
            unacknowledged++;
            if (highestUnacknowledged is not { } h || row.Priority > h) highestUnacknowledged = row.Priority;
        }
        return new AlarmSummary(_sorted.Count, unacknowledged, cautions, highestUnacknowledged, perSystem);
    }

    private void SyncSubset(ObservableList<AlarmRow> target, Func<AlarmRow, bool> include)
    {
        _subset.Clear();
        foreach (var row in _sorted)
        {
            if (include(row)) _subset.Add(row);
        }
        KeyedSync.SyncRows(target, _subset, static row => row.AlarmId);
    }

    private AlarmRow ToRow(AlarmSnapshot alarm)
    {
        var acknowledged = IsAcknowledged(alarm);
        return new AlarmRow(
            alarm.AlarmId,
            alarm.Priority,
            alarm.System,
            alarm.Text,
            alarm.Source,
            alarm.RaisedAt,
            ReadoutFormatter.FormatMet(alarm.RaisedAt),
            acknowledged,
            Pinned: !acknowledged && alarm.Priority >= PinThreshold,
            _status.For(alarm.Priority));
    }

    private bool IsAcknowledged(AlarmSnapshot alarm) =>
        alarm.Acknowledged || _acknowledgedLocally.Contains(AcknowledgementKey.Of(alarm));

    private bool IsActive(AcknowledgementKey key)
    {
        foreach (var alarm in _current)
        {
            if (AcknowledgementKey.Of(alarm) == key) return true;
        }
        return false;
    }

    private static int CompareRows(AlarmRow a, AlarmRow b)
    {
        var order = b.Pinned.CompareTo(a.Pinned);
        if (order != 0) return order;
        order = b.Priority.CompareTo(a.Priority);
        if (order != 0) return order;
        order = a.Acknowledged.CompareTo(b.Acknowledged);
        if (order != 0) return order;
        order = b.RaisedAt.CompareTo(a.RaisedAt);
        return order != 0 ? order : string.CompareOrdinal(a.AlarmId, b.AlarmId);
    }

    /// <summary>One raising of an alarm: a new level or raise time needs a new acknowledgement.</summary>
    private readonly record struct AcknowledgementKey(string AlarmId, AlarmPriority Priority, MissionTime RaisedAt)
    {
        public static AcknowledgementKey Of(AlarmSnapshot alarm) => new(alarm.AlarmId, alarm.Priority, alarm.RaisedAt);
    }
}
