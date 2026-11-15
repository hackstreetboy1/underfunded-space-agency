using Godot;
using Usa.Game.Content;
using Usa.Game.Presentation.Workspace;
using Usa.Game.UI.Console;
using Usa.Game.UI.Entry;
using Usa.Game.UI.Theme;
using Usa.Game.UI.Workspace.Geometry;
using Usa.Simulation.Kernel;

namespace Usa.Game.UI.Workspace;

/// <summary>
/// The engineering workspace main view (<c>res://ui/workspace/EngineeringWorkspace.tscn</c>; Figma G5-01a node 36:6607,
/// G5-01b node 37:2219): goal header 56, inventory tray 240, board (496 × 496 grid) and test panel 260 (Gate 5 decision 5).
/// It lays itself out for the main-view region it is given (<see cref="WorkspaceLayout"/>, R3-04): at 1000 × 560 the
/// three columns sit side by side and the board scales to fit; in the compact 600 × 406 view the tray narrows, the test
/// panel becomes a drawer opened from the board header, and the board scrolls below its legibility scale. A thin view
/// over <see cref="WorkspaceModel"/>: gestures go to the model, panels redraw on its change events. Assign needs a passing
/// test and the single crew task slot free (decision 4); with the slot busy the press is rejected through
/// <see cref="TaskSlotBusy"/> so the console can flash the slot (UI.md 3.7). Words come from <c>workspace.*</c> strings
/// once the host calls <see cref="ApplyText"/>.
/// </summary>
public sealed partial class EngineeringWorkspaceView : Control
{
    private const string LegendRow = "%Legend/Row/";

    private static readonly TextSlot[] Slots =
    [
        new("%TrayTitle", "workspace.trayTitle"),
        new("%TrayAside", "workspace.trayAside"),
        new("%TrayHint", "workspace.trayHint"),
        new("%BoardTitle", "workspace.boardTitle"),
        new("%BoardAside", "workspace.boardAside"),
        new("%TestToggle", "workspace.testOpen"),
        new("%TestToggle", "workspace.testOpenTooltip", Tooltip: true),
        new("%TestTitle", "workspace.testTitle"),
        new("%TestClose", "workspace.testClose"),
        new("%TestClose", "workspace.testCloseTooltip", Tooltip: true),
        new("%RunTestButton", "workspace.runTest"),
        new("%AssignButton", "workspace.assign"),
        new(LegendRow + "DType/Text", "workspace.legendDType"),
        new(LegendRow + "Round/Text", "workspace.legendRound"),
        new(LegendRow + "Wire/Text", "workspace.legendWire"),
        new(LegendRow + "Invalid/Text", "workspace.legendInvalid"),
    ];

    private readonly VariationSetter _variations = new();

    private Label _goalTitle = null!;
    private Label _goalDescription = null!;
    private Label _goalSource = null!;
    private Label _goalSink = null!;
    private Label _goalLimits = null!;
    private Label _difficultyAside = null!;
    private Control _columns = null!;
    private Control _tray = null!;
    private Control _testPanel = null!;
    private Button _testToggle = null!;
    private Button _testClose = null!;
    private VBoxContainer _trayRows = null!;
    private Control _boardHolder = null!;
    private BoardCanvas _board = null!;
    private Label _boardStatus = null!;
    private Label _routeText = null!;
    private PanelContainer _summary = null!;
    private TextureRect _summaryIcon = null!;
    private Label _summaryText = null!;
    private VBoxContainer _checkRows = null!;
    private Button _runTest = null!;
    private Button _assign = null!;
    private Label _assignReason = null!;
    private AssigneePickerView _picker = null!;
    private PopupMenu _contextMenu = null!;
    private TextureRect[] _legendIcons = [];
    private LegendSwatch[] _legendSwatches = [];

    private WorkspaceModel? _model;
    private ConsolePalette _palette = ConsolePalette.Default;
    private WorkspaceWords _words = WorkspaceWords.Design;
    private WorkspaceLayout? _layout;
    private bool _testOpen;
    private bool _slotFree = true;
    private string? _slotBusyText;
    private Gesture _gesture;
    private IReadOnlyList<ContextAction> _contextActions = [];

    /// <summary>Assign was pressed while the crew task slot is busy; the console flashes the slot with this reason.</summary>
    public event Action<string>? TaskSlotBusy;

