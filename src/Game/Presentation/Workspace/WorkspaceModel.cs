using Usa.Game.Content;
using Usa.Simulation.Engineering;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Game.Presentation.Workspace;

/// <summary>
/// Presentation model of the engineering workspace (GDD 8, UI 4.6): goal header, inventory tray, a board on a
/// 16 px grid, wiring gestures, undo/redo, the digital test and the Assign step. Godot-free; views read the
/// snapshot properties again when a change event fires.
/// </summary>
/// <remarks>
/// <para>Tray quantities are a workspace-side reservation: placing a card takes one from the tray and removing it
/// (or undoing the place) gives it back, on top of <see cref="Inventory.Available"/>. The model never writes the
/// simulation's <see cref="Inventory"/>: that would bypass the command log, and the validator's inventory check
/// counts real reservations as unavailable. The parts are really reserved when the assigned design is registered
/// for install_repair.</para>
/// <para>Assign needs a passing test with no later circuit edit (place, remove, wire, unwire, or their undo/redo)
/// and no later change to the stock of a part on the board. Moving a card is layout only and keeps the test.</para>
/// </remarks>
public sealed class WorkspaceModel
{
    public const string ExplainPath = StatePaths.Difficulty + "workspace_explain";

    /// <summary>The mission clock, stamped on each test run.</summary>
    public const string MetPath = "mission.met_s";

    /// <summary>Knowledge fact paths: <c>knowledge.fact.&lt;id&gt;.value|state|met_s</c>.</summary>
    public const string FactPrefix = "knowledge.fact.";

    private readonly PartCatalog _catalog;
    private readonly RepairCatalog _repairs;
    private readonly Inventory _inventory;
    private readonly IStateQuery _state;
    private readonly IRepairAssignmentEstimator _estimator;
    private readonly AssemblyValidator _validator;
    private readonly WorkspaceOptions _options;
    private readonly WorkspaceText _text;
    private readonly IRepairReservations? _reservations;
    private readonly IRepairRequirementSource? _requirements;
    private readonly List<Card> _cards = [];
    private readonly Stack<BoardEdit> _undo = new();
    private readonly Stack<BoardEdit> _redo = new();
    private readonly Dictionary<PartId, int> _stockAtTest = [];
    private AssemblyGraph? _graph;
    private DragSession? _drag;
    private DragPreview? _dragPreview;
    private WireGesture? _wire;
    private AssemblyReport? _report;
    private IReadOnlyList<CheckRow> _rows = [];
    private bool _explain;
    private double? _testedAtMetS;
    private bool _assigned;
    private bool _lastCanAssign;
    private int _nextCard = 1;
    private int _revision;
    private int _testedRevision = -1;

    public WorkspaceModel(
        PartCatalog catalog,
        RepairCatalog repairs,
        Inventory inventory,
        IStateQuery state,
        IRepairAssignmentEstimator estimator,
        ITextSource text,
        WorkspaceOptions? options = null,
        IRepairReservations? reservations = null,
        IRepairRequirementSource? requirements = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(repairs);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(estimator);
        ArgumentNullException.ThrowIfNull(text);
        _options = options ?? WorkspaceOptions.Default;
        _options.Validate();
        _catalog = catalog;
        _repairs = repairs;
        _inventory = inventory;
        _state = state;
        _estimator = estimator;
        _text = new WorkspaceText(text);
        _validator = new AssemblyValidator(catalog);
        _reservations = reservations;
        _requirements = requirements;
    }

    public event EventHandler? HeaderChanged;

    public event EventHandler? TrayChanged;

    /// <summary>Cards, wires or the lock state changed.</summary>
    public event EventHandler? BoardChanged;

    public event EventHandler? DragChanged;

    public event EventHandler? WireChanged;

    public event EventHandler? TestChanged;

    public event EventHandler? HistoryChanged;

    /// <summary><see cref="CanAssign"/> changed.</summary>
    public event EventHandler? AssignmentChanged;

    /// <summary>The design was assigned; forward it to the command pipeline as install_repair.</summary>
    public event EventHandler<RepairAssignment>? Assigned;

    [Flags]
    private enum Change
    {
        None = 0,
        Header = 1,
        Tray = 2,
        Board = 4,
        Drag = 8,
        Wire = 16,
        Test = 32,
        History = 64,
        All = Header | Tray | Board | Drag | Wire | Test | History,
    }

    public BoardLayout Layout => _options.Layout;

    public bool IsOpen => _graph is not null;

    /// <summary>The design was assigned; the board is read-only until another <c>Open</c>.</summary>
    public bool IsLocked => _assigned;

    public GoalHeader? Header { get; private set; }

    /// <summary>Spares with stock or cards on the board, by kind (harness, adapter, fuse, breaker, tape, canister) then id.</summary>
    public IReadOnlyList<PartCard> Tray => BuildTray();

    /// <summary>Placed cards in placement order.</summary>
    public IReadOnlyList<BoardCard> Cards => BuildCards();

    /// <summary>The goal's source (left) and sink (right).</summary>
    public IReadOnlyList<FixedNode> FixedNodes => BuildNodes();

    public IReadOnlyList<BoardWire> Wires => BuildWires();

    /// <summary>The route wired from the source and its length, for the board legend.</summary>
    public BoardRoute Route => BuildRoute();

    public DragPreview? Drag => _dragPreview;

    public WireGesture? Wire => _wire;

    public TestPanel Test
    {
        get
        {
            if (_report is null) return TestPanel.NotRun;
            var state = _testedRevision == _revision ? TestState.Current : TestState.Stale;
            var failed = 0;
            foreach (var row in _rows)
            {
                if (row.Status == CheckStatus.Failed) failed++;
            }
            return new TestPanel(state, _report.Passed, _explain, _text.TestSummary(state, failed), _rows, _testedAtMetS,
                _testedAtMetS is { } met ? _text.TestRunAt(met) : "");
        }
    }

