using Godot;
using Usa.Game.Presentation.Console.Alarms;
using Usa.Game.Presentation.Console.EventLog;
using Usa.Game.UI.Console.Binding;
using Usa.Game.UI.Console.Components;

namespace Usa.Game.UI.Console.Regions;

/// <summary>
/// The event log (<c>EventLog.tscn</c>, Figma G1-03 node 14:357; UI.md 3.8): category filters, search, the pinned
/// unacknowledged critical alarms and the rows in a virtualized list (<see cref="VirtualLogList"/>), kept on the newest while
/// the player is at the bottom. The compact layout (Figma 21:807) folds the six filters into one "Filter: All" menu.
/// </summary>
public sealed partial class EventLog : PanelContainer
{
    private static readonly LogFilter[] FilterOrder = Enum.GetValues<LogFilter>();

    private readonly BindingScope _bindings = new();
    private (Button Button, LogFilter Filter)[] _filters = [];
    private Label _title = null!;
    private Control _filterButtons = null!;
    private OptionButton _filterMenu = null!;
    private LineEdit _search = null!;
    private VBoxContainer _pinned = null!;
    private VirtualLogList _list = null!;
    private EventLogViewModel? _log;
    private ConsoleLabels? _labels;
    private bool _compact;

    [Export]
    public PackedScene? AlarmRowScene { get; set; }

    public override void _Ready()
    {
        _filters =
        [
            (GetNode<Button>("%FilterAll"), LogFilter.All),
            (GetNode<Button>("%FilterAlarms"), LogFilter.Alarms),
            (GetNode<Button>("%FilterCrew"), LogFilter.Crew),
            (GetNode<Button>("%FilterCommands"), LogFilter.Commands),
            (GetNode<Button>("%FilterSystemChanges"), LogFilter.SystemChanges),
            (GetNode<Button>("%FilterDecisions"), LogFilter.Decisions),
        ];
        foreach (var (button, filter) in _filters) button.Pressed += () => _log?.SetFilter(filter);
        _title = GetNode<Label>("%Title");
        _filterButtons = GetNode<Control>("%Filters");
        _filterMenu = GetNode<OptionButton>("%FilterMenu");
        _search = GetNode<LineEdit>("%Search");
        _pinned = GetNode<VBoxContainer>("%Pinned");
        _list = GetNode<VirtualLogList>("%LogList");
        _search.TextChanged += text => _log?.SetSearch(text);
        _filterMenu.ItemSelected += index => _log?.SetFilter(FilterOrder[(int)index]);
        BuildFilterMenu();
    }

    public override void _ExitTree() => Unbind();

    /// <param name="alarms">The pinned rows' ACK (its word and what it acknowledges).</param>
    public void Bind(EventLogViewModel log, AlarmListViewModel alarms, ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(alarms);
        ArgumentNullException.ThrowIfNull(palette);
        Unbind();
        _log = log;
        _bindings.Observe(log.Filter, current =>
        {
            foreach (var (button, filter) in _filters) button.SetPressedNoSignal(filter == current);
            _filterMenu.Select(Array.IndexOf(FilterOrder, current));
            BuildFilterMenu();
        });
        _bindings.Observe(log.Search, text =>
        {
            if (_search.Text != text) _search.Text = text;
        });
        _list.Bind(log.Rows, palette);
        if (AlarmRowScene is { } alarmScene)
        {
            _bindings.Mirror(log.Pinned, new ListBinder<AlarmRow, AlarmRowView>(
                _pinned, alarmScene, (view, row) => view.Bind(row, palette, alarms)));
        }
        _bindings.Add(log.Pinned.Subscribe(_ => _pinned.Visible = log.Pinned.Count > 0));
        _pinned.Visible = log.Pinned.Count > 0;
    }

    public void Unbind()
    {
        _bindings.Release();
        _list.Unbind();
        _log = null;
    }

    public void ApplyLabels(ConsoleLabels labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        _labels = labels;
        _title.Text = labels.LogTitle;
        foreach (var (button, filter) in _filters) button.Text = FilterWord(filter, labels);
        _search.PlaceholderText = labels.LogSearch;
        BuildFilterMenu();
    }

    /// <summary><c>Ctrl+F</c>.</summary>
    public void FocusSearch() => _search.GrabFocus();

    public void ApplyLayout(ConsoleLayoutMetrics layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        CustomMinimumSize = new Vector2(0, layout.EventLogMinPx);
        _compact = layout.Mode == ConsoleLayoutMode.Compact;
        _filterButtons.Visible = !_compact;
        _filterMenu.Visible = _compact;
    }

    // "Filter: All" in the compact layout: the menu's own text names the current filter.
    private void BuildFilterMenu()
    {
        var selected = _log?.Filter.Value ?? LogFilter.All;
        _filterMenu.Clear();
        foreach (var filter in FilterOrder)
        {
            var word = _labels is { } labels ? FilterWord(filter, labels) : FilterButton(filter).Text;
            _filterMenu.AddItem(filter == selected && _labels is { } l ? l.LogFilterCompact(word) : word);
        }
        _filterMenu.Select(Array.IndexOf(FilterOrder, selected));
    }

    private Button FilterButton(LogFilter filter)
    {
        foreach (var (button, f) in _filters)
        {
            if (f == filter) return button;
        }
        return _filters[0].Button;
    }

    private static string FilterWord(LogFilter filter, ConsoleLabels labels) => filter switch
    {
        LogFilter.Alarms => labels.LogFilterAlarms,
        LogFilter.Crew => labels.LogFilterCrew,
        LogFilter.Commands => labels.LogFilterCommands,
        LogFilter.SystemChanges => labels.LogFilterSystemChanges,
        LogFilter.Decisions => labels.LogFilterDecisions,
        _ => labels.LogFilterAll,
    };
}
