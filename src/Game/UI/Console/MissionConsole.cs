using Godot;
using Usa.Game.Bootstrap;
using Usa.Game.Comms;
using Usa.Game.Comms.Hosting;
using Usa.Game.Content;
using Usa.Game.Flow.Tutorial;
using Usa.Game.Input;
using Usa.Game.Integration.Adapters;
using Usa.Game.Integration.Runtime;
using Usa.Game.Integration.Runtime.Hosting;
using Usa.Game.Presentation.Console;
using Usa.Game.Presentation.Console.Crew;
using Usa.Game.Presentation.Console.MainViews;
using Usa.Game.Presentation.Console.Status;
using Usa.Simulation.Flight;
using Usa.Game.Presentation.Views;
using Usa.Game.Presentation.ViewModels;
using Usa.Game.Services.Settings;
using Usa.Game.Services.Voice;
using Usa.Game.UI.Console.Binding;
using Usa.Game.UI.Console.Binding.Comms;
using Usa.Game.UI.Console.Comms;
using Usa.Game.UI.Console.Overlays;
using Usa.Game.UI.Console.Regions;
using Usa.Game.UI.Frontend;
using Usa.Game.UI.Sound;
using Usa.Game.UI.Theme;
using Usa.Game.Presentation.Workspace;
using Usa.Simulation.Commands;

namespace Usa.Game.UI.Console;

/// <summary>
/// The mission console shell (<c>MissionConsole.tscn</c>, Figma G1-03 node 12:242 and compact node 21:807; UI.md 3): header,
/// master alarm band, resource strip, left column (tip card, crew cards, comms), centre column (view tabs, task bar, event
/// log), systems board, the confirmation dialog and captions. It binds view models to controls and forwards input. The
/// front-end shell owns the window: it applies the theme (default or high contrast) and the UI scale with the 720p rule to the
/// whole window, and hosts the Esc menu, encyclopedia sheet and connection overlay above the console; the console only
/// follows the resulting logical width into its full or compact layout and picks the matching palette.
/// </summary>
/// <remarks>
/// Use: the shell instantiates the scene and calls <see cref="Initialize"/> (<see cref="IMissionScene"/>); the console then
/// binds every runtime context the flow host announces (integration bind contract: <c>BindScene</c> for the orbit map, entry
/// HUD, recovery and closing cards; <c>SetExternalViewVisible</c> on tab changes; <c>ExecutePlan</c> for the planner).
/// <see cref="Bind(MissionConsoleViewModel)"/> binds the console view models alone (previews, tests).
/// </remarks>
public sealed partial class MissionConsole : Control, IMissionScene, ITutorialAnchors
{
    private const string PlanConfirmationId = "planner-execute";

    private readonly BindingScope _bindings = new();
    private ColorRect _background = null!;
    private ColorRect _alarmBand = null!;
    private Control _header = null!;
    private MarginContainer _body = null!;
    private Control _shell = null!;
    private Action? _cancelBurnOrder;
    private bool _fitQueued;
    private HBoxContainer _columns = null!;
    private Control _leftColumn = null!;
    private Control _centreColumn = null!;
    private Control _rightColumn = null!;
    private VBoxContainer _crewCards = null!;
    private ConsoleHeader _headerView = null!;
    private ResourceStrip _resourceStrip = null!;
    private TipCard _tipCard = null!;
    private CommsPanel _comms = null!;
    private MainViewTabs _mainView = null!;
    private TaskBar _taskBar = null!;
    private EventLog _eventLog = null!;
    private SystemsBoard _systemsBoard = null!;
    private ConfirmDialog _confirmDialog = null!;
    private CaptionPanel _captions = null!;
    private ConsoleLayoutMetrics _fullLayout = null!;
    private ConsoleLayoutMetrics _compactLayout = null!;
    private ConsoleLayoutMetrics? _layout;
    private ListMirror<CrewCardViewModel, CrewCard>? _crewCardViews;
    private ConsolePalette _palette = ConsolePalette.Default;
    private DisplaySettings _display = new();
    private GameServices? _services;
    private MissionConsoleViewModel? _console;
    private MissionRuntimeContext? _context;
    private ConsoleRuntimeBinding? _runtime;
    private CommsController? _commsController;
    private ITextSource? _strings;
    private IReadOnlyObservable<bool>? _frozen;
    private MicCapture? _mic;
    private CaptionSettings _captionSettings = new();
    private ConsoleLabels? _labels;
    private MissionRuntimeContext? _viewsBoundTo;
    private IDisposable? _sceneBinding;
    private MissionRuntimeContext? _pumped;
    private WorkspaceModel? _workspace;
    private UiSoundHooks? _soundHooks;
    private readonly RailsGate _railsGate = new();
    private Node[] _railsRoots = [];
    private int _railsViewSerial;
    private ulong _firstUseShownMs;

    /// <summary>The External view's first-use tip dismisses itself after this long on screen.</summary>
    private const ulong FirstUseLegendMs = 6000;

    [Export]
    public PackedScene? CrewCardScene { get; set; }

    /// <summary>The live crew feed (<c>scenes/crew/CrewFeed.tscn</c>) put into each crew card.</summary>
    [Export]
    public PackedScene? CrewFeedScene { get; set; }

    public MainViewTabs MainView => _mainView;

    public ConsoleLayoutMetrics? Layout => _layout;

    /// <summary>The header's menu button.</summary>
    public event Action? MenuRequested;