    public bool CanUndo => IsEditable && _undo.Count > 0;

    public bool CanRedo => IsEditable && _redo.Count > 0;

    public bool CanAssign => IsEditable && _report is { Passed: true } && _testedRevision == _revision;

    /// <summary>The assignment made from this board, once <see cref="Assign"/> succeeds.</summary>
    public RepairAssignment? Assignment { get; private set; }

    private bool IsEditable => _graph is not null && !_assigned;

    /// <summary>The board being edited, for a save or a retry; null when no goal is open.</summary>
    public WorkspaceBoardSave? SavedBoard
    {
        get
        {
            if (_graph is null) return null;
            var design = _graph.ToSavedDesign();
            var cells = new GridPoint[design.Parts.Count];
            for (var i = 0; i < cells.Length; i++)
            {
                var instance = new PartInstanceId(design.Instances![i]);
                foreach (var card in _cards)
                {
                    if (card.Instance == instance) cells[i] = card.Cell;
                }
            }
            return new WorkspaceBoardSave(design, cells);
        }
    }

    /// <summary>Reopens a board from <see cref="SavedBoard"/>, cards where they were.</summary>
    public WorkspaceOpenResult Open(string goalId, WorkspaceBoardSave saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        return Open(goalId, saved.Design, saved.CardCells);
    }

    /// <summary>
    /// Opens a goal on an empty board, or on <paramref name="design"/> (a retry or a save, see <see cref="SavedBoard"/>).
    /// The sink is the goal's first candidate whose condition holds now. The previous board is kept when opening fails.
    /// </summary>
    /// <param name="cardCells">Where each design part's card goes; a cell off the board or overlapping takes the next free slot.</param>
    public WorkspaceOpenResult Open(string goalId, AssemblyDesign? design = null, IReadOnlyList<GridPoint>? cardCells = null)
    {
        ArgumentNullException.ThrowIfNull(goalId);
        if (!_repairs.TryGet(goalId, out var goal)) return WorkspaceOpenResult.UnknownGoal;
        if (goal.ResolveSink(_state) is not { } sink) return WorkspaceOpenResult.NothingToRepair;

        AssemblyGraph graph;
        if (design is null)
        {
            graph = new AssemblyGraph(_catalog, goal, sink);
        }
        else
        {
            if (design.Sink != sink.Definition.Component) return WorkspaceOpenResult.DesignMismatch;
            if (!AssemblyGraph.TryRestore(_catalog, goal, design, out var restored).Succeeded || restored is null)
                return WorkspaceOpenResult.DesignMismatch;
            graph = restored;
        }

        Reset(graph);
        var index = 0;
        foreach (var placed in graph.Parts)
        {
            var cell = cardCells is not null && index < cardCells.Count && FitsOnBoard(cardCells[index]) && !Overlaps(cardCells[index], null)
                ? cardCells[index]
                : FreeCell();
            index++;
            _cards.Add(new Card(new CardId(_nextCard++), placed.Part, cell, placed.Instance));
        }
        Header = BuildHeader(goal, sink);
        Notify(Change.All);
        return WorkspaceOpenResult.Opened;
    }

    public void Close()
    {
        Reset(null);
        Header = null;
        Notify(Change.All);
    }

    /// <summary>Starts dragging a new part from the tray; the card is centred under the pointer. Check <see cref="Drag"/> for drop feedback.</summary>
    public EditResult BeginTrayDrag(PartId part, BoardPoint position)
    {
        if (!CanEdit(out var refused)) return refused;
        if (!_catalog.TryGet(part, out var definition)) return Refused(AssemblyFailure.UnknownPart, part.Value);
        StartDrag(new DragSession(definition, null, CardWidthPx / 2.0, CardHeightPx / 2.0), position);
        return EditResult.Ok;
    }

    /// <summary>Starts moving a placed card, keeping the pointer's offset within it.</summary>
    public EditResult BeginCardDrag(CardId card, BoardPoint position)
    {
        if (!CanEdit(out var refused)) return refused;
        if (card.IsEndpoint) return Refused(AssemblyFailure.EndpointFixed, "");
        var index = IndexOf(card);
        if (index < 0) return Refused(AssemblyFailure.UnknownInstance, "");
        var placed = _cards[index];
        StartDrag(new DragSession(placed.Part, placed, position.X - placed.Cell.XPx, position.Y - placed.Cell.YPx), position);
        return EditResult.Ok;
    }

    public DragPreview? MoveDrag(BoardPoint position)
    {
        if (_drag is null) return null;
        _dragPreview = Preview(_drag, position);
        Notify(Change.Drag);
        return _dragPreview;
    }

    /// <summary>Places or moves the card at the snapped cell; an invalid drop leaves the board unchanged.</summary>
    public EditResult Drop(BoardPoint position)
    {
        if (_drag is not { } drag) return Problem(EditProblem.NoGesture, "");
        var preview = Preview(drag, position);
        _drag = null;
        _dragPreview = null;
        if (!preview.IsValid)
        {
            Notify(Change.Drag);
            return new EditResult(preview.Problem, preview.Failure, preview.Message);
        }

        if (drag.Card is null)
        {
            var result = Do(new PlaceEdit(new CardId(_nextCard), drag.Part, preview.Cell), Change.Drag);
            if (result.Succeeded) _nextCard++;
            return result;
        }
        if (drag.Card.Cell == preview.Cell)
        {
            Notify(Change.Drag);
            return EditResult.Ok;
        }
        return Do(new MoveEdit(drag.Card.Id, drag.Card.Cell, preview.Cell), Change.Drag);
    }

