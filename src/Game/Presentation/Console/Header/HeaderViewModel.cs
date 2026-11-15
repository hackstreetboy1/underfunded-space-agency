using Usa.Game.Presentation.Console.Alarms;
using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Header;

/// <summary>
/// The console header (UI.md 3.1): phase chip, MET clock, time compression with its reason and the Advance
/// button, objectives summary, link indicator and the master alarm, which follows <see cref="AlarmListViewModel"/>.
/// MET and compression change every frame at high rates, so they are throttled to 10 Hz when a throttle is given.
/// </summary>
public sealed class HeaderViewModel : ViewModelBase
{
    private readonly AlarmListViewModel _alarms;
    private readonly ConsoleWords _words;
    private readonly ObservableValue<string> _missionTitle = new(string.Empty);
    private readonly ObservableValue<string> _vehicleName = new(string.Empty);
    private readonly ObservableValue<PhaseChip> _phase;
    private readonly ObservableValue<string> _metText;
    private readonly ObservableValue<CompressionIndicator> _compression;
    private readonly ObservableList<ObjectiveChip> _objectives = new();
    private readonly ObservableValue<string> _objectivesSummary = new(string.Empty);
    private readonly StatusLanguage _status;
    private readonly ObservableValue<StatusVisual> _link;
    private readonly ObservableValue<MasterAlarmIndicator> _masterAlarm = new(MasterAlarmIndicator.Off);
    private readonly Signal<AdvanceToNextEventRequest> _advanceRequested = new();
    private readonly List<ObjectiveChip> _objectiveScratch = [];
    private CompressionSnapshot _lastCompression = CompressionSnapshot.RealTime;
    private MissionTime _met;

    public HeaderViewModel(AlarmListViewModel alarms, StatusLanguage status, ConsoleWords words, UpdateThrottle? throttle = null)
    {
        ArgumentNullException.ThrowIfNull(alarms);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(words);
        _status = status;
        _words = words;
        _phase = new(PhaseChip.For(MissionPhase.DockedOps, words));
        _link = new(status.For(LinkState.Ok));
        _alarms = alarms;
        _metText = ConsoleObservables.Throttled(FormatMetClock(MissionTime.Zero), throttle);
        _compression = ConsoleObservables.Throttled(CompressionIndicator.From(CompressionSnapshot.RealTime, words), throttle);
        Observe(alarms.Summary, summary => _masterAlarm.Value = MasterAlarmIndicator.From(summary));
    }

    /// <summary><c>USA</c>.</summary>
    public string AgencyMark => _words.AgencyMark;

    /// <summary>Caption of the master alarm annunciator.</summary>
    public string MasterAlarmLabel => _words.MasterAlarm;

    public IReadOnlyObservable<string> MissionTitle => _missionTitle;

    public IReadOnlyObservable<string> VehicleName => _vehicleName;

    public IReadOnlyObservable<PhaseChip> Phase => _phase;

    /// <summary><c>MET 06:58:12</c>.</summary>
    public IReadOnlyObservable<string> MetText => _metText;

    public IReadOnlyObservable<CompressionIndicator> Compression => _compression;

    public ObservableList<ObjectiveChip> Objectives => _objectives;

    /// <summary><c>1/3 done</c>; empty without objectives.</summary>
    public IReadOnlyObservable<string> ObjectivesSummary => _objectivesSummary;

    /// <summary><c>LINK OK</c> / <c>RECONNECTING</c>.</summary>
    public IReadOnlyObservable<StatusVisual> Link => _link;

    public IReadOnlyObservable<MasterAlarmIndicator> MasterAlarm => _masterAlarm;

    public Signal<AdvanceToNextEventRequest> AdvanceRequested => _advanceRequested;

    public void Apply(ConsoleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _met = snapshot.Met;
        _lastCompression = snapshot.Compression;
        _missionTitle.Value = snapshot.MissionTitle;
        _vehicleName.Value = snapshot.VehicleName;
        if (_phase.Value.Phase != snapshot.Phase) _phase.Value = PhaseChip.For(snapshot.Phase, _words);
        _metText.Value = FormatMetClock(snapshot.Met);
        _compression.Value = CompressionIndicator.From(snapshot.Compression, _words);
        _link.Value = _status.For(snapshot.Link);
        ApplyObjectives(snapshot.Objectives);
    }

    /// <summary>Raises <see cref="AdvanceRequested"/> when the vehicle is stable and no advance is running.</summary>
    public bool RequestAdvanceToNextEvent()
    {
        if (!_lastCompression.CanAdvanceToNextEvent || _lastCompression.IsAdvancingToEvent) return false;
        _advanceRequested.Raise(new AdvanceToNextEventRequest(_met));
        return true;
    }

    /// <summary>Master alarm click / <c>Ctrl+A</c>.</summary>
    /// <returns>Alarms acknowledged.</returns>
    public int AcknowledgeAlarms() => _alarms.AcknowledgeAll();

    private void ApplyObjectives(IReadOnlyList<ObjectiveSnapshot> objectives)
    {
        _objectiveScratch.Clear();
        var done = 0;
        foreach (var objective in objectives)
        {
            if (objective.State == ObjectiveState.Completed) done++;
            _objectiveScratch.Add(new ObjectiveChip(objective.Id, objective.Label, _status.For(objective.State)));
        }
        KeyedSync.SyncRows(_objectives, _objectiveScratch, static chip => chip.Id);
        _objectivesSummary.Value = objectives.Count == 0
            ? string.Empty
            : _words.ObjectivesDone(done, objectives.Count);
    }

    private string FormatMetClock(MissionTime met) => _words.MetClock(ReadoutFormatter.FormatMet(met));
}