    /// <summary>
    /// The External (3D) view came on screen or left it. The console already tells the bound runtime
    /// (<c>MissionRuntimeContext.SetExternalViewVisible</c>); this is for anyone else who cares.
    /// </summary>
    public event Action<bool>? ExternalViewVisibilityChanged;

    /// <summary>A console or workspace command and the session's answer (for anyone logging or narrating them).</summary>
    public event Action<CommandRequest, ValidationResult>? CommandAnswered;

    public override void _Ready()
    {
        _background = GetNode<ColorRect>("%Background");
        _alarmBand = GetNode<ColorRect>("%AlarmBand");
        _header = GetNode<Control>("%Header");
        _body = GetNode<MarginContainer>("%Body");
        _columns = GetNode<HBoxContainer>("%Columns");
        _leftColumn = GetNode<Control>("%LeftColumn");
        _centreColumn = GetNode<Control>("%CentreColumn");
        _rightColumn = GetNode<Control>("%RightColumn");
        _crewCards = GetNode<VBoxContainer>("%CrewCards");
        _headerView = GetNode<ConsoleHeader>("%Header");
        _resourceStrip = GetNode<ResourceStrip>("%ResourceStrip");
        _tipCard = GetNode<TipCard>("%TipCard");
        _comms = GetNode<CommsPanel>("%CommsPanel");
        _mainView = GetNode<MainViewTabs>("%MainViewTabs");
        _taskBar = GetNode<TaskBar>("%TaskBar");
        _eventLog = GetNode<EventLog>("%EventLog");
        _systemsBoard = GetNode<SystemsBoard>("%SystemsBoard");
        _confirmDialog = GetNode<ConfirmDialog>("%ConfirmDialog");
        _confirmDialog.InputBlocked = IsCovered;
        _captions = _mainView.Captions;
        _railsRoots = [_systemsBoard, _resourceStrip, _eventLog, _taskBar, _tipCard, _comms];

        var tokens = UiTokens.Default;
        _fullLayout = ConsoleLayoutMetrics.Full(
            tokens.LayoutGutterPx, tokens.LayoutHeaderPx, tokens.LayoutResourceStripPx, tokens.LayoutColLeftPx, tokens.LayoutColCentrePx,
            tokens.LayoutColRightPx, tokens.LayoutViewTabsPx, tokens.LayoutMainViewPx, tokens.LayoutTaskBarPx, tokens.LayoutEventLogPx,
            tokens.LayoutTipCardPx, tokens.LayoutCrewCardPx);
        _compactLayout = ConsoleLayoutMetrics.CompactOf(_fullLayout);

        _headerView.MenuRequested += () => MenuRequested?.Invoke();
        _mainView.ViewChanged += OnViewChanged;
        _mainView.Orbit.ExecuteRequested += OnPlanExecuteRequested;
        _mainView.ClosingCard.ProceedRequested += OnProceedRequested;
        _mainView.Workspace.TaskSlotBusy += OnWorkspaceTaskSlotBusy;
        _mainView.Diagram.OpenWorkspaceRequested += _ => OpenWorkspace();
        _mainView.Diagram.InspectRequested += OnInspectRequested;
        _mainView.External.FirstUseLegendDismissed += OnFirstUseLegendDismissed;
        _mainView.External.SetFirstUseLegendVisible(false);
        Resized += ApplyLayout;
        _shell = GetNode<Control>("Shell");
        _shell.SetAnchorsPreset(LayoutPreset.TopLeft);
        Resized += QueueFit;
        _shell.MinimumSizeChanged += QueueFit;
        ApplyPalette();
        ApplyDisplay();
        SetProcess(false);

        if (_context is { } context) BindRuntime(context);
        else if (_console is { } console) BindConsole(console);
    }

    public override void _ExitTree()
    {
        DetachServices();
        UnbindAll();
    }

    // Presentation only: real time for the confirmation countdown and the caption hold.
    public override void _Process(double delta) => _runtime?.Advance(delta);

    public override void _UnhandledInput(InputEvent @event)
    {
        // The dialog is modal and a freeze or overlay covers the console: no hotkey acts behind them (review R3-06).
        if (_console is null || _confirmDialog.Visible || IsCovered() || _frozen?.Value == true) return;
        if (Pressed(@event, InputBindings.AcknowledgeAlarms)) _headerView.AcknowledgeAlarms();
        else if (Pressed(@event, InputBindings.AdvanceEvent)) _headerView.RequestAdvance();
        else if (Pressed(@event, InputBindings.View1)) _mainView.SelectView(0);
        else if (Pressed(@event, InputBindings.View2)) _mainView.SelectView(1);
        else if (Pressed(@event, InputBindings.View3)) _mainView.SelectView(2);
        else if (Pressed(@event, InputBindings.View4)) _mainView.SelectView(3);
        else if (Pressed(@event, InputBindings.FocusVehicle)) FocusVehicle();
        else if (Pressed(@event, InputBindings.Search)) FocusSearch();
        else if (Pressed(@event, InputBindings.FocusComms)) _comms.FocusInput();
        else return;
        GetViewport().SetInputAsHandled();
    }

    /// <summary><see cref="IMissionScene"/>: the front-end shell hands over the services once it has added the scene.</summary>
    public void Initialize(GameServices services) => AttachServices(services);