    public void CancelDrag()
    {
        if (_drag is null) return;
        _drag = null;
        _dragPreview = null;
        Notify(Change.Drag);
    }

    /// <summary>Starts a wire at a free connector.</summary>
    public EditResult BeginWire(BoardPort from)
    {
        if (!CanEdit(out var refused)) return refused;
        var failure = FreePortFailure(from);
        if (failure != AssemblyFailure.None) return Refused(failure, "");
        _wire = new WireGesture(from, null, AssemblyFailure.None, "");
        Notify(Change.Wire);
        return EditResult.Ok;
    }

    /// <summary>
    /// Feedback for the port under the pointer (null when over nothing). Mismatched connector types are allowed here:
    /// the digital test explains them, so the board never solves the design for the player (GDD 8).
    /// </summary>
    public WireGesture? HoverWire(BoardPort? target)
    {
        if (_wire is null) return null;
        if (target is not { } port)
        {
            _wire = _wire with { Hover = null, HoverFailure = AssemblyFailure.None, HoverMessage = "" };
        }
        else
        {
            var failure = Probe(_wire.From, port);
            _wire = _wire with { Hover = port, HoverFailure = failure, HoverMessage = _text.Failure(failure, "") };
        }
        Notify(Change.Wire);
        return _wire;
    }

    /// <summary>Ends the wire gesture at <paramref name="target"/>, connecting when the graph accepts it.</summary>
    public EditResult CompleteWire(BoardPort target)
    {
        if (_wire is null) return Problem(EditProblem.NoGesture, "");
        var from = _wire.From;
        _wire = null;
        var result = Do(new ConnectEdit(from, target), Change.Wire);
        if (!result.Succeeded) Notify(Change.Wire);
        return result;
    }

    public void CancelWire()
    {
        if (_wire is null) return;
        _wire = null;
        Notify(Change.Wire);
    }

    /// <summary>Right-click actions: remove a card, and disconnect each wired connector (cards and fixed nodes).</summary>
    public IReadOnlyList<ContextAction> ContextActionsFor(CardId card)
    {
        var actions = new List<ContextAction>();
        if (!IsEditable) return actions;
        if (card.IsEndpoint)
        {
            if (card != CardId.Source && card != CardId.Sink) return actions;
            var port = new BoardPort(card, 0);
            if (LinkedTo(port) is not null) actions.Add(new ContextAction(ContextActionKind.Disconnect, card, port, _text.DisconnectAction(LabelOf(card))));
            return actions;
        }

        var index = IndexOf(card);
        if (index < 0) return actions;
        var part = _cards[index].Part;
        actions.Add(new ContextAction(ContextActionKind.RemovePart, card, default, _text.RemovePartAction(part.Name)));
        for (var p = 0; p < part.Connectors.Count; p++)
        {
            var port = new BoardPort(card, p);
            if (LinkedTo(port) is null) continue;
            actions.Add(new ContextAction(ContextActionKind.Disconnect, card, port,
                _text.DisconnectConnectorAction(p, _catalog.ConnectorName(part.Connectors[p]))));
        }
        return actions;
    }

    public EditResult Invoke(ContextAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return action.Kind == ContextActionKind.RemovePart ? RemovePart(action.Card) : Disconnect(action.Port);
    }

    /// <summary>Takes a card off the board with its wires; the part goes back to the tray.</summary>
    public EditResult RemovePart(CardId card)
    {
        if (!CanEdit(out var refused)) return refused;
        if (card.IsEndpoint) return Refused(AssemblyFailure.EndpointFixed, "");
        var index = IndexOf(card);
        if (index < 0) return Refused(AssemblyFailure.UnknownInstance, "");
        var placed = _cards[index];
        return Do(new RemoveEdit(placed.Id, placed.Part, placed.Cell, LinksOf(placed)), Change.None);
    }

    /// <summary>Removes the wire at <paramref name="port"/>.</summary>
    public EditResult Disconnect(BoardPort port)
    {
        if (!CanEdit(out var refused)) return refused;
        if (!TryToGraph(port, out var graphPort)) return Refused(AssemblyFailure.UnknownInstance, "");
        if (_graph!.LinkedTo(graphPort) is not { } other)
        {
            var failure = _graph.Disconnect(graphPort).Failure;
            return Refused(failure == AssemblyFailure.None ? AssemblyFailure.NotConnected : failure, "");
        }
        return Do(new DisconnectEdit(port, FromGraph(other)), Change.None);
    }

    public EditResult Undo()
    {
        if (!CanEdit(out var refused)) return refused;
        if (_undo.Count == 0) return Problem(EditProblem.NothingToUndo, "");
        var edit = _undo.Peek();
        var result = Apply(edit, forward: false);
        if (!result.Succeeded) return result;
        _undo.Pop();
        _redo.Push(edit);
        Committed(edit, EndGestures());
        return result;
    }

    public EditResult Redo()
    {
        if (!CanEdit(out var refused)) return refused;
        if (_redo.Count == 0) return Problem(EditProblem.NothingToRedo, "");
        var edit = _redo.Peek();
        var result = Apply(edit, forward: true);
        if (!result.Succeeded) return result;
        _redo.Pop();
        _undo.Push(edit);
        Committed(edit, EndGestures());
        return result;
    }