    /// <summary><c>InventoryCard.tscn</c>, set in the scene.</summary>
    [Export]
    public PackedScene? CardScene { get; set; }

    /// <summary><c>CheckRow.tscn</c>, set in the scene.</summary>
    [Export]
    public PackedScene? CheckScene { get; set; }

    /// <summary>The layout this view chose for its current size.</summary>
    public WorkspaceLayout? Layout => _layout;

    private enum Gesture
    {
        None,
        Part,
        Wire,
    }

    public override void _Ready()
    {
        _goalTitle = GetNode<Label>("%GoalTitle");
        _goalDescription = GetNode<Label>("%GoalDescription");
        _goalSource = GetNode<Label>("%GoalSource");
        _goalSink = GetNode<Label>("%GoalSink");
        _goalLimits = GetNode<Label>("%GoalLimits");
        _difficultyAside = GetNode<Label>("%DifficultyAside");
        _columns = GetNode<Control>("%Columns");
        _tray = GetNode<Control>("%Tray");
        _testPanel = GetNode<Control>("%Test");
        _testToggle = GetNode<Button>("%TestToggle");
        _testClose = GetNode<Button>("%TestClose");
        _trayRows = GetNode<VBoxContainer>("%TrayRows");
        _boardHolder = GetNode<Control>("%BoardHolder");
        _board = GetNode<BoardCanvas>("%Board");
        _boardStatus = GetNode<Label>("%BoardStatus");
        _routeText = GetNode<Label>("%RouteText");
        _summary = GetNode<PanelContainer>("%Summary");
        _summaryIcon = GetNode<TextureRect>("%SummaryIcon");
        _summaryText = GetNode<Label>("%SummaryText");
        _checkRows = GetNode<VBoxContainer>("%CheckRows");
        _runTest = GetNode<Button>("%RunTestButton");
        _assign = GetNode<Button>("%AssignButton");
        _assignReason = GetNode<Label>("%AssignReason");
        _picker = GetNode<AssigneePickerView>("%AssigneePicker");
        _contextMenu = GetNode<PopupMenu>("%ContextMenu");
        _legendIcons = [GetNode<TextureRect>(LegendRow + "DType/Icon"), GetNode<TextureRect>(LegendRow + "Round/Icon")];
        _legendSwatches = [GetNode<LegendSwatch>(LegendRow + "Wire/Swatch"), GetNode<LegendSwatch>(LegendRow + "Invalid/Swatch")];

        // The approved Assign icon replaces the ▸ glyph IBM Plex lacks (R3-11).
        _assign.Icon = HudIcons.Texture(UiIcons.ActionAssign);
        _assign.IconAlignment = HorizontalAlignment.Right;
        _assign.AddThemeConstantOverride("icon_max_width", 16);

        _board.GuiInput += OnBoardInput;
        _runTest.Pressed += () => _model?.RunTest();
        _assign.Pressed += OnAssignPressed;
        _testToggle.Toggled += SetTestOpen;
        _testClose.Pressed += () => SetTestOpen(false);
        _picker.Confirmed += OnAssigneeConfirmed;
        _picker.Cancelled += () => _picker.Visible = false;
        _contextMenu.IdPressed += OnContextAction;
        _picker.Visible = false;
        ApplyPalette();
        ApplyLayout();
        RefreshAll();
    }

    public override void _ExitTree() => Unbind();

    public override void _Notification(int what)
    {
        if (what == NotificationResized && IsNodeReady()) ApplyLayout();
    }

    /// <summary>The workspace's fixed words from <c>content/strings.json</c>, once per mission.</summary>
    public void ApplyText(ITextSource text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _words = WorkspaceWords.From(text);
        if (!IsNodeReady()) return;
        SceneText.Apply(this, text, Slots);
        _picker.ApplyText(text, _words);
        RefreshAll();
    }

    public void Bind(WorkspaceModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Unbind();
        _model = model;
        model.HeaderChanged += OnHeaderChanged;
        model.TrayChanged += OnTrayChanged;
        model.BoardChanged += OnBoardChanged;
        model.DragChanged += OnBoardChanged;
        model.WireChanged += OnBoardChanged;
        model.TestChanged += OnTestChanged;
        model.AssignmentChanged += OnTestChanged;
        model.HistoryChanged += OnBoardChanged;
        _board.Bind(model);
        RefreshAll();
    }

