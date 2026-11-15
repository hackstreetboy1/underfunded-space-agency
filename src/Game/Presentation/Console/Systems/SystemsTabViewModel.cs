using Usa.Game.Presentation.Console.Alarms;
using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Systems;

/// <summary>One systems-board tab: grouped readouts, load list, remote controls and the system's alarms.</summary>
public sealed class SystemsTabViewModel : ViewModelBase
{
    private readonly ControlSignals _signals;
    private readonly StatusLanguage _status;
    private readonly ConsoleWords _words;
    private readonly Signal<AskDraft> _askRequested;
    private readonly UpdateThrottle? _throttle;
    private readonly ObservableList<ReadoutGroupViewModel> _readoutGroups = new();
    private readonly ObservableList<LoadRowViewModel> _loads = new();
    private readonly ObservableList<RemoteControlViewModel> _controls = new();
    private readonly ObservableValue<SystemAlarmCount> _alarmCount;

    public SystemsTabViewModel(
        SystemsTab tab,
        AlarmListViewModel alarms,
        ControlSignals signals,
        Signal<AskDraft> askRequested,
        StatusLanguage status,
        ConsoleWords words,
        UpdateThrottle? throttle = null)
    {
        ArgumentNullException.ThrowIfNull(alarms);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(words);
        _status = status;
        _words = words;
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(askRequested);
        Tab = tab;
        _signals = signals;
        _askRequested = askRequested;
        _throttle = throttle;
        Alarms = alarms.RowsFor(tab);
        _alarmCount = new ObservableValue<SystemAlarmCount>(alarms.Summary.Value.For(tab));
        Observe(alarms.Summary, summary => _alarmCount.Value = summary.For(tab), invokeImmediately: false);
    }

    public SystemsTab Tab { get; }

    public string Label => _words.TabLabel(Tab);

    public string IconKey => SystemsTabInfo.IconKey(Tab);

    public bool UsesLegacyPanelStyle => SystemsTabInfo.UsesLegacyPanelStyle(Tab);

    /// <summary>Tape label on the legacy panel; null on the others.</summary>
    public string? TapeLabel => UsesLegacyPanelStyle ? _words.LegacyTapeLabel : null;

    public ObservableList<ReadoutGroupViewModel> ReadoutGroups => _readoutGroups;

    public ObservableList<LoadRowViewModel> Loads => _loads;

    public ObservableList<RemoteControlViewModel> Controls => _controls;

    /// <summary>This system's alarms (shared with <see cref="AlarmListViewModel.RowsFor"/>).</summary>
    public ObservableList<AlarmRow> Alarms { get; }

    /// <summary>Tab badge counts.</summary>
    public IReadOnlyObservable<SystemAlarmCount> AlarmCount => _alarmCount;

    /// <param name="group">Null clears the tab (nothing known to show).</param>
    public void Apply(SystemsGroupSnapshot? group, MissionTime now)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (group is not null && group.Tab != Tab)
            throw new ArgumentException($"Group for {group.Tab} applied to tab {Tab}.", nameof(group));

        KeyedSync.Sync(
            _readoutGroups,
            group?.ReadoutGroups ?? [],
            static g => g.Id,
            static g => g.Id,
            g =>
            {
                var created = new ReadoutGroupViewModel(g.Id, _askRequested, _words, _throttle);
                created.Apply(g, now);
                return created;
            },
            (existing, g) =>
            {
                existing.Apply(g, now);
                return existing;
            });

        KeyedSync.Sync(
            _loads,
            group?.Loads ?? [],
            static load => load.Id,
            static row => row.Id,
            load => new LoadRowViewModel(load, _signals, _status, _words),
            (row, load) =>
            {
                row.Apply(load, now);
                return row;
            });

        KeyedSync.Sync(
            _controls,
            group?.Controls ?? [],
            static control => control.ComponentId,
            static control => control.ComponentId,
            control => new RemoteControlViewModel(control, _signals, _words),
            (existing, control) =>
            {
                existing.Apply(control, now);
                return existing;
            });
    }

    /// <summary>Finds a control on this tab, standalone or as a load's switch.</summary>
    public RemoteControlViewModel? FindControl(ComponentId id)
    {
        foreach (var control in _controls)
        {
            if (control.ComponentId == id) return control;
        }
        foreach (var load in _loads)
        {
            if (load.Switch is { } control && control.ComponentId == id) return control;
        }
        return null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            foreach (var group in _readoutGroups) group.Dispose();
            foreach (var load in _loads) load.Dispose();
            foreach (var control in _controls) control.Dispose();
        }
        base.Dispose(disposing);
    }
}