    /// <summary>Runs the digital test on the board as it is now.</summary>
    public TestPanel RunTest()
    {
        if (_graph is null) return Test;
        _report = _validator.Validate(_graph, ValidationInventory());
        _explain = _state.TryGet(ExplainPath, out var explain) && explain.Kind == StateValueKind.Bool && explain.AsBool();
        _testedAtMetS = Number(MetPath) is var met && double.IsFinite(met) ? met : null;
        var rows = new CheckRow[_report.Checks.Count];
        for (var i = 0; i < rows.Length; i++)
        {
            var check = _report.Checks[i];
            rows[i] = new CheckRow(check.Check, check.Status, check.Reason, WorkspaceText.CheckMark(check.Status),
                _text.CheckWord(check.Status), check.Code, _explain ? _text.CheckTitle(check.Check) : null,
                _explain ? _text.Explanation(check) : null);
        }
        _rows = rows;
        _testedRevision = _revision;
        _stockAtTest.Clear();
        foreach (var part in _catalog.Parts) _stockAtTest[part.Id] = EffectiveAvailable(part.Id);
        Notify(Change.Test);
        return Test;
    }

    /// <summary>Re-reads the spares. Call when the simulation publishes <see cref="InventoryChanged"/>.</summary>
    public void RefreshInventory()
    {
        if (_graph is null) return;
        var change = Change.Tray;
        if (_report is not null && _testedRevision == _revision && BoardStockChangedSinceTest())
        {
            _testedRevision = -1;
            change |= Change.Test;
        }
        Notify(change);
    }

    /// <summary>Convenience for hosts that forward every drained simulation event.</summary>
    public void OnSimEvent(ISimEvent simEvent)
    {
        if (simEvent is InventoryChanged) RefreshInventory();
    }

    /// <summary>Who can install the tested design; null unless <see cref="CanAssign"/>.</summary>
    public AssignmentPreview? PreviewAssignment()
    {
        if (!CanAssign) return null;
        var task = _report!.Task!;
        var estimates = _estimator.Estimate(task);
        var options = new AssigneeOption[estimates.Count];
        for (var i = 0; i < options.Length; i++) options[i] = ToOption(task, estimates[i]);
        var requirements = Requirements(task);
        return new AssignmentPreview(_graph!.Goal.Id, _graph.Goal.Name, task, options, requirements, RequirementsLine(task, requirements));
    }

    /// <summary>Hands the tested design to <paramref name="assignee"/> and locks the board.</summary>
    public AssignmentResult Assign(CrewId assignee)
    {
        if (_graph is null) return new AssignmentResult(AssignProblem.NotOpen, null);
        if (_assigned) return new AssignmentResult(AssignProblem.AlreadyAssigned, null);
        if (!CanAssign) return new AssignmentResult(AssignProblem.NotTested, null);

        var task = _report!.Task!;
        AssigneeEstimate? chosen = null;
        foreach (var estimate in _estimator.Estimate(task))
        {
            if (estimate.Crew == assignee) chosen = estimate;
        }
        if (chosen is null) return new AssignmentResult(AssignProblem.UnknownAssignee, null);
        if (!chosen.Available) return new AssignmentResult(AssignProblem.AssigneeUnavailable, null);

        var assignment = new RepairAssignment(_graph.Goal.Id, task.Design, assignee, task);
        _assigned = true;
        Assignment = assignment;
        Notify(Change.Board | Change.History | EndGestures());
        Assigned?.Invoke(this, assignment);
        return new AssignmentResult(AssignProblem.None, assignment);
    }

    private abstract record BoardEdit(bool ChangesCircuit);

    private sealed record PlaceEdit(CardId Card, PartDefinition Part, GridPoint Cell) : BoardEdit(true);

    private sealed record RemoveEdit(CardId Card, PartDefinition Part, GridPoint Cell, IReadOnlyList<Link> Links) : BoardEdit(true);

    private sealed record MoveEdit(CardId Card, GridPoint From, GridPoint To) : BoardEdit(false);

    private sealed record ConnectEdit(BoardPort A, BoardPort B) : BoardEdit(true);

    private sealed record DisconnectEdit(BoardPort A, BoardPort B) : BoardEdit(true);

    private readonly record struct Link(BoardPort A, BoardPort B);

    private sealed record Card(CardId Id, PartDefinition Part, GridPoint Cell, PartInstanceId Instance);

    private sealed record DragSession(PartDefinition Part, Card? Card, double GrabXPx, double GrabYPx);

    private EditResult Do(BoardEdit edit, Change also)
    {
        var result = Apply(edit, forward: true);
        if (!result.Succeeded)
        {
            Notify(also);
            return result;
        }
        _undo.Push(edit);
        _redo.Clear();
        Committed(edit, also);
        return result;
    }

    private void Committed(BoardEdit edit, Change also)
    {
        var change = Change.Board | Change.History | also;
        if (edit.ChangesCircuit)
        {
            _revision++;
            change |= Change.Tray | (_report is null ? Change.None : Change.Test);
        }
        Notify(change);
    }

    private EditResult Apply(BoardEdit edit, bool forward) => edit switch
    {
        PlaceEdit place => forward ? AddCard(place.Card, place.Part, place.Cell, []) : RemoveCard(place.Card),
        RemoveEdit remove => forward ? RemoveCard(remove.Card) : AddCard(remove.Card, remove.Part, remove.Cell, remove.Links),
        MoveEdit move => MoveCard(move.Card, forward ? move.To : move.From),
        ConnectEdit connect => forward ? Connect(connect.A, connect.B) : Unplug(connect.A),
        DisconnectEdit disconnect => forward ? Unplug(disconnect.A) : Connect(disconnect.A, disconnect.B),
        _ => throw new InvalidOperationException($"Unknown board edit {edit}."),
    };