    public void Unbind()
    {
        if (_model is { } model)
        {
            model.HeaderChanged -= OnHeaderChanged;
            model.TrayChanged -= OnTrayChanged;
            model.BoardChanged -= OnBoardChanged;
            model.DragChanged -= OnBoardChanged;
            model.WireChanged -= OnBoardChanged;
            model.TestChanged -= OnTestChanged;
            model.AssignmentChanged -= OnTestChanged;
            model.HistoryChanged -= OnBoardChanged;
        }
        _model = null;
        _gesture = Gesture.None;
        if (IsNodeReady()) _board.Bind(null);
    }

    /// <summary>Whether the single crew task slot is free (UI.md 3.7); <paramref name="busyText"/> is the slot's "Crew busy: …" line.</summary>
    public void SetTaskSlot(bool free, string? busyText = null)
    {
        _slotFree = free;
        _slotBusyText = busyText;
        RefreshAssign();
    }

    public void SetPalette(ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        _board.SetPalette(palette);
        _picker.SetPalette(palette);
        ApplyPalette();
        RefreshAll();
    }

    /// <summary>Opens or closes the test drawer (compact view); the docked test panel is always open.</summary>
    public void SetTestOpen(bool open)
    {
        _testOpen = open;
        ApplyLayout();
    }

    public override void _Input(InputEvent @event)
    {
        if (_gesture == Gesture.None || _model is not { } model) return;
        var point = BoardPointer();
        switch (@event)
        {
            case InputEventMouseMotion:
                _board.SetPointer(_board.GetLocalMousePosition());
                if (_gesture == Gesture.Part) model.MoveDrag(point);
                else model.HoverWire(BoardHitTest.PortAt(model.Cards, model.FixedNodes, point)?.Port);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                FinishGesture(model, point);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                CancelGesture(model);
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!IsVisibleInTree() || _model is not { } model || @event is not InputEventKey { Pressed: true, CtrlPressed: true } key) return;
        EditResult? result = key.Keycode switch
        {
            Key.Z when key.ShiftPressed => model.Redo(),
            Key.Z => model.Undo(),
            Key.Y => model.Redo(),
            _ => null,
        };
        if (result is not { } done) return;
        ShowResult(done);
        GetViewport().SetInputAsHandled();
    }

    private void ApplyLayout()
    {
        if (!IsNodeReady()) return;
        var layout = WorkspaceLayout.For(Size.X, Size.Y);
        _layout = layout;
        _tray.CustomMinimumSize = new Vector2(layout.TrayWidthPx, 0);
        _columns.OffsetRight = layout.TestDocked ? -layout.TestWidthPx : 0;
        _testPanel.OffsetLeft = -layout.TestWidthPx;
        _testPanel.Visible = layout.TestDocked || _testOpen;
        _testToggle.Visible = !layout.TestDocked;
        _testToggle.SetPressedNoSignal(_testOpen);
        _testClose.Visible = !layout.TestDocked;

        // The board keeps its grid coordinates; only its drawing scales (hit tests read local positions).
        var scale = layout.BoardScale;
        _board.Scale = new Vector2(scale, scale);
        _board.Position = new Vector2(WorkspaceLayout.BoardInsetXPx, WorkspaceLayout.BoardInsetYPx);
        var extent = (WorkspaceLayout.BoardGridPx * scale) + (2 * WorkspaceLayout.BoardInsetYPx);
        // When it scrolls, the last rows can be scrolled out from under the legend strip.
        _boardHolder.CustomMinimumSize = new Vector2(
            (WorkspaceLayout.BoardGridPx * scale) + (2 * WorkspaceLayout.BoardInsetXPx),
            layout.BoardFits ? extent : extent + WorkspaceLayout.LegendPx);
        if (_picker.Visible) PlacePicker();
    }

    /// <summary>Keeps the picker inside the view at any size: bottom right, above the test panel's buttons.</summary>
    private void PlacePicker()
    {
        var size = _picker.GetCombinedMinimumSize();
        var x = Math.Max(8, Size.X - size.X - 12);
        var y = Math.Clamp(Size.Y - size.Y - 12, 8, Math.Max(8, Size.Y - size.Y));
        _picker.Position = new Vector2(x, y);
    }

