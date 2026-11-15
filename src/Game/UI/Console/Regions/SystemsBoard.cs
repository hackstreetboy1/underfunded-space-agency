using Godot;
using Usa.Game.Presentation.Console.Alarms;
using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.Presentation.Console.Systems;
using Usa.Game.UI.Console.Binding;
using Usa.Game.UI.Console.Components;

namespace Usa.Game.UI.Console.Regions;

/// <summary>
/// The systems board (<c>SystemsBoard.tscn</c>, Figma G1-03 node 15:364; UI.md 3.9): the seven system tabs with alarm
/// shapes, and the selected tab's alarms, readout groups, load list and remote controls. The Thermal tab draws its content
/// in the older Program 2 panel with the tape label.
/// </summary>
public sealed partial class SystemsBoard : PanelContainer
{
    private readonly BindingScope _bindings = new();
    private readonly BindingScope _tabContent = new();
    private PanelHeader _header = null!;
    private TabBar _tabs = null!;
    private VBoxContainer _alarms = null!;
    private VBoxContainer _groups = null!;
    private Control _legacyPanel = null!;
    private PanelHeader _legacyHeader = null!;
    private VBoxContainer _legacyGroups = null!;
    private Label _tapeLabel = null!;
    private Control _loadsSection = null!;
    private VBoxContainer _loads = null!;
    private Control _controlsSection = null!;
    private VBoxContainer _controls = null!;
    private SystemsBoardViewModel? _board;
    private ConsolePalette _palette = ConsolePalette.Default;
    private AlarmListViewModel? _alarmList;
    private StatusLanguage? _status;
    private ConsoleLabels? _labels;
    private Label _loadsCaption = null!;
    private Label _controlsCaption = null!;
    private Label _controlsAside = null!;

    [Export]
    public PackedScene? AlarmRowScene { get; set; }

    [Export]
    public PackedScene? ReadoutGroupScene { get; set; }

    [Export]
    public PackedScene? LoadRowScene { get; set; }

    [Export]
    public PackedScene? ControlRowScene { get; set; }

    public override void _Ready()
    {
        _header = GetNode<PanelHeader>("%Header");
        _tabs = GetNode<TabBar>("%Tabs");
        _alarms = GetNode<VBoxContainer>("%Alarms");
        _groups = GetNode<VBoxContainer>("%Groups");
        _legacyPanel = GetNode<Control>("%LegacyPanel");
        _legacyHeader = GetNode<PanelHeader>("%LegacyHeader");
        _legacyGroups = GetNode<VBoxContainer>("%LegacyGroups");
        _tapeLabel = GetNode<Label>("%TapeLabel");
        _loadsSection = GetNode<Control>("%LoadsSection");
        _loads = GetNode<VBoxContainer>("%Loads");
        _controlsSection = GetNode<Control>("%ControlsSection");
        _controls = GetNode<VBoxContainer>("%Controls");
        _loadsCaption = GetNode<Label>("%LoadsCaption");
        _controlsCaption = GetNode<Label>("%ControlsCaption");
        _controlsAside = GetNode<Label>("%ControlsAside");
        _tabs.TabChanged += index => _board?.Select((SystemsTab)(int)index);
    }

    public override void _ExitTree() => Unbind();

    /// <param name="alarms">The alarm rows' ACK (its word and what it acknowledges).</param>
    public void Bind(SystemsBoardViewModel board, AlarmListViewModel alarms, StatusLanguage status, ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(alarms);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(palette);
        Unbind();
        _palette = palette;
        _alarmList = alarms;
        _status = status;

        // Resizing the tab bar can emit TabChanged; the board is attached afterwards so that never selects a tab.
        _tabs.TabCount = board.Tabs.Count;
        _board = board;
        for (var i = 0; i < board.Tabs.Count; i++)
        {
            var index = i;
            var tab = board.Tabs[i];
            _bindings.Observe(tab.AlarmCount, count =>
            {
                // The alarm shape is an icon beside the label (review R3-11), never a font glyph.
                _tabs.SetTabTitle(index, tab.Label);
                var alert = ConsoleText.TabAlert(count, status);
                _tabs.SetTabIcon(index, alert is not null && StatusIcons.For(alert.Shape) is { } icon ? StatusIconView.Texture(icon) : null);
            });
        }
        _bindings.Observe(board.SelectedTab, selected =>
        {
            if (_tabs.CurrentTab != (int)selected) _tabs.CurrentTab = (int)selected;
            ShowTab(board.TabFor(selected));
        });
    }