    private EditResult AddCard(CardId id, PartDefinition part, GridPoint cell, IReadOnlyList<Link> links)
    {
        if (Quantity(part.Id) <= 0) return Problem(EditProblem.NoneAvailable, part.Name);
        var placed = _graph!.Place(part.Id);
        if (!placed.Succeeded) return Refused(placed.Failure, part.Name);
        _cards.Add(new Card(id, part, cell, placed.Instance));
        foreach (var link in links)
        {
            // Undo is last-in-first-out, so every wire a removal took away can go back.
            var restored = Connect(link.A, link.B);
            if (!restored.Succeeded) throw new InvalidOperationException($"Undo could not restore the wire {link.A}-{link.B}: {restored.Failure}.");
        }
        return EditResult.Ok;
    }

    private EditResult RemoveCard(CardId id)
    {
        var index = IndexOf(id);
        if (index < 0) return Refused(AssemblyFailure.UnknownInstance, "");
        var removed = _graph!.Remove(_cards[index].Instance);
        if (!removed.Succeeded) return Refused(removed.Failure, _cards[index].Part.Name);
        _cards.RemoveAt(index);
        return EditResult.Ok;
    }

    private EditResult MoveCard(CardId id, GridPoint cell)
    {
        var index = IndexOf(id);
        if (index < 0) return Refused(AssemblyFailure.UnknownInstance, "");
        _cards[index] = _cards[index] with { Cell = cell };
        return EditResult.Ok;
    }

    private EditResult Connect(BoardPort a, BoardPort b)
    {
        if (!TryToGraph(a, out var ga) || !TryToGraph(b, out var gb)) return Refused(AssemblyFailure.UnknownInstance, "");
        var result = _graph!.Connect(ga, gb);
        return result.Succeeded ? EditResult.Ok : Refused(result.Failure, "");
    }

    private EditResult Unplug(BoardPort port)
    {
        if (!TryToGraph(port, out var graphPort)) return Refused(AssemblyFailure.UnknownInstance, "");
        var result = _graph!.Disconnect(graphPort);
        return result.Succeeded ? EditResult.Ok : Refused(result.Failure, "");
    }

    /// <summary>What <see cref="AssemblyGraph.Connect"/> would say, without keeping the link.</summary>
    private AssemblyFailure Probe(BoardPort a, BoardPort b)
    {
        if (!TryToGraph(a, out var ga) || !TryToGraph(b, out var gb)) return AssemblyFailure.UnknownInstance;
        // The graph has no "can connect" query; connecting and unlinking keeps hover feedback identical to the real edit.
        var result = _graph!.Connect(ga, gb);
        if (result.Succeeded) _graph.Disconnect(ga);
        return result.Failure;
    }

    private AssemblyFailure FreePortFailure(BoardPort port)
    {
        if (!TryToGraph(port, out var graphPort)) return AssemblyFailure.UnknownInstance;
        var ports = port.Card.IsEndpoint ? 1 : _cards[IndexOf(port.Card)].Part.Connectors.Count;
        if (port.Port < 0 || port.Port >= ports) return AssemblyFailure.UnknownPort;
        return _graph!.LinkedTo(graphPort) is null ? AssemblyFailure.None : AssemblyFailure.PortInUse;
    }

    private int CardWidthPx => Layout.CardColumns * BoardLayout.CellPx;

    private int CardHeightPx => Layout.CardRows * BoardLayout.CellPx;

    private void StartDrag(DragSession session, BoardPoint position)
    {
        _wire = null;
        _drag = session;
        _dragPreview = Preview(session, position);
        Notify(Change.Drag | Change.Wire);
    }

    private DragPreview Preview(DragSession drag, BoardPoint position)
    {
        var layout = Layout;
        var snapped = GridPoint.Snap(position.X - drag.GrabXPx, position.Y - drag.GrabYPx);
        // A card dropped across an edge is pulled inside; only a pointer off the board refuses the drop.
        var cell = new GridPoint(
            Math.Clamp(snapped.Column, 0, layout.Columns - layout.CardColumns),
            Math.Clamp(snapped.Row, 0, layout.CardAreaRows - layout.CardRows));

        var problem = EditProblem.None;
        var failure = AssemblyFailure.None;
        if (!drag.Part.IsAssemblyPart)
        {
            problem = EditProblem.Refused;
            failure = AssemblyFailure.NotAnAssemblyPart;
        }
        else if (drag.Card is null && Quantity(drag.Part.Id) <= 0)
        {
            problem = EditProblem.NoneAvailable;
        }
        else if (position.X < 0 || position.Y < 0 || position.X >= layout.WidthPx || position.Y >= layout.HeightPx)
        {
            problem = EditProblem.OutsideBoard;
        }
        else if (Overlaps(cell, drag.Card?.Id))
        {
            problem = EditProblem.Overlaps;
        }

        var message = problem == EditProblem.Refused
            ? _text.Failure(failure, drag.Part.Name)
            : _text.Problem(problem, drag.Part.Name);
        return new DragPreview(drag.Part.Id, drag.Card?.Id, cell, cell.XPx, cell.YPx, CardWidthPx, CardHeightPx, problem, failure, message);
    }

    private bool FitsOnBoard(GridPoint cell) =>
        cell.Column >= 0 && cell.Row >= 0
        && cell.Column + Layout.CardColumns <= Layout.Columns && cell.Row + Layout.CardRows <= Layout.CardAreaRows;

    private bool Overlaps(GridPoint cell, CardId? except)
    {
        var layout = Layout;
        foreach (var card in _cards)
        {
            if (card.Id != except && Intersects(cell, layout.CardColumns, layout.CardRows, card.Cell, layout.CardColumns, layout.CardRows))
                return true;
        }
        return Intersects(cell, layout.CardColumns, layout.CardRows, layout.SourceCell, layout.NodeColumns, layout.NodeRows)
            || Intersects(cell, layout.CardColumns, layout.CardRows, layout.SinkCell, layout.NodeColumns, layout.NodeRows);
    }