    private void ApplyPalette()
    {
        foreach (var icon in _legendIcons) icon.SelfModulate = _palette.Of("text.mid");
        foreach (var swatch in _legendSwatches) swatch.SetPalette(_palette);
    }

    private BoardPoint BoardPointer()
    {
        var local = _board.GetLocalMousePosition();
        return new BoardPoint(local.X, local.Y);
    }

    private void OnBoardInput(InputEvent @event)
    {
        if (_model is not { IsOpen: true } model || _gesture != Gesture.None) return;
        if (@event is not InputEventMouseButton { Pressed: true } button) return;
        var point = new BoardPoint(button.Position.X, button.Position.Y);
        if (button.ButtonIndex == MouseButton.Right)
        {
            OpenContextMenu(model, point);
            _board.AcceptEvent();
            return;
        }
        if (button.ButtonIndex != MouseButton.Left) return;
        if (BoardHitTest.PortAt(model.Cards, model.FixedNodes, point) is { } port)
        {
            if (Begin(model.BeginWire(port.Port), Gesture.Wire)) _board.SetPointer(button.Position);
        }
        else if (BoardHitTest.CardAt(model.Cards, point) is { } card)
        {
            Begin(model.BeginCardDrag(card.Id, point), Gesture.Part);
        }
        _board.AcceptEvent();
    }

    private void OnTrayDragStarted(PartCard card)
    {
        if (_model is not { } model || _gesture != Gesture.None) return;
        Begin(model.BeginTrayDrag(card.Part, BoardPointer()), Gesture.Part);
    }

    private bool Begin(EditResult result, Gesture gesture)
    {
        ShowResult(result);
        if (!result.Succeeded) return false;
        _gesture = gesture;
        return true;
    }

    private void FinishGesture(WorkspaceModel model, BoardPoint point)
    {
        var gesture = _gesture;
        _gesture = Gesture.None;
        if (gesture == Gesture.Part)
        {
            // Released back over the tray: the part goes back, no complaint.
            if (point.X < 0) model.CancelDrag();
            else ShowResult(model.Drop(point));
            return;
        }
        if (model.Wire is { Hover: { } target })
        {
            ShowResult(model.CompleteWire(target));
            return;
        }
        model.CancelWire();
    }

    private void CancelGesture(WorkspaceModel model)
    {
        if (_gesture == Gesture.Part) model.CancelDrag();
        else model.CancelWire();
        _gesture = Gesture.None;
    }

    private void OpenContextMenu(WorkspaceModel model, BoardPoint point)
    {
        var card = BoardHitTest.CardAt(model.Cards, point)?.Id ?? BoardHitTest.NodeAt(model.FixedNodes, point)?.Port.Card;
        if (card is not { } id) return;
        _contextActions = model.ContextActionsFor(id);
        if (_contextActions.Count == 0) return;
        _contextMenu.Clear();
        for (var i = 0; i < _contextActions.Count; i++) _contextMenu.AddItem(_contextActions[i].Label, i);
        _contextMenu.Popup(new Rect2I((Vector2I)GetGlobalMousePosition(), Vector2I.Zero));
    }

    private void OnContextAction(long id)
    {
        if (_model is null || id < 0 || id >= _contextActions.Count) return;
        ShowResult(_model.Invoke(_contextActions[(int)id]));
    }

    private void OnAssignPressed()
    {
        if (_model is not { CanAssign: true } model) return;
        if (!_slotFree)
        {
            var reason = _slotBusyText ?? _words.SlotBusy;
            _assignReason.Text = reason;
            TaskSlotBusy?.Invoke(reason);
            return;
        }
        if (model.PreviewAssignment() is not { } preview) return;
        _picker.Show(preview);
        Callable.From(PlacePicker).CallDeferred();
    }

    private void OnAssigneeConfirmed(CrewId crew)
    {
        if (_model is not { } model) return;
        if (!_slotFree)
        {
            TaskSlotBusy?.Invoke(_slotBusyText ?? _words.SlotBusy);
            return;
        }
        var result = model.Assign(crew);
        _picker.Visible = false;
        if (!result.Succeeded) _assignReason.Text = result.Problem == AssignProblem.AssigneeUnavailable ? _words.AssigneeUnavailable : _words.AssignUnlocks;
    }