    /// <summary>
    /// Connects the console to the game: binds every runtime context the flow host announces (and the current one), follows
    /// the display and caption settings, the app freeze, and the push-to-talk microphone.
    /// </summary>
    public void AttachServices(GameServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (ReferenceEquals(_services, services)) return;
        DetachServices();
        _services = services;
        _soundHooks ??= UiSoundHooks.Attach(this, services.Audio);
        services.Flow.ContextChanged += OnContextChanged;
        services.Settings.Changed += OnSettingsChanged;
        ApplySettings(services.Settings.Current);
        OnContextChanged(services.Flow.Context);
    }

    /// <summary>Binds one mission's runtime; null unbinds.</summary>
    /// <param name="strings">The fixed labels and the confirmation effect texts; without it the scenes' design text and the
    /// effect keys are shown.</param>
    /// <param name="frozen">The app freeze; null when the game never freezes.</param>
    /// <param name="mic">Push-to-talk capture for the PTT button.</param>
    public void BindContext(MissionRuntimeContext? context, ITextSource? strings = null, IReadOnlyObservable<bool>? frozen = null, MicCapture? mic = null)
    {
        UnbindAll();
        _context = context;
        _railsViewSerial = context?.Rails?.ViewRequestSerial ?? 0;
        _console = context?.Console;
        _strings = strings ?? context?.Strings;
        _frozen = frozen;
        _mic = mic;
        if (context is not null && IsNodeReady()) BindRuntime(context);
    }

    /// <summary>Binds the console view models alone, without comms, dialog, captions or the mission views.</summary>
    public void Bind(MissionConsoleViewModel console)
    {
        ArgumentNullException.ThrowIfNull(console);
        UnbindAll();
        _console = console;
        if (IsNodeReady()) BindConsole(console);
    }

    /// <summary>High contrast (the palette the views colour with), reduced motion, captions and the layout.</summary>
    public void ApplySettings(GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var themeChanged = settings.Display.HighContrast != _display.HighContrast;
        _display = settings.Display;
        _captionSettings = settings.Captions;
        if (!IsNodeReady()) return;
        ApplyDisplay();
        if (!themeChanged) return;
        ApplyPalette();
        Repalette();
    }

    /// <summary>
    /// Opens the engineering workspace in the Workspace tab (UI.md 4.6) and shows it; a workspace already open stays. Needs a
    /// bound runtime.
    /// </summary>
    /// <returns>False without a runtime.</returns>
    public bool OpenWorkspace()
    {
        if (_context is not { } context || MainViewTabs.IsCut((int)MainViewKind.Workspace)) return false;
        if (_workspace is null)
        {
            _workspace = context.Workspaces.Create();
            _mainView.Workspace.Bind(_workspace);
            RefreshWorkspaceTaskSlot();
        }
        _mainView.SetWorkspaceAvailable(true);
        _mainView.SelectView((int)MainViewKind.Workspace);
        return true;
    }

    /// <summary>Closes the workspace (its tab greys out and the Diagram returns) and releases its model.</summary>
    public void CloseWorkspace()
    {
        if (_workspace is { } model)
        {
            if (IsNodeReady()) _mainView.Workspace.Unbind();
            if (_context is { IsDisposed: false } context) context.Workspaces.Release(model);
            _workspace = null;
        }
        if (IsNodeReady()) _mainView.SetWorkspaceAvailable(false);
    }

    /// <summary><see cref="ITutorialAnchors.TryGetAvoidRect"/>: the External view's canvas rectangle while it is the shown main view.</summary>
    public LayoutRect? TryGetAvoidRect()
    {
        if (!IsNodeReady() || (MainViewKind)_mainView.SelectedView != MainViewKind.External || !_mainView.External.IsVisibleInTree()) return null;
        var rect = CanvasRect(_mainView.External);
        return rect.Size.X > 0 && rect.Size.Y > 0 ? new LayoutRect(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y) : null;
    }

    /// <summary><see cref="ITutorialAnchors"/>: the global rectangle of a guided-first-run anchor; null when not on screen.</summary>
    public LayoutRect? Find(string anchorId) => Resolve(anchorId)?.Rect;