    private static bool Intersects(GridPoint a, int aColumns, int aRows, GridPoint b, int bColumns, int bRows) =>
        a.Column < b.Column + bColumns && b.Column < a.Column + aColumns && a.Row < b.Row + bRows && b.Row < a.Row + aRows;

    /// <summary>The first free slot in reading order, for cards of a restored design.</summary>
    private GridPoint FreeCell()
    {
        var layout = Layout;
        for (var row = 1; row + layout.CardRows <= layout.CardAreaRows; row += layout.CardRows + 1)
        {
            for (var column = 1; column + layout.CardColumns <= layout.Columns; column += layout.CardColumns + 1)
            {
                var cell = new GridPoint(column, row);
                if (!Overlaps(cell, null)) return cell;
            }
        }
        return new GridPoint(layout.Columns - layout.CardColumns, 0);
    }

    private (int X, int Y) AnchorOf(BoardPort port)
    {
        var layout = Layout;
        if (port.Card == CardId.Source || port.Card == CardId.Sink)
        {
            var node = port.Card == CardId.Source ? layout.SourceCell : layout.SinkCell;
            var x = node.XPx + (layout.NodePortOnRight(node) ? layout.NodeColumns * BoardLayout.CellPx : 0);
            return (x, node.YPx + (layout.NodeRows * BoardLayout.CellPx / 2));
        }
        var cell = _cards[IndexOf(port.Card)].Cell;
        return (cell.XPx + (port.Port == 0 ? 0 : CardWidthPx), cell.YPx + (CardHeightPx / 2));
    }

    private bool TryToGraph(BoardPort port, out PortRef graphPort)
    {
        if (port.Card == CardId.Source || port.Card == CardId.Sink)
        {
            graphPort = new PortRef(port.Card == CardId.Source ? PartInstanceId.Source : PartInstanceId.Sink, port.Port);
            return true;
        }
        var index = IndexOf(port.Card);
        graphPort = index >= 0 ? new PortRef(_cards[index].Instance, port.Port) : default;
        return index >= 0;
    }

    private BoardPort FromGraph(PortRef port)
    {
        if (port.Instance == PartInstanceId.Source) return new BoardPort(CardId.Source, port.Port);
        if (port.Instance == PartInstanceId.Sink) return new BoardPort(CardId.Sink, port.Port);
        foreach (var card in _cards)
        {
            if (card.Instance == port.Instance) return new BoardPort(card.Id, port.Port);
        }
        throw new InvalidOperationException($"No card for {port}.");
    }

    private BoardPort? LinkedTo(BoardPort port) =>
        TryToGraph(port, out var graphPort) && _graph!.LinkedTo(graphPort) is { } other ? FromGraph(other) : null;

    private List<Link> LinksOf(Card card)
    {
        var links = new List<Link>(card.Part.Connectors.Count);
        for (var p = 0; p < card.Part.Connectors.Count; p++)
        {
            var port = new BoardPort(card.Id, p);
            if (LinkedTo(port) is { } other) links.Add(new Link(port, other));
        }
        return links;
    }

    private int IndexOf(CardId id)
    {
        for (var i = 0; i < _cards.Count; i++)
        {
            if (_cards[i].Id == id) return i;
        }
        return -1;
    }

    private string LabelOf(CardId card) =>
        card == CardId.Source ? _graph!.Goal.Source.Label : card == CardId.Sink ? _graph!.Sink.Port.Label : _cards[IndexOf(card)].Part.Name;

    private int OnBoard(PartId part)
    {
        var count = 0;
        foreach (var card in _cards)
        {
            if (card.Part.Id == part) count++;
        }
        return count;
    }

    private int Quantity(PartId part) => Math.Max(0, EffectiveAvailable(part) - OnBoard(part));

    /// <summary>Spares free for this goal: the available stock plus what the goal's own registration holds.</summary>
    private int EffectiveAvailable(PartId part) => _inventory.Available(part) + ReservedByOpenGoal(part);

    private int ReservedByOpenGoal(PartId part)
    {
        if (_graph is null || _reservations is null) return 0;
        var held = 0;
        foreach (var quantity in _reservations.ReservedBy(_graph.Goal.Id))
        {
            if (quantity.Part == part) held += quantity.Count;
        }
        // Never count more than the spares really hold in reserve (a stale registration report).
        return Math.Clamp(held, 0, _inventory.Reserved(part));
    }

    /// <summary>
    /// The validator reads <see cref="Inventory.Available"/>; with a registration of the open goal, it checks a scratch
    /// copy where that registration's parts are free again, as the simulation does before it re-registers. The real
    /// inventory is never written.
    /// </summary>
    private Inventory ValidationInventory()
    {
        var returned = false;
        var items = new List<InventoryItem>(_inventory.Parts.Count);
        foreach (var part in _inventory.Parts)
        {
            var own = ReservedByOpenGoal(part);
            returned |= own > 0;
            items.Add(new InventoryItem(part, _inventory.Available(part) + own));
        }
        if (!returned) return _inventory;
        return new Inventory(_catalog, new InventoryDefinition(InventoryDefinition.SchemaTag, "workspace", "preview", items), new EventBus());
    }

    private bool BoardStockChangedSinceTest()
    {
        foreach (var card in _cards)
        {
            var id = card.Part.Id;
            if (!_stockAtTest.TryGetValue(id, out var atTest) || atTest != EffectiveAvailable(id)) return true;
        }
        return false;
    }