    public void Unbind()
    {
        _tabContent.Release();
        _bindings.Release();
        _board = null;
    }

    public void ApplyLabels(ConsoleLabels labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        _labels = labels;
        _header.Title = labels.SystemsTitle;
        _legacyHeader.Aside = labels.SystemsLegacyAside;
        _loadsCaption.Text = labels.SystemsLoads;
        _controlsCaption.Text = labels.SystemsRemoteControls;
        _controlsAside.Text = labels.SystemsRemoteAside;
        if (_board is { } board) ShowTab(board.Selected);
    }

    public void ApplyLayout(ConsoleLayoutMetrics layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        // The tab bar never sets the column's width: tabs that do not fit scroll (Godot verification, 1280 × 720).
        _tabs.ClipTabs = true;
    }

    /// <summary>The Power tab's load list (watts and dependencies) when shown, else the tab bar; for the guided first run.</summary>
    public Control PowerAnchor() =>
        _board?.SelectedTab.Value == SystemsTab.Power && _loadsSection.IsVisibleInTree() ? _loadsSection : _tabs;

    /// <summary>The source and MET stamp of the first readout on the selected tab; null when none is on screen.</summary>
    public Control? FirstVisibleSourceStamp()
    {
        var container = _legacyPanel.Visible ? _legacyGroups : _groups;
        for (var i = 0; i < container.GetChildCount(); i++)
        {
            if (container.GetChild(i) is ReadoutGroupView { Visible: true } group && group.FirstVisibleSourceStamp() is { } stamp) return stamp;
        }
        return null;
    }

    private void ShowTab(SystemsTabViewModel tab)
    {
        _tabContent.Release();
        _header.Aside = tab.Label;

        var legacy = tab.UsesLegacyPanelStyle;
        _legacyPanel.Visible = legacy;
        _groups.Visible = !legacy;
        if (legacy)
        {
            _legacyHeader.Title = tab.Label;
            _tapeLabel.ShowText(tab.TapeLabel);
        }

        var palette = _palette;
        var labels = _labels;
        if (AlarmRowScene is { } alarmScene && _alarmList is { } alarmList)
        {
            _tabContent.Mirror(tab.Alarms, new ListBinder<AlarmRow, AlarmRowView>(_alarms, alarmScene, (view, row) => view.Bind(row, palette, alarmList)));
        }
        if (ReadoutGroupScene is { } groupScene && _status is { } status)
        {
            _tabContent.Mirror(tab.ReadoutGroups, new ListBinder<ReadoutGroupViewModel, ReadoutGroupView>(
                legacy ? _legacyGroups : _groups, groupScene, (view, group) => view.Bind(group, status, palette, labels)));
        }
        if (LoadRowScene is { } loadScene)
        {
            _tabContent.Mirror(tab.Loads, new ListBinder<LoadRowViewModel, LoadRow>(_loads, loadScene, (view, load) =>
            {
                view.Bind(load);
                if (labels is not null) view.ApplyLabels(labels);
            }));
        }
        if (ControlRowScene is { } controlScene)
        {
            _tabContent.Mirror(tab.Controls, new ListBinder<RemoteControlViewModel, RemoteControlRow>(
                _controls, controlScene, (view, control) =>
                {
                    view.Bind(control, palette);
                    if (labels is not null) view.ApplyLabels(labels);
                }));
        }
        TrackSectionVisibility(tab);
    }

    private void TrackSectionVisibility(SystemsTabViewModel tab)
    {
        void Refresh()
        {
            _alarms.Visible = tab.Alarms.Count > 0;
            _loadsSection.Visible = tab.Loads.Count > 0;
            _controlsSection.Visible = tab.Controls.Count > 0;
        }

        _tabContent.Add(tab.Alarms.Subscribe(_ => Refresh()));
        _tabContent.Add(tab.Loads.Subscribe(_ => Refresh()));
        _tabContent.Add(tab.Controls.Subscribe(_ => Refresh()));
        Refresh();
    }
}