    /// <summary>
    /// <see cref="ITutorialAnchors.Resolve"/>: the anchor's on-screen rectangle in canvas pixels (after the scale-to-fit),
    /// clipped to any scroll area it sits in; while it is in a hidden main-view tab or the closed planner drawer, the tab or
    /// planner button to press first.
    /// </summary>
    public TutorialAnchorHit? Resolve(string anchorId)
    {
        if (!IsNodeReady()) return null;
        if (!IsInGroup(TutorialAnchorIds.Group)) AddToGroup(TutorialAnchorIds.Group);
        switch (anchorId)
        {
            case TutorialAnchorIds.OrbitMap:
                return _mainView.Orbit.IsVisibleInTree() ? Hit(_mainView.Orbit) : OrbitTabHit();
            case TutorialAnchorIds.PlannerEngine:
                return PlannerHit("EngineSelect");
            case TutorialAnchorIds.PlannerGoal:
                return PlannerHit("GoalSelect");
            case TutorialAnchorIds.PlannerPropose:
                return PlannerHit("ProposeButton");
            case TutorialAnchorIds.PlannerExecute:
                return PlannerHit("ExecuteButton");
            case TutorialAnchorIds.ConfirmDialog:
                if (VisibleNamed(_confirmDialog, "Confirm") is { } confirm) return Hit(confirm);
                // Cancelled or timed out with no burn order held: point back at Execute, which opens it again.
                if (_context?.BurnOrders.Pending is not null) return null;
                return PlannerHit("ExecuteButton") is { } execute ? execute with { IsTarget = false } : null;
            case TutorialAnchorIds.RecoveryCard:
                return _mainView.RecoveryCard.IsVisibleInTree() ? Hit(_mainView.RecoveryCard) : null;
            case TutorialAnchorIds.ProceedDebrief:
                return (VisibleNamed(_mainView.RecoveryCard, "ProceedButton") ?? VisibleNamed(_mainView.ClosingCard, "Proceed")) is { } proceed
                    ? Hit(proceed)
                    : null;
            case TutorialAnchorIds.AlarmAck:
                if ((VisibleNamed(_systemsBoard, "Ack") ?? VisibleNamed(_eventLog, "Ack")) is { } ack) return Hit(ack);
                // The roll takes minutes to raise the first alarm after the short; until then the guide points at the master
                // alarm (where it will sound) instead of hiding while it holds time at 1x.
                return _header.GetNodeOrNull<Control>("%MasterAlarm") is { } master && master.IsVisibleInTree()
                    ? Hit(master) with { IsTarget = false }
                    : null;
        }
        var target = anchorId switch
        {
            TutorialAnchorIds.CommsInput => _comms.InputRow,
            TutorialAnchorIds.CommsQuick => VisibleNamed(_comms, "QuickCommands") ?? _comms.InputRow,
            TutorialAnchorIds.ReadoutAsk => VisibleNamed(_systemsBoard, "Ask") ?? _systemsBoard,
            TutorialAnchorIds.FactSourceStamp => _systemsBoard.FirstVisibleSourceStamp(),
            TutorialAnchorIds.TaskBar => _taskBar,
            TutorialAnchorIds.ResourceStrip => _resourceStrip,
            TutorialAnchorIds.SystemsPower => _systemsBoard.PowerAnchor(),
            _ => null,
        };
        return target is not null && target.IsVisibleInTree() ? Hit(target) : null;
    }