    private GoalHeader BuildHeader(RepairGoal goal, RepairSink sink)
    {
        var source = new PortSummary(goal.Source.Component, goal.Source.Label, goal.Source.Connector, _catalog.ConnectorName(goal.Source.Connector));
        var target = new PortSummary(sink.Port.Component, sink.Port.Label, sink.Port.Connector, _catalog.ConnectorName(sink.Port.Connector));
        var load = WorkspaceText.Amps(goal.LoadCurrentA);
        return new GoalHeader(
            goal.Id,
            goal.Name,
            goal.Definition.Description,
            source,
            target,
            load,
            WorkspaceText.Amps(goal.WireRatingA),
            WorkspaceText.Metres(goal.MinLengthM),
            _text.GoalSummary(source, target, load));
    }

    private List<PartCard> BuildTray()
    {
        var tray = new List<PartCard>();
        if (_graph is null) return tray;
        foreach (var part in _catalog.Parts)
        {
            var onBoard = OnBoard(part.Id);
            if (_inventory.Count(part.Id) == 0 && onBoard == 0) continue;
            var stock = EffectiveAvailable(part.Id);
            tray.Add(new PartCard(
                part.Id,
                WorkspaceText.IconKey(part),
                WorkspaceText.CardName(part),
                part.Kind,
                Math.Max(0, stock - onBoard),
                onBoard,
                Math.Max(0, onBoard - stock),
                LengthText(part),
                RatingText(part),
                Chips(part),
                part.IsAssemblyPart));
        }
        tray.Sort((a, b) => a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind) : a.Part.CompareTo(b.Part));
        return tray;
    }

    private List<BoardCard> BuildCards()
    {
        var cards = new List<BoardCard>(_cards.Count);
        foreach (var card in _cards)
        {
            var ports = new BoardPortView[card.Part.Connectors.Count];
            for (var p = 0; p < ports.Length; p++) ports[p] = PortView(new BoardPort(card.Id, p), card.Part.Connectors[p]);
            cards.Add(new BoardCard(card.Id, card.Part.Id, WorkspaceText.CardName(card.Part), WorkspaceText.IconKey(card.Part), card.Cell, card.Cell.XPx,
                card.Cell.YPx, CardWidthPx, CardHeightPx, LengthText(card.Part), RatingText(card.Part), ports));
        }
        return cards;
    }

    private List<FixedNode> BuildNodes()
    {
        if (_graph is null) return [];
        var layout = Layout;
        var width = layout.NodeColumns * BoardLayout.CellPx;
        var height = layout.NodeRows * BoardLayout.CellPx;
        return
        [
            new FixedNode(BoardPort.Source, _graph.Goal.Source.Label, layout.SourceCell, layout.SourceCell.XPx, layout.SourceCell.YPx,
                width, height, PortView(BoardPort.Source, _graph.Goal.Source.Connector)),
            new FixedNode(BoardPort.Sink, _graph.Sink.Port.Label, layout.SinkCell, layout.SinkCell.XPx, layout.SinkCell.YPx,
                width, height, PortView(BoardPort.Sink, _graph.Sink.Port.Connector)),
        ];
    }

    /// <summary>Follows the wires from the source, as the digital test does: every part is a two-ended series element.</summary>
    private BoardRoute BuildRoute()
    {
        if (_graph is null) return BoardRoute.None;
        var lengthM = 0.0;
        var parts = 0;
        var next = _graph.LinkedTo(PortRef.Source);
        while (next is { } entry)
        {
            if (entry.Instance == PartInstanceId.Sink) return new BoardRoute(lengthM, parts, true, _text.Route(lengthM, parts, reachesSink: true));
            // A route can only loop back into itself through more parts than the board holds, so the count ends the trace.
            if (parts >= _cards.Count || !_graph.TryGetPart(entry.Instance, out var placed)) break;
            lengthM += placed.Part.LengthM;
            parts++;
            next = _graph.LinkedTo(new PortRef(entry.Instance, 1 - entry.Port));
        }
        return new BoardRoute(lengthM, parts, false, _text.Route(lengthM, parts, reachesSink: false));
    }

    /// <summary>The task's rate limits against the ground's readings (<c>knowledge.fact.*</c>), never the true attitude.</summary>
    private AssignRequirement[] Requirements(RepairTaskSpec task)
    {
        if (_requirements is null) return [];
        var limits = _requirements.RateLimits(task);
        var result = new AssignRequirement[limits.Count];
        for (var i = 0; i < result.Length; i++)
        {
            var limit = limits[i];
            var prefix = FactPrefix + limit.FactId + ".";
            var value = Number(prefix + "value");
            var at = Number(prefix + "met_s");
            var state = Text(prefix + "state");
            var known = double.IsFinite(value) && state is "known" or "conflicting" or "estimated" or "stale";
            double? magnitude = known ? Math.Abs(value) : null;
            var status = !known ? RequirementStatus.Unknown
                : state == "stale" ? RequirementStatus.Stale
                : magnitude < limit.LimitDps ? RequirementStatus.Met
                : RequirementStatus.NotMet;
            double? atMetS = known && at >= 0 ? at : null;
            result[i] = new AssignRequirement(limit, status, magnitude, atMetS, _text.RateRequirement(limit, status, magnitude, atMetS));
        }
        return result;
    }

    private string RequirementsLine(RepairTaskSpec task, AssignRequirement[] requirements)
    {
        var needs = new string[requirements.Length];
        for (var i = 0; i < needs.Length; i++) needs[i] = requirements[i].Text;
        var uses = new string[task.PartsUsed.Count];
        for (var i = 0; i < uses.Length; i++)
        {
            var used = task.PartsUsed[i];
            var name = _catalog.TryGet(used.Part, out var part) ? part.ShortName : used.Part.Value;
            uses[i] = _text.PartCount(name, used.Count);
        }
        return _text.RequirementsLine(task.Complexity, needs, uses);
    }

    private double Number(string path) =>
        _state.TryGet(path, out var value) && value.Kind == StateValueKind.Number ? value.AsNumber() : double.NaN;

    private string Text(string path) =>
        _state.TryGet(path, out var value) && value.Kind == StateValueKind.Text ? value.AsText() : "";

    private List<BoardWire> BuildWires()
    {
        if (_graph is null) return [];
        var links = _graph.Links();
        var wires = new List<BoardWire>(links.Count);
        foreach (var link in links)
        {
            var a = FromGraph(link.A);
            var b = FromGraph(link.B);
            if (Compare(a, b) > 0) (a, b) = (b, a);
            var (ax, ay) = AnchorOf(a);
            var (bx, by) = AnchorOf(b);
            wires.Add(new BoardWire(a, b, ax, ay, bx, by));
        }
        // By card id rather than graph instance, so a wire keeps its place when undo re-adds a part.
        wires.Sort((x, y) => Compare(x.A, y.A));
        return wires;
    }

    private static int Compare(BoardPort a, BoardPort b)
    {
        var byCard = a.Card.CompareTo(b.Card);
        return byCard != 0 ? byCard : a.Port.CompareTo(b.Port);
    }

    private BoardPortView PortView(BoardPort port, string connector)
    {
        var (x, y) = AnchorOf(port);
        return new BoardPortView(port, connector, _catalog.ConnectorName(connector), x, y, LinkedTo(port) is not null);
    }

    private List<ConnectorChip> Chips(PartDefinition part)
    {
        var chips = new List<ConnectorChip>(part.Connectors.Count);
        foreach (var connector in part.Connectors) chips.Add(new ConnectorChip(connector, _catalog.ConnectorName(connector), connector));
        return chips;
    }

    private static string? LengthText(PartDefinition part) => part.LengthM > 0 ? WorkspaceText.Metres(part.LengthM) : null;

    private static string? RatingText(PartDefinition part) => part.CurrentRatingA > 0 ? WorkspaceText.Amps(part.CurrentRatingA) : null;

    private AssigneeOption ToOption(RepairTaskSpec task, AssigneeEstimate estimate)
    {
        if (!double.IsFinite(estimate.DurationFactor) || estimate.DurationFactor <= 0)
            throw new InvalidOperationException($"Duration factor for '{estimate.Crew}' must be positive, got {estimate.DurationFactor}.");
        if (!double.IsFinite(estimate.SuccessProbability))
            throw new InvalidOperationException($"Success probability for '{estimate.Crew}' is not a number.");
        var durationS = task.DurationS * estimate.DurationFactor;
        var band = estimate.SuccessProbability >= _options.HighLikelihoodAtLeast ? LikelihoodBand.High
            : estimate.SuccessProbability >= _options.MediumLikelihoodAtLeast ? LikelihoodBand.Medium
            : LikelihoodBand.Low;
        return new AssigneeOption(estimate.Crew, estimate.Name, estimate.Available, estimate.Available ? "" : estimate.UnavailableReason,
            durationS, WorkspaceText.Duration(durationS), band, _text.Likelihood(band));
    }

    private void Reset(AssemblyGraph? graph)
    {
        _graph = graph;
        _cards.Clear();
        _undo.Clear();
        _redo.Clear();
        _stockAtTest.Clear();
        _drag = null;
        _dragPreview = null;
        _wire = null;
        _report = null;
        _rows = [];
        _explain = false;
        _testedAtMetS = null;
        _assigned = false;
        Assignment = null;
        _nextCard = 1;
        _revision = 0;
        _testedRevision = -1;
    }

    private Change EndGestures()
    {
        var change = (_drag is null ? Change.None : Change.Drag) | (_wire is null ? Change.None : Change.Wire);
        _drag = null;
        _dragPreview = null;
        _wire = null;
        return change;
    }

    private bool CanEdit(out EditResult refused)
    {
        refused = _graph is null ? Problem(EditProblem.NotOpen, "") : _assigned ? Problem(EditProblem.Locked, "") : EditResult.Ok;
        return refused.Succeeded;
    }

    private EditResult Problem(EditProblem problem, string partName) =>
        new(problem, AssemblyFailure.None, _text.Problem(problem, partName));

    private EditResult Refused(AssemblyFailure failure, string partName) =>
        new(EditProblem.Refused, failure, _text.Failure(failure, partName));

    private void Notify(Change change)
    {
        if ((change & Change.Header) != 0) HeaderChanged?.Invoke(this, EventArgs.Empty);
        if ((change & Change.Tray) != 0) TrayChanged?.Invoke(this, EventArgs.Empty);
        if ((change & Change.Board) != 0) BoardChanged?.Invoke(this, EventArgs.Empty);
        if ((change & Change.Drag) != 0) DragChanged?.Invoke(this, EventArgs.Empty);
        if ((change & Change.Wire) != 0) WireChanged?.Invoke(this, EventArgs.Empty);
        if ((change & Change.Test) != 0) TestChanged?.Invoke(this, EventArgs.Empty);
        if ((change & Change.History) != 0) HistoryChanged?.Invoke(this, EventArgs.Empty);
        var canAssign = CanAssign;
        if (canAssign == _lastCanAssign) return;
        _lastCanAssign = canAssign;
        AssignmentChanged?.Invoke(this, EventArgs.Empty);
    }
}
