using Usa.Game.Content;
using Usa.Game.Presentation.Console.Alarms;
using Usa.Game.Presentation.Console.Crew;
using Usa.Game.Presentation.Console.EventLog;
using Usa.Game.Presentation.Console.Header;
using Usa.Game.Presentation.Console.MainViews;
using Usa.Game.Presentation.Console.Resources;
using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.Presentation.Console.Systems;
using Usa.Game.Presentation.Console.Tasks;
using Usa.Game.Presentation.Console.Tips;
using Usa.Game.Presentation.ViewModels;

namespace Usa.Game.Presentation.Console;

/// <summary>
/// Root of the mission console view models. The session adapter calls <see cref="Apply"/> with each
/// <see cref="ConsoleSnapshot"/> and subscribes to the panels' signals (commands, crew orders, asks); the host
/// pumps the shared <see cref="UpdateThrottle"/> once per frame.
/// </summary>
public sealed class MissionConsoleViewModel : ViewModelBase
{
    /// <param name="text">Console words (<c>console.*</c> strings).</param>
    /// <param name="throttle">Limits fast-changing values to 10 Hz (UI.md 6.4); null for immediate notifications.</param>
    public MissionConsoleViewModel(ITextSource text, UpdateThrottle? throttle = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        Status = new StatusLanguage(text);
        Words = new ConsoleWords(text);
        Alarms = Track(new AlarmListViewModel(Status, Words));
        Header = Track(new HeaderViewModel(Alarms, Status, Words, throttle));
        Resources = Track(new ResourceStripViewModel(new ForecastText(text, Status), Words, throttle, AskRequested));
        Crew = Track(new CrewPanelViewModel(Status, Words));
        TaskSlot = Track(new TaskSlotViewModel(Words, throttle));
        Log = Track(new EventLogViewModel(Words, Alarms));
        Systems = Track(new SystemsBoardViewModel(Alarms, Status, Words, throttle, AskRequested));
        Tip = Track(new TipCardViewModel(Words));
        MainView = Track(new MainViewTabsViewModel(Words));

        Track(Resources.OpenTabRequested.Subscribe(tab => Systems.Select(tab)));
        Track(Tip.FactHighlightRequested.Subscribe(chip =>
        {
            if (chip.Tab is { } tab) Systems.Select(tab);
        }));
    }

    /// <summary>Shape, word and colour of every status, for views that draw statuses outside these panels.</summary>
    public StatusLanguage Status { get; }

    /// <summary>The console's fixed words and templates.</summary>
    public ConsoleWords Words { get; }

    public AlarmListViewModel Alarms { get; }

    public HeaderViewModel Header { get; }

    public ResourceStripViewModel Resources { get; }

    public CrewPanelViewModel Crew { get; }

    public TaskSlotViewModel TaskSlot { get; }

    public EventLogViewModel Log { get; }

    public SystemsBoardViewModel Systems { get; }

    public TipCardViewModel Tip { get; }

    /// <summary>The centre view tabs (Orbit, External, Diagram, Workspace).</summary>
    public MainViewTabsViewModel MainView { get; }

    /// <summary>"Ask" on any unknown value (resource strip or systems board).</summary>
    public Signal<AskDraft> AskRequested { get; } = new();

    public void Apply(ConsoleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        Alarms.Apply(snapshot.Alarms);
        Header.Apply(snapshot);
        Resources.Apply(snapshot);
        Crew.Apply(snapshot.Crew);
        TaskSlot.Apply(snapshot.ActiveTask, snapshot.Met, snapshot.Compression.Rate);
        Log.AppendRange(snapshot.NewLogEntries);
        Systems.Apply(snapshot.SystemsBoard, snapshot.Met);
        Tip.Apply(snapshot.Tip, snapshot.TipCooldownUntil, snapshot.Met);
    }
}