    private void ShowResult(EditResult result) => _boardStatus.ShowText(result.Succeeded ? null : result.Message);

    private void OnHeaderChanged(object? sender, EventArgs e) => RefreshHeader();

    private void OnTrayChanged(object? sender, EventArgs e) => RefreshTray();

    private void OnBoardChanged(object? sender, EventArgs e)
    {
        _board.Redraw();
        RefreshRoute();
    }

    private void OnTestChanged(object? sender, EventArgs e)
    {
        RefreshTest();
        RefreshAssign();
    }

    private void RefreshAll()
    {
        if (!IsNodeReady()) return;
        RefreshHeader();
        RefreshTray();
        _board.Redraw();
        RefreshRoute();
        RefreshTest();
        RefreshAssign();
    }

    private void RefreshHeader()
    {
        var header = _model?.Header;
        var words = _words;
        _goalTitle.Text = header is null ? words.GoalTitle : WorkspaceWords.Fill(words.GoalTitleNamed, ("name", header.Name.ToUpperInvariant()));
        _goalDescription.Text = header?.Description ?? string.Empty;
        _goalSource.Text = header is null ? string.Empty
            : WorkspaceWords.Fill(words.GoalSource, ("label", header.Source.Label), ("connector", header.Source.ConnectorName));
        _goalSink.Text = header is null ? string.Empty
            : WorkspaceWords.Fill(words.GoalSink, ("label", header.Sink.Label), ("connector", header.Sink.ConnectorName));
        _goalLimits.Text = header is null ? string.Empty
            : WorkspaceWords.Fill(words.GoalLimits, ("load", header.LoadText), ("rating", header.WireRatingText), ("length", header.MinLengthText));
    }

    private void RefreshTray()
    {
        if (_model is not { } model)
        {
            ChildSync.Sync(_trayRows, 0, CardScene);
            return;
        }
        var tray = model.Tray;
        var before = _trayRows.GetChildCount();
        ChildSync.Sync(_trayRows, tray.Count, CardScene);
        for (var i = 0; i < tray.Count; i++)
        {
            var view = _trayRows.GetChild<InventoryCardView>(i);
            if (i >= before) view.DragStarted += OnTrayDragStarted;
            view.Show(tray[i], _words, _palette);
        }
    }

    private void RefreshRoute() => _routeText.Text = _model?.Route.Text ?? string.Empty;

    private void RefreshTest()
    {
        var test = _model?.Test ?? TestPanel.NotRun;
        _difficultyAside.Text = test.State == TestState.NotRun ? string.Empty : test.ExplanationsShown ? _words.DifficultyEasy : _words.DifficultyNormal;
        _summary.Visible = test.State != TestState.NotRun;
        var (token, shape) = test.State == TestState.Stale ? ("caution", HudShape.Caution)
            : test.Passed ? ("ok", HudShape.Done)
            : ("critical", HudShape.Failed);
        _variations.SetBadge(_summary, _summaryText, token);
        HudIcons.Show(_summaryIcon, shape, _palette.Of(token));
        _summaryText.Text = test.RunText.Length > 0
            ? WorkspaceWords.Fill(_words.TestSummaryRun, ("summary", test.Summary), ("run", test.RunText))
            : test.Summary;
        ChildSync.Sync(_checkRows, test.Rows.Count, CheckScene);
        for (var i = 0; i < test.Rows.Count; i++) _checkRows.GetChild<CheckRowView>(i).Show(test.Rows[i], _palette);
        _runTest.Disabled = _model is not { IsOpen: true, IsLocked: false };
    }

    private void RefreshAssign()
    {
        if (!IsNodeReady()) return;
        var model = _model;
        _assign.Disabled = model is not { CanAssign: true };
        _assignReason.Text = model switch
        {
            { IsLocked: true } => _words.AssignedNote,
            { CanAssign: true } when !_slotFree => _slotBusyText ?? _words.SlotBusy,
            { CanAssign: true } => _words.AssignOpensPicker,
            _ => _words.AssignUnlocks,
        };
        if (model is not { CanAssign: true }) _picker.Visible = false;
    }
}
