using Godot;
using Usa.Game.Presentation.Console.MainViews;
using Usa.Game.UI.Console.Binding;
using Usa.Game.UI.Console.Overlays;
using Usa.Game.UI.Console.Views;
using Usa.Game.UI.Entry;
using Usa.Game.UI.Map;
using Usa.Game.UI.Workspace;

namespace Usa.Game.UI.Console.Regions;

/// <summary>
/// The centre view tabs (<c>MainViewTabs.tscn</c>, Figma G1-03 node 14:304; UI.md 3.6): Orbit (<c>ui/map/OrbitMapView.tscn</c>),
/// External (<c>ui/views/ExternalView.tscn</c> with the entry HUD), Diagram (<c>ui/views/InternalDiagram.tscn</c>) and Workspace (<c>ui/workspace/EngineeringWorkspace.tscn</c>, only while a repair is open), with the recovery and
/// crew-lost closing cards and the captions over them. The selection lives in <see cref="MainViewTabsViewModel"/>.
/// </summary>
public sealed partial class MainViewTabs : PanelContainer
{
    public const int ExternalViewIndex = (int)MainViewKind.External;

    private readonly BindingScope _bindings = new();
    private TabBar _tabs = null!;
    private Control[] _slots = [];
    private MainViewTabsViewModel? _model;
    private bool _syncing;

    public OrbitMapView Orbit { get; private set; } = null!;

    /// <summary>The External view chrome around the 3D view.</summary>
    public ExternalViewChrome External { get; private set; } = null!;

    /// <summary>The internal 2D diagram (<c>ui/views/InternalDiagram.tscn</c>).</summary>
    public InternalDiagramView Diagram { get; private set; } = null!;

    public EngineeringWorkspaceView Workspace { get; private set; } = null!;

    /// <summary>The splashdown / recovery card, centred over the views.</summary>
    public RecoveryCardView RecoveryCard { get; private set; } = null!;

    /// <summary>The crew-lost closing card, centred over the views.</summary>
    public ClosingCard ClosingCard { get; private set; } = null!;

    /// <summary>Crew captions, over the bottom of the main view.</summary>
    public CaptionPanel Captions { get; private set; } = null!;

    /// <summary>The selected view changed (index as <see cref="SelectedView"/>).</summary>
    public event Action<int>? ViewChanged;

    /// <summary>The selected view: 0 Orbit, 1 External, 2 Diagram, 3 Workspace.</summary>
    public int SelectedView => _tabs.CurrentTab;

    public override void _Ready()
    {
        _tabs = GetNode<TabBar>("%Tabs");
        Captions = GetNode<CaptionPanel>("%Captions");
        Orbit = GetNode<OrbitMapView>("%Orbit");
        External = GetNode<ExternalViewChrome>("%External");
        Diagram = GetNode<InternalDiagramView>("%Diagram");
        Workspace = GetNode<EngineeringWorkspaceView>("%Workspace");
        RecoveryCard = GetNode<RecoveryCardView>("%RecoveryCard");
        ClosingCard = GetNode<ClosingCard>("%ClosingCard");
        _slots =
        [
            GetNode<Control>("%OrbitSlot"),
            GetNode<Control>("%ExternalSlot"),
            GetNode<Control>("%DiagramSlot"),
            GetNode<Control>("%WorkspaceSlot"),
        ];
        _tabs.TabChanged += OnTabChanged;
        HideCutViews();
        ShowSlot(_tabs.CurrentTab);
    }

    /// <summary>
    /// Demo cuts 5 and 6 (docs/dev/DEMO_CUTS.md): the Diagram and Workspace views are not offered. Their tabs are hidden and
    /// cannot be selected by key; remove the views from this list to bring them back.
    /// </summary>
    private static readonly int[] CutViews = [(int)MainViewKind.Diagram, (int)MainViewKind.Workspace];

    public static bool IsCut(int view) => Array.IndexOf(CutViews, view) >= 0;

    private void HideCutViews()
    {
        foreach (var view in CutViews)
        {
            if (view < _tabs.TabCount) _tabs.SetTabHidden(view, true);
        }
    }

    public override void _ExitTree() => Unbind();

    /// <summary>Follows (and drives) the console's main-view selection.</summary>
    public void Bind(MainViewTabsViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Unbind();
        _model = model;
        _bindings.Observe(model.Tabs, tabs =>
        {
            for (var i = 0; i < tabs.Count && i < _tabs.TabCount; i++)
            {
                _tabs.SetTabTitle(i, tabs[i].Label + "   " + tabs[i].KeyHint);
                _tabs.SetTabDisabled(i, !tabs[i].Enabled);
            }
            HideCutViews();
        });
        _bindings.Observe(model.Selected, view =>
        {
            if (IsCut((int)view) && model.SelectIndex((int)MainViewKind.Orbit)) return;
            if (_tabs.CurrentTab != (int)view)
            {
                _syncing = true;
                _tabs.CurrentTab = (int)view;
                _syncing = false;
            }
            ShowSlot((int)view);
            ViewChanged?.Invoke((int)view);
        });
    }

    public void Unbind()
    {
        _bindings.Release();
        _model = null;
    }

    /// <summary>A tab button's global rectangle (the tutorial points at the Orbit tab while another view is shown).</summary>
    public Rect2 TabRect(int view)
    {
        var tab = _tabs.GetTabRect(view);
        return new Rect2(_tabs.GetGlobalRect().Position + tab.Position, tab.Size);
    }

    /// <summary>The container for a view (index as <see cref="SelectedView"/>).</summary>
    public Control Slot(int view) => _slots[view];

    /// <summary>Keys <c>1</c>–<c>4</c>; a disabled tab (Workspace with no repair open) is ignored.</summary>
    public void SelectView(int view)
    {
        if (IsCut(view)) return;
        if (_model is { } model)
        {
            model.SelectIndex(view);
            return;
        }
        if (view < 0 || view >= _tabs.TabCount || _tabs.IsTabDisabled(view)) return;
        _tabs.CurrentTab = view;
    }

    /// <summary>Enables the Workspace tab while a repair workspace is open; closing it returns to the Diagram.</summary>
    public void SetWorkspaceAvailable(bool available)
    {
        if (_model is { } model)
        {
            model.SetWorkspaceAvailable(available);
            return;
        }
        const int workspace = (int)MainViewKind.Workspace;
        _tabs.SetTabDisabled(workspace, !available);
        if (!available && _tabs.CurrentTab == workspace) _tabs.CurrentTab = (int)MainViewKind.Diagram;
    }

    public void ApplyLayout(ConsoleLayoutMetrics layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        CustomMinimumSize = new Vector2(0, layout.MainViewPx);
        // The captions stay inside the compact 600 px view.
        var halfWidth = Math.Min(400, layout.CentreColumnPx / 2 - 24);
        Captions.OffsetLeft = -halfWidth;
        Captions.OffsetRight = halfWidth;
    }

    private void OnTabChanged(long index)
    {
        if (_syncing) return;
        if (_model is { } model)
        {
            // The view model decides; a refused selection (a disabled tab) snaps back.
            if (!model.SelectIndex((int)index)) _tabs.CurrentTab = (int)model.Selected.Value;
            return;
        }
        ShowSlot((int)index);
        ViewChanged?.Invoke((int)index);
    }

    private void ShowSlot(int index)
    {
        for (var i = 0; i < _slots.Length; i++) _slots[i].Visible = i == index;
    }
}
