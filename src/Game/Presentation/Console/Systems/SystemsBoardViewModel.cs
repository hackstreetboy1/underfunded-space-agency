using Usa.Game.Presentation.Console.Alarms;
using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Systems;

/// <summary>
/// The systems board (UI.md 3.9): the seven tabs in fixed order, the selected tab, and the shared
/// <see cref="ControlSignals"/> through which every control's execute, confirmation and crew-request intents leave.
/// </summary>
public sealed class SystemsBoardViewModel : ViewModelBase
{
    private readonly SystemsTabViewModel[] _tabs;
    private readonly ObservableValue<SystemsTab> _selected = new(SystemsTab.Power);

    /// <param name="alarms">Source of each tab's alarm rows and badge counts.</param>
    /// <param name="throttle">Limits readouts to 10 Hz; null for immediate notifications.</param>
    /// <param name="askRequested">Shared "Ask" sink; a private one when null.</param>
    public SystemsBoardViewModel(
        AlarmListViewModel alarms,
        StatusLanguage status,
        ConsoleWords words,
        UpdateThrottle? throttle = null,
        Signal<AskDraft>? askRequested = null)
    {
        ArgumentNullException.ThrowIfNull(alarms);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(words);
        AskRequested = askRequested ?? new Signal<AskDraft>();
        var tabs = Enum.GetValues<SystemsTab>();
        _tabs = new SystemsTabViewModel[tabs.Length];
        for (var i = 0; i < tabs.Length; i++) _tabs[i] = Track(new SystemsTabViewModel(tabs[i], alarms, Controls, AskRequested, status, words, throttle));
    }

    public IReadOnlyList<SystemsTabViewModel> Tabs => _tabs;

    public IReadOnlyObservable<SystemsTab> SelectedTab => _selected;

    public SystemsTabViewModel Selected => _tabs[(int)_selected.Value];

    public ControlSignals Controls { get; } = new();

    public Signal<AskDraft> AskRequested { get; }

    public SystemsTabViewModel TabFor(SystemsTab tab) => _tabs[(int)tab];

    /// <returns>True when the selection changed.</returns>
    public bool Select(SystemsTab tab) => _selected.Set(tab);

    public void Apply(IReadOnlyList<SystemsGroupSnapshot> groups, MissionTime now)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        foreach (var tab in _tabs) tab.Apply(GroupFor(groups, tab.Tab), now);
    }

    /// <summary>Finds a control anywhere on the board.</summary>
    public RemoteControlViewModel? FindControl(ComponentId id)
    {
        foreach (var tab in _tabs)
        {
            if (tab.FindControl(id) is { } control) return control;
        }
        return null;
    }

    private static SystemsGroupSnapshot? GroupFor(IReadOnlyList<SystemsGroupSnapshot> groups, SystemsTab tab)
    {
        foreach (var group in groups)
        {
            if (group.Tab == tab) return group;
        }
        return null;
    }
}