    /// <summary><see cref="ITutorialAnchors.StateOf"/>: the planner dropdown's selected item (visible or not), or whether a burn order is held.</summary>
    public string? StateOf(string anchorId)
    {
        if (!IsNodeReady()) return null;
        if (anchorId == TutorialAnchorIds.ConfirmDialog) return _context?.BurnOrders.Pending is null ? "none" : "held";
        var name = anchorId switch
        {
            TutorialAnchorIds.PlannerEngine => "EngineSelect",
            TutorialAnchorIds.PlannerGoal => "GoalSelect",
            _ => null,
        };
        if (name is null) return null;
        if (!_tutorialTargets.TryGetValue("select/" + name, out var cached) || !IsInstanceValid(cached))
        {
            if (_mainView.Orbit.FindChild(name, recursive: true, owned: false) is not OptionButton found) return null;
            _tutorialTargets["select/" + name] = cached = found;
        }
        return cached is OptionButton option ? option.Selected.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    private readonly Dictionary<string, Control> _tutorialTargets = new(StringComparer.Ordinal);

    /// <summary><see cref="ITutorialAnchors.IsChosen"/>: the planner's engine or goal is the railed step's choice.</summary>
    public bool IsChosen(string anchorId, string item) => anchorId switch
    {
        TutorialAnchorIds.PlannerEngine => _context?.Planner.SelectedEngine?.Id == item,
        TutorialAnchorIds.PlannerGoal => IsNodeReady() && _mainView.Orbit.PlannerSelectedGoal is TargetZone zone && zone.ZoneId == item,
        _ => false,
    };

    // The tutorial rails (guided first run): one gate over every actionable control, refreshed with the 10 Hz pump.
    private void ApplyRails(Usa.Simulation.Guidance.ITutorialRails? rails)
    {
        _railsGate.Apply(_railsRoots, rails);
        _mainView.Orbit.ApplyRails(rails);
    }

    private const string TutorialOrderConfirmationId = "tutorial-order";

    /// <summary>
    /// <see cref="ITutorialAnchors.SendOrder"/>: the callout's suggested order goes out through the bridge as the structured
    /// request it names (the path an accepted pre-fill takes, so the AI never interprets it); an order that needs confirmation
    /// opens the usual dialog and is sent confirmed from there.
    /// </summary>
    public bool SendOrder(Usa.Simulation.Guidance.TutorialOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (_context is not { } context || _runtime is not { } runtime) return false;
        var draft = new CommandDraft(order.Action, CommandSource.QuickCommand, order.Crew, order.Args);
        var result = context.Bridge.Send(draft);
        if (result.Status != ValidationStatus.NeedsConfirmation) return result.IsValid;

        var title = context.CommsAdapter.DescribeCommand(draft.ToRequest(new Usa.Simulation.Kernel.CommandId(TutorialOrderConfirmationId)));
        var prompt = new ConfirmPrompt(
            ConfirmPromptKind.Command,
            string.IsNullOrWhiteSpace(title) ? order.Text : title,
            ConfirmationCoordinator.ResolveEffects([], result.Impact?.EffectTextKeys ?? [], _strings),
            Irreversible: result.Impact?.Impact == Usa.Simulation.Commands.Catalog.ActionImpact.Irreversible);
        runtime.Confirmations.ShowExternal(
            prompt, TutorialOrderConfirmationId, () => context.Bridge.Send(draft with { Confirmed = true }), static () => { }, ownsTimeout: false);
        return true;
    }

    // Orbit tab first, then the planner drawer button, then the planner control itself.
    private TutorialAnchorHit? PlannerHit(string nodeName)
    {
        if (!_mainView.Orbit.IsVisibleInTree()) return OrbitTabHit();
        if (VisibleNamed(_mainView.Orbit, nodeName) is { } target) return Hit(target);
        return VisibleNamed(_mainView.Orbit, "PlannerToggle") is { } toggle ? Hit(toggle) with { IsTarget = false } : null;
    }

    private TutorialAnchorHit? OrbitTabHit()
    {
        if (!_mainView.IsVisibleInTree() || _mainView.GetNodeOrNull<TabBar>("%Tabs") is not { } tabs) return null;
        var xform = tabs.GetGlobalTransformWithCanvas();
        var tab = tabs.GetTabRect((int)MainViewKind.Orbit);
        var position = xform * tab.Position;
        var size = tab.Size * xform.Scale;
        return new TutorialAnchorHit(new LayoutRect(position.X, position.Y, size.X, size.Y), false);
    }

    // The first visible descendant with that node name (cached while it stays visible: this runs every frame).
    private Control? VisibleNamed(Node root, string nodeName)
    {
        var key = root.GetInstanceId() + "/" + nodeName;
        if (_tutorialTargets.TryGetValue(key, out var cached) && IsInstanceValid(cached) && cached.IsVisibleInTree()) return cached;
        _tutorialTargets.Remove(key);
        if (FirstVisible(root, nodeName) is not { } found) return null;
        _tutorialTargets[key] = found;
        return found;
    }

    private static Control? FirstVisible(Node node, string nodeName)
    {
        if (node is CanvasItem { Visible: false }) return null;
        // A spent Ask or ACK stays on screen disabled; the guide wants the next one the player can press.
        if (node is Control control && control.Name == nodeName && control is not BaseButton { Disabled: true }) return control;
        for (var i = 0; i < node.GetChildCount(); i++)
        {
            if (FirstVisible(node.GetChild(i), nodeName) is { } found) return found;
        }
        return null;
    }

    private static TutorialAnchorHit Hit(Control control)
    {
        var rect = CanvasRect(control);
        for (var parent = control.GetParent(); parent is not null; parent = parent.GetParent())
        {
            if (parent is not ScrollContainer scroll) continue;
            var clip = CanvasRect(scroll);
            rect = rect.Intersects(clip) ? rect.Intersection(clip) : clip;
        }
        return new TutorialAnchorHit(new LayoutRect(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y), true);
    }

    private static Rect2 CanvasRect(Control control)
    {
        var xform = control.GetGlobalTransformWithCanvas();
        return new Rect2(xform.Origin, control.Size * xform.Scale);
    }

    private void BindRuntime(MissionRuntimeContext context)
    {
        _labels = _strings is { } strings ? new ConsoleLabels(strings) : null;
        BindConsole(context.Console);
        BindViews(context);
        var runtime = new ConsoleRuntimeBinding(context, _frozen, _strings);
        _runtime = runtime;
        _bindings.Add(runtime);
        runtime.PrefillRequested += _comms.ApplyPrefill;
        runtime.CommandAnswered += OnCommandAnswered;
        _confirmDialog.Bind(runtime.Confirmations, _palette);
        _captions.Bind(runtime.Captions, _palette);
        _bindings.Observe(runtime.Confirmations.Prompt, prompt => OnPromptChanged(prompt is not null));
        SetProcess(true);

        if (context.CommsHost is CommsControllerHost { Controller: var controller })
        {
            _commsController = controller;
            _comms.Bind(controller, context.Console.Crew, _palette, _mic, _labels, draft => context.Bridge.Send(draft));
            controller.ConfirmationRequested += OnCommsConfirmationRequested;
            controller.ConfirmationClosed += OnCommsConfirmationClosed;
            controller.CaptionStarted += OnCaptionStarted;
        }

        _mainView.External.EntryHud.SetLimits(context.EntryHudSource.Limits);
        if (_strings is { } text)
        {
            _mainView.External.EntryHud.ApplyText(text);
            _mainView.RecoveryCard.ApplyText(text);
            _mainView.Workspace.ApplyText(text);
        }
        _sceneBinding = context.BindScene(new MissionSceneViews(
            Map: _mainView.Orbit, EntryHud: _mainView.External.EntryHud, RecoveryCard: _mainView.RecoveryCard, ShowClosing: ShowClosing));
        // Clearing the plan also drops a burn order still holding for its mark, so nothing stays armed.
        _cancelBurnOrder = context.CancelBurnOrder;
        _mainView.Orbit.ClearRequested += _cancelBurnOrder;
        _bindings.Add(context.Console.TaskSlot.HasTask.Subscribe(_ => RefreshWorkspaceTaskSlot()));
        _mainView.Diagram.Bind(context.Diagram, _palette);
        context.Pumped += OnPumped;
        _pumped = context;
        context.Console.MainView.SetWorkspaceOpenable(true);
        _bindings.On(context.Console.MainView.WorkspaceRequested, _ => OpenWorkspace());
        context.SetExternalViewVisible(context.Console.MainView.ExternalVisible.Value);
    }

    private void BindConsole(MissionConsoleViewModel console)
    {
        if (_labels is { } labels) ApplyLabels(labels);
        _headerView.Bind(console.Header, console.Status, _palette);
        _bindings.Observe(console.Header.MasterAlarm, indicator => _alarmBand.Visible = indicator.CriticalBand);
        _resourceStrip.Bind(console.Resources, _palette);
        _tipCard.Bind(console.Tip);
        _taskBar.Bind(console.TaskSlot);
        _eventLog.Bind(console.Log, console.Alarms, _palette);
        _systemsBoard.Bind(console.Systems, console.Alarms, console.Status, _palette);
        _mainView.Bind(console.MainView);
        _bindings.Observe(console.MainView.ExternalVisible, visible => ExternalViewVisibilityChanged?.Invoke(visible));
        if (CrewCardScene is { } cardScene)
        {
            var palette = _palette;
            _crewCardViews = _bindings.Mirror(console.Crew.Cards, new ListBinder<CrewCardViewModel, CrewCard>(_crewCards, cardScene, (view, card) =>
            {
                view.Bind(card, palette);
                if (_layout is { } layout) view.ApplyLayout(layout);
                if (_labels is { } cardLabels) view.ApplyLabels(cardLabels);
                if (_context is { } context) AttachCrewFeed(context, view, card);
            }));
        }
    }

    private void ApplyLabels(ConsoleLabels labels)
    {
        _headerView.ApplyLabels(labels);
        _tipCard.ApplyLabels(labels);
        _resourceStrip.ApplyLabels(labels);
        _taskBar.ApplyLabels(labels);
        _eventLog.ApplyLabels(labels);
        _systemsBoard.ApplyLabels(labels);
        _confirmDialog.ApplyLabels(labels);
        _mainView.External.ApplyLabels(labels);
        _mainView.Diagram.ApplyLabels(labels);
    }

    // The 3D view binds once per context: the context owns it and releases it when the mission stops.
    private void BindViews(MissionRuntimeContext context)
    {
        if (ReferenceEquals(_viewsBoundTo, context)) return;
        _viewsBoundTo = context;
        context.Bind(_mainView.External.View, _mainView.External.Backdrop, _services?.Settings);
    }

    private void AttachCrewFeed(MissionRuntimeContext context, CrewCard view, CrewCardViewModel card)
    {
        if (CrewFeedScene is null || view.Feed is { } existing && existing.CrewId == card.Id.Value) return;
        var feed = CrewFeedScene.Instantiate<CrewFeedView>();
        // The feed picks its crew model by id when it enters the tree.
        feed.CrewId = card.Id.Value;
        view.AttachFeed(feed);
        context.BindCrewFeed(card.Id, feed.Animator);
    }

    private void UnbindAll()
    {
        SetProcess(false);
        if (IsNodeReady()) ApplyRails(null);
        OnPromptChanged(false);
        CloseWorkspace();
        _sceneBinding?.Dispose();
        _sceneBinding = null;
        if (_cancelBurnOrder is { } cancel && IsNodeReady())
        {
            _mainView.Orbit.ClearRequested -= cancel;
            _mainView.Orbit.ShowHeldOrder(null);
        }
        _cancelBurnOrder = null;
        if (_pumped is { } pumped)
        {
            pumped.Pumped -= OnPumped;
            _pumped = null;
        }
        if (IsNodeReady()) _mainView.Diagram.Unbind();
        if (_commsController is { } controller)
        {
            controller.ConfirmationRequested -= OnCommsConfirmationRequested;
            controller.ConfirmationClosed -= OnCommsConfirmationClosed;
            controller.CaptionStarted -= OnCaptionStarted;
            _commsController = null;
        }
        if (_runtime is { } runtime)
        {
            runtime.PrefillRequested -= _comms.ApplyPrefill;
            runtime.CommandAnswered -= OnCommandAnswered;
            _runtime = null;
        }
        _bindings.Release();
        _crewCardViews = null;
        if (!IsNodeReady()) return;
        _headerView.Unbind();
        _resourceStrip.Unbind();
        _tipCard.Unbind();
        _taskBar.Unbind();
        _eventLog.Unbind();
        _systemsBoard.Unbind();
        _comms.Unbind();
        _confirmDialog.Unbind();
        _captions.Unbind();
        _mainView.Unbind();
        _mainView.ClosingCard.Visible = false;
        _alarmBand.Visible = false;
    }

    // High contrast toggled: the views take the new palette in place. The runtime binding, its open confirmation, the comms
    // exchange and the crew feeds stay as they are (review R3-08).
    private void Repalette()
    {
        if (!IsNodeReady() || _console is not { } console) return;
        var palette = _palette;
        _headerView.Bind(console.Header, console.Status, palette);
        _resourceStrip.Bind(console.Resources, palette);
        _eventLog.Bind(console.Log, console.Alarms, palette);
        _systemsBoard.Bind(console.Systems, console.Alarms, console.Status, palette);
        if (_crewCardViews is { } cards)
        {
            for (var i = 0; i < cards.Views.Count && i < console.Crew.Cards.Count; i++) cards.Views[i].Bind(console.Crew.Cards[i], palette);
        }
        if (_runtime is { } runtime)
        {
            _confirmDialog.Bind(runtime.Confirmations, palette);
            _captions.Bind(runtime.Captions, palette);
        }
    }

    private void OnPromptChanged(bool open)
    {
        // Esc, F1 and the header menu must not act on or over a live prompt (review R3-05).
        _services?.Router.SetMissionDialogOpen(open);
        if (IsNodeReady()) _headerView.SetMenuEnabled(!open);
    }

    private bool IsCovered() => _services?.Router is { } router && (router.EscMenuVisible || router.ConnectionOverlayVisible);

    private void Rebind()
    {
        if (!IsNodeReady()) return;
        if (_context is { } context) BindContext(context, _strings, _frozen, _mic);
        else if (_console is { } console) Bind(console);
    }

    private void DetachServices()
    {
        if (_services is not { } services) return;
        services.Flow.ContextChanged -= OnContextChanged;
        services.Settings.Changed -= OnSettingsChanged;
        _services = null;
    }

    private void OnContextChanged(MissionRuntimeContext? context)
    {
        if (_services is not { } services) return;
        var strings = context is null ? null : services.Content.Get(context.Session.MissionId).Strings;
        BindContext(context, strings, services.Freeze.Frozen, services.Mic);
    }

    private void OnSettingsChanged(SettingsChange change) => ApplySettings(change.Current);

    private void OnCommandAnswered(CommandRequest request, ValidationResult result) => CommandAnswered?.Invoke(request, result);

    private void OnViewChanged(int view)
    {
        var external = view == MainViewTabs.ExternalViewIndex;
        if (external && !FirstUseFlags.Seen(FirstUseFlags.ExternalLegend)) _mainView.External.SetFirstUseLegendVisible(true);
        _context?.SetExternalViewVisible(external);
        // Without a bound view model nothing else reports the change.
        if (_console is null) ExternalViewVisibilityChanged?.Invoke(external);
    }

    private void OnCommsConfirmationRequested(ConfirmationRequest request)
    {
        if (_runtime is null || _commsController is not { } controller) return;
        var prompt = _strings is { } strings
            ? CommsConfirmations.ToPrompt(request, strings)
            : new ConfirmPrompt(ConfirmPromptKind.Command, request.Title, request.EffectTextKeys, request.Irreversible, request.ExecutorName);
        var id = request.Id;
        _runtime.Confirmations.ShowExternal(
            prompt, id, () => controller.Confirm(id), () => controller.CancelConfirmation(id), ownsTimeout: true, controller.TouchConfirmation);
    }

    private void OnCommsConfirmationClosed(ConfirmationClosed closed) => _runtime?.Confirmations.CloseExternal(closed.Request.Id);

    private void OnCaptionStarted(CaptionCue cue) => _runtime?.Captions.Show(cue.Line.SpeakerName, cue.Line.Caption, cue.HoldSeconds);

    // UI.md 4.1: Execute gives the crew order or remote command the engine needs, after the high-impact confirmation.
    private void OnPlanExecuteRequested(ManeuverPlan plan)
    {
        if (_context is not { } context || _runtime is not { } runtime) return;
        var execution = context.ExecutePlan(plan);
        if (execution is not { Status: PlanExecutionStatus.NeedsConfirmation, Order: { } order }) return;
        var definition = order.Action.Definition;
        var prompt = new ConfirmPrompt(
            ConfirmPromptKind.Command,
            PlanTitle(context, order),
            ConfirmationCoordinator.ResolveEffects([], definition.EffectTextKeys ?? [], _strings),
            Irreversible: order.Action.BaseImpact == Usa.Simulation.Commands.Catalog.ActionImpact.Irreversible);
        runtime.Confirmations.ShowExternal(
            prompt, PlanConfirmationId, () => context.ExecutePlan(plan, confirmed: true), static () => { }, ownsTimeout: false);
    }

    // The 10 Hz pump: the diagram redraws only when the knowledge behind it changed.
    private void OnPumped()
    {
        ApplyRails(_context?.Rails);
        FollowRailsView(_context?.Rails);
        ExpireFirstUseLegend();
        // After splashdown the entry HUD is gone, but the pod is still what the player is watching.
        _mainView.External.SetOverlaysSuppressed(_mainView.RecoveryCard.IsVisibleInTree());
        _mainView.Diagram.Refresh();
        _mainView.Orbit.ShowHeldOrder(_context?.BurnOrders.Pending?.OrderAt);
    }

    private void OnInspectRequested(Usa.Simulation.Kernel.ComponentId component, string name)
    {
        var text = _labels?.DiagramInspectPrefill(name) ?? name;
        _comms.ApplyPrefill(new CommsPrefill(text, Component: component));
    }

    // The guided run shows what a step is about (the docking approach, the chutes, the planner): each request once, so the player
    // may switch away again. Showing External this way also retires its first-use tip, which would cover the moment.
    private void FollowRailsView(Usa.Simulation.Session.TutorialRails? rails)
    {
        if (rails is null || rails.ViewRequestSerial == _railsViewSerial) return;
        _railsViewSerial = rails.ViewRequestSerial;
        var view = rails.RequestedView switch
        {
            "external" => MainViewKind.External,
            "orbit" => MainViewKind.Orbit,
            _ => (MainViewKind?)null,
        };
        if (view is not { } kind) return;
        if ((MainViewKind)_mainView.SelectedView != kind) _mainView.SelectView((int)kind);
        if (kind == MainViewKind.External && _mainView.External.IsFirstUseLegendWanted) _mainView.External.DismissFirstUseLegend();
    }

    private void ExpireFirstUseLegend()
    {
        if (!_mainView.External.IsFirstUseLegendShowing)
        {
            _firstUseShownMs = 0;
            return;
        }
        var nowMs = Time.GetTicksMsec();
        if (_firstUseShownMs == 0) _firstUseShownMs = nowMs;
        else if (nowMs - _firstUseShownMs >= FirstUseLegendMs) _mainView.External.DismissFirstUseLegend();
    }

    private void OnFirstUseLegendDismissed() => FirstUseFlags.MarkSeen(FirstUseFlags.ExternalLegend);

    // The dialog names the exact order ("Lodestar burn 12 s"), worded like the comms confirmations (CommandText).
    private static string PlanTitle(MissionRuntimeContext context, BurnOrder order)
    {
        var source = order.Kind == BurnOrderKind.CrewOrder ? CommandSource.QuickCommand : CommandSource.RemoteControl;
        var request = new CommandRequest(new Usa.Simulation.Kernel.CommandId(PlanConfirmationId), source, order.Action.Id, null, order.Args, Confirmed: false);
        var title = context.CommsAdapter.DescribeCommand(request);
        return string.IsNullOrWhiteSpace(title) ? order.Action.Definition.Description.TrimEnd('.') : title;
    }

    private void ShowClosing(MissionClosingCard card) => _mainView.ClosingCard.Show(card);

    private void OnProceedRequested() => _context?.ProceedToDebrief();

    private void OnWorkspaceTaskSlotBusy(string reason) => _console?.TaskSlot.ReportCrewBusy();

    private void RefreshWorkspaceTaskSlot()
    {
        if (_workspace is null || _console is not { } console) return;
        var slot = console.TaskSlot;
        _mainView.Workspace.SetTaskSlot(!slot.HasTask.Value, slot.HasTask.Value ? slot.TaskName.Value : null);
    }

    private void FocusVehicle()
    {
        switch ((MainViewKind)_mainView.SelectedView)
        {
            case MainViewKind.Orbit:
                _mainView.Orbit.FocusVehicle();
                break;
            case MainViewKind.External:
                _mainView.External.FocusVehicle();
                break;
        }
    }

    private void FocusSearch()
    {
        // Ctrl+F searches the panel that has focus; the transcript otherwise (UI.md 3.5, 3.8).
        if (_eventLog.IsAncestorOf(GetViewport().GuiGetFocusOwner())) _eventLog.FocusSearch();
        else _comms.FocusSearch();
    }

    private void ApplyDisplay()
    {
        _headerView.ReducedMotion = _display.ReducedMotion;
        _captions.ApplySettings(_captionSettings);
        ApplyLayout();
    }

    // The shell sets the window theme; the console keeps the palette its views colour with in step.
    private void ApplyPalette()
    {
        _palette = _display.HighContrast ? ConsolePalette.HighContrast : ConsolePalette.Default;
        _background.Color = _palette.Tokens.ColorBg0;
        _alarmBand.Color = _palette.Of(ColourTokens.Critical);
        _mainView.Orbit.SetPalette(_palette);
        _mainView.External.SetPalette(_palette);
        _mainView.RecoveryCard.SetPalette(_palette);
        _mainView.Workspace.SetPalette(_palette);
        _mainView.Diagram.SetPalette(_palette);
    }

    private void QueueFit()
    {
        if (_fitQueued) return;
        _fitQueued = true;
        Callable.From(FitShell).CallDeferred();
    }

    /// <summary>
    /// Keeps the whole console on screen: when the live content's minimum size is larger than the console (long readouts,
    /// many tiles, a small window), the shell is laid out at its minimum size and scaled down to fit instead of spilling
    /// past the window edges. At or above the minimum it fills the console at scale 1.
    /// </summary>
    private void FitShell()
    {
        _fitQueued = false;
        if (!IsInstanceValid(_shell)) return;
        var available = Size;
        var min = _shell.GetCombinedMinimumSize();
        if (available.X <= 0 || available.Y <= 0 || min.X <= 0 || min.Y <= 0) return;
        var scale = Math.Min(1f, Math.Min(available.X / min.X, available.Y / min.Y));
        _shell.Scale = new Vector2(scale, scale);
        _shell.Position = Vector2.Zero;
        _shell.Size = available / scale;
    }

    private void ApplyLayout()
    {
        if (!IsNodeReady()) return;
        var layout = ConsoleLayoutMetrics.Select((int)GetViewportRect().Size.X, _fullLayout, _compactLayout);
        if (layout == _layout) return;
        _layout = layout;

        _header.CustomMinimumSize = new Vector2(0, layout.HeaderPx);
        foreach (var side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" })
            _body.AddThemeConstantOverride(side, layout.GutterPx);
        _columns.AddThemeConstantOverride("separation", layout.GutterPx);
        _leftColumn.CustomMinimumSize = new Vector2(layout.LeftColumnPx, 0);
        _centreColumn.CustomMinimumSize = new Vector2(layout.CentreColumnPx, 0);
        _rightColumn.CustomMinimumSize = new Vector2(layout.RightColumnPx, 0);
        _headerView.ApplyLayout(layout);
        _resourceStrip.ApplyLayout(layout);
        _tipCard.ApplyLayout(layout);
        _comms.ApplyLayout(layout);
        _mainView.ApplyLayout(layout);
        _taskBar.ApplyLayout(layout);
        _eventLog.ApplyLayout(layout);
        _systemsBoard.ApplyLayout(layout);
        _confirmDialog.ApplyLayout(layout);
        if (_crewCardViews is not null)
        {
            foreach (var card in _crewCardViews.Views) card.ApplyLayout(layout);
        }
    }

    private static bool Pressed(InputEvent input, StringName action) =>
        InputMap.HasAction(action) && input.IsActionPressed(action, exactMatch: true);
}
