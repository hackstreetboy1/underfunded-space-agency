using Godot;
using Usa.Game.Bootstrap;
using Usa.Game.Flow;
using Usa.Game.Flow.Screens;
using Usa.Game.Flow.Tutorial;
using Usa.Game.Input;
using Usa.Game.Integration.Runtime;
using Usa.Game.Services.Settings;
using Usa.Game.UI.Console;
using Usa.Game.UI.Frontend.Model;
using Usa.Simulation.Guidance;

namespace Usa.Game.UI.Frontend;

/// <summary>
/// Root of <c>Main.tscn</c>: shows one front-end scene per <see cref="ScreenRouter.Current"/> (UI.md 5), keeps the mission
/// scene alive while a run is flying (hidden and frozen behind Settings opened from the Esc menu), and hosts the
/// in-mission overlays: the encyclopedia sheet (sim running, anchored left of the centre column) and the Esc menu
/// (layer <see cref="EscMenuLayer"/>; the console's connection overlay must sit above it). Applies the theme and UI scale,
/// and routes Esc and F1. During a launch's scene stage it loads the console on a worker and instances it hidden behind the
/// Loading screen (<see cref="LoadProgress"/>). <see cref="GameRoot"/> calls <see cref="Initialize"/> once the services exist.
/// </summary>
public sealed partial class FrontendShell : Control
{
    public const int SheetLayer = 20;
    public const int EscMenuLayer = 50;
    private const string Category = "Frontend";

    private ColorRect _background = null!;
    private Control _missionHost = null!;
    private Control _screenHost = null!;
    private Control _sheetHost = null!;
    private Control _escHost = null!;
    private Control _calloutHost = null!;
    private Control _freezeHost = null!;
    private ConnectionOverlayView? _connectionOverlay;
    private FrontendContext? _context;
    private Node? _screen;
    private Screen? _screenShown;
    private Node? _mission;
    private EscMenuView? _escMenu;
    private EncyclopediaSheetView? _sheet;
    private EncyclopediaSheetModel? _sheetModel;
    private IDisposable? _advisorBinding;
    private TutorialCalloutView? _callouts;
    private TutorialCalloutFeed? _tutorial;
    private MissionRuntimeContext? _tutorialRuntime;
    private IDisposable[] _tutorialEvents = [];
    private string? _preloadPath;
    private bool _preloadHeld;
    private PackedScene? _preloaded;
    private Resource? _threadLoadHold;

    private const string ShaderIncludePath = "res://assets/shaders/usa_palette_common.gdshaderinc";

    /// <summary>False when the console's overlay host shows the Esc menu itself (then it instantiates <see cref="FrontendRoutes.EscMenu"/>).</summary>
    [Export]
    public bool HostsEscMenu { get; set; } = true;

    [Export]
    public string MissionScenePath { get; set; } = FrontendRoutes.MissionConsole;

    public FrontendContext? Context => _context;

    public override void _Ready()
    {
        _background = GetNode<ColorRect>("%Background");
        _missionHost = GetNode<Control>("%MissionHost");
        _screenHost = GetNode<Control>("%ScreenHost");
        _sheetHost = GetNode<Control>("%SheetHost");
        _escHost = GetNode<Control>("%EscHost");
        _calloutHost = GetNode<Control>("%CalloutHost");
        _freezeHost = GetNode<Control>("%FreezeHost");
        SetProcess(false);
    }

    // Polls the threaded load of the mission scene during the Loading screen's scene stage (presentation glue only).
    public override void _Process(double delta)
    {
        if (_preloadPath is not { } path || _context is not { } context)
        {
            SetProcess(false);
            return;
        }
        var progress = context.Services.Flow.LoadProgress;
        using var loaded = new Godot.Collections.Array();
        var status = ResourceLoader.LoadThreadedGetStatus(path, loaded);
        if (status == ResourceLoader.ThreadLoadStatus.InProgress)
        {
            if (_preloadHeld && loaded.Count > 0) progress.ReportScene(loaded[0].AsDouble());
            return;
        }

        _preloadPath = null;
        SetProcess(false);
        _threadLoadHold = null;
        if (status == ResourceLoader.ThreadLoadStatus.Loaded) _preloaded = ResourceLoader.LoadThreadedGet(path) as PackedScene;
        else context.Log.Warn(Category, $"{path} did not load in the background ({status}); it loads when the mission starts");
        if (!_preloadHeld) return;

        // Instanced now, hidden behind the Loading screen, so the router shows a ready console.
        if (context.Router.Current == Screen.Loading) EnsureMissionScene(context);
        _preloadHeld = false;
        progress.ReleaseScene();
    }

    public override void _ExitTree()
    {
        _threadLoadHold = null;
        if (_context is not { } context) return;
        context.Router.ScreenChanged -= OnScreenChanged;
        context.Router.Changed -= OnRouterChanged;
        context.Services.Flow.LoadProgress.Changed -= OnLoadProgress;
        context.Services.Flow.ContextChanged -= OnMissionContextChanged;
        context.Services.Settings.Changed -= OnSettingsChanged;
        GetTree().Root.SizeChanged -= OnWindowResized;
        ReleaseSheet();
        ReleaseTutorial();
    }

    public void Initialize(GameServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (_context is not null) return;
        FrontendContext context;
        try
        {
            context = FrontendContext.Create(services);
        }
        catch (Exception ex)
        {
            services.Log.Error(Category, $"front end not started: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        _context = context;
        ApplyDisplay(services.Settings.Current.Display);
        Sound.UiSoundHooks.Attach(this, services.Audio);
        context.Router.ScreenChanged += OnScreenChanged;
        context.Router.Changed += OnRouterChanged;
        context.Services.Flow.LoadProgress.Changed += OnLoadProgress;
        context.Services.Flow.ContextChanged += OnMissionContextChanged;
        context.Services.Settings.Changed += OnSettingsChanged;
        GetTree().Root.SizeChanged += OnWindowResized;
        if (Instantiate<ConnectionOverlayView>(FrontendRoutes.ConnectionOverlay) is { } overlay)
        {
            _freezeHost.AddChild(overlay);
            overlay.Initialize(context);
            _connectionOverlay = overlay;
        }
        ShowScreen(context.Router.Current);
        OnRouterChanged();
    }

    /// <summary>
    /// The guided first run's callout layer: the tutorial director hands its <see cref="TutorialCallouts"/> and a lookup from
    /// anchor id to the anchored control's global rectangle (null when it is not on screen).
    /// </summary>
    public void AttachCallouts(TutorialCallouts callouts, Func<string, Rect2?> anchorRect)
    {
        ArgumentNullException.ThrowIfNull(callouts);
        ArgumentNullException.ThrowIfNull(anchorRect);
        if (_context is not { } context) return;
        if (_callouts is null)
        {
            _callouts = Instantiate<TutorialCalloutView>(FrontendRoutes.TutorialCallout);
            if (_callouts is null) return;
            _calloutHost.AddChild(_callouts);
        }
        _callouts.Bind(callouts, anchorRect, context);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_context is not { } context || @event.IsEcho()) return;
        var router = context.Router;
        if (@event.IsActionPressed(InputBindings.Menu))
        {
            if (router.HandleEscape()) GetViewport().SetInputAsHandled();
            return;
        }

        if (!@event.IsActionPressed(InputBindings.Encyclopedia) || router.Current != Screen.Mission) return;
        if (router.EscMenuVisible) _escMenu?.ShowEncyclopediaUnavailable();
        else if (router.EncyclopediaSheetOpen) router.CloseEncyclopediaSheet();
        else router.OpenEncyclopediaSheet();
        GetViewport().SetInputAsHandled();
    }

    private void OnScreenChanged(ScreenChange change) => ShowScreen(change.To);

    /// <summary>
    /// The launch reached its scene stage: hold it, load the console scene on a worker (its progress fills the last part of the
    /// Loading bar), then instance it hidden and release the stage (<see cref="_Process"/>).
    /// </summary>
    private void OnLoadProgress()
    {
        if (_context is not { } context) return;
        var progress = context.Services.Flow.LoadProgress;
        // A new or abandoned launch dropped every hold; a load still running is only collected.
        if (progress.Stage is LoadStage.Idle or LoadStage.DataLoad) _preloadHeld = false;
        if (progress.Stage != LoadStage.SceneInstancing || _preloadHeld || _mission is not null) return;
        if (!ResourceLoader.Exists(MissionScenePath)) return;

        progress.HoldScene();
        _preloadHeld = true;
        if (_preloadPath is not null)
        {
            SetProcess(true);
            return;
        }
        _preloaded = null;
        // Godot 4.7.2 leaks one RefCounted when a shader #include is first loaded on a loader thread (the console's
        // 3D view uses one); loaded and held here on the main thread, the worker finds it cached (PLAYABLE_BUGS 2).
        _threadLoadHold = ResourceLoader.Exists(ShaderIncludePath) ? GD.Load<Resource>(ShaderIncludePath) : null;
        var error = ResourceLoader.LoadThreadedRequest(MissionScenePath, nameof(PackedScene));
        if (error != Error.Ok)
        {
            context.Log.Warn(Category, $"{MissionScenePath} background load not started ({error})");
            _preloadHeld = false;
            progress.ReleaseScene();
            return;
        }
        _preloadPath = MissionScenePath;
        SetProcess(true);
    }

    private void ShowScreen(Screen screen)
    {
        if (_context is not { } context) return;
        var router = context.Router;

        if (FrontendRoutes.KeepsMissionScene(screen, router.SettingsReturn)) EnsureMissionScene(context);
        else ReleaseMissionScene();
        _missionHost.Visible = FrontendRoutes.ShowsMissionScene(screen);

        if (_screenShown == screen && _screen is not null) return;
        if (_screen is not null)
        {
            _screenHost.RemoveChild(_screen);
            _screen.QueueFree();
            _screen = null;
        }
        _screenShown = screen;

        if (FrontendRoutes.SceneFor(screen) is not { } path) return;
        var node = Instantiate<Node>(path);
        if (node is null) return;
        _screenHost.AddChild(node);
        _screen = node;
        if (node is IFrontendScreen frontend)
        {
            try
            {
                frontend.Initialize(context);
            }
            catch (Exception ex)
            {
                context.Log.Error(Category, $"{path} failed to bind: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private void OnRouterChanged()
    {
        if (_context is not { } context) return;
        var router = context.Router;
        var flying = router.Current == Screen.Mission;

        if (_escMenu is not null) _escMenu.Visible = HostsEscMenu && router.EscMenuVisible;
        if (_sheet is not null)
        {
            var show = flying && router.EncyclopediaSheetOpen && !router.EscMenuVisible && !router.ConnectionOverlayVisible;
            if (show && !_sheet.Visible) _sheet.Open();
            _sheet.Visible = show;
        }
        _escMenu?.Refresh();
        _connectionOverlay?.Refresh();
    }

    private void EnsureMissionScene(FrontendContext context)
    {
        if (_mission is not null) return;
        var services = context.Services;
        Node mission;
        if (_preloaded is { } packed)
        {
            mission = packed.Instantiate();
            _preloaded = null;
        }
        else if (ResourceLoader.Exists(MissionScenePath) && Instantiate<Node>(MissionScenePath) is { } scene)
        {
            mission = scene;
        }
        else
        {
            context.Log.Warn(Category, $"{MissionScenePath} not found; showing a placeholder console");
            var placeholder = Ui.Fill(new CenterContainer { Name = "MissionPlaceholder" });
            placeholder.AddChild(Ui.Label(MissionScenePath, Ui.Meta));
            mission = placeholder;
        }
        _missionHost.AddChild(mission);
        _mission = mission;
        if (mission is IMissionScene missionScene) missionScene.Initialize(services);
        if (mission is MissionConsole console)
        {
            // The console binds every runtime context the flow host announces; its header menu button opens the Esc menu.
            console.AttachServices(services);
            console.MenuRequested += () => context.Router.OpenEscMenu();
        }

        if (HostsEscMenu && Instantiate<EscMenuView>(FrontendRoutes.EscMenu) is { } esc)
        {
            _escHost.AddChild(esc);
            esc.Initialize(context, () => services.Flow.Context?.Session.Now.Milliseconds);
            esc.Visible = false;
            _escMenu = esc;
        }

        if (context.Encyclopedia is { } encyclopedia && Instantiate<EncyclopediaSheetView>(FrontendRoutes.EncyclopediaSheet) is { } sheet)
        {
            _sheetModel = new EncyclopediaSheetModel(encyclopedia, context.Strings);
            _sheetHost.AddChild(sheet);
            sheet.Initialize(context, _sheetModel, onClose: () => context.Router.CloseEncyclopediaSheet());
            sheet.Visible = false;
            _sheet = sheet;
            BindAdvisor(services.Flow.Context);
        }

        BindTutorial(services.Flow.Context);
    }

    private void ReleaseMissionScene()
    {
        ReleaseSheet();
        FreeChild(_escHost, ref _escMenu);
        FreeChild(_calloutHost, ref _callouts);
        if (_mission is null) return;
        _missionHost.RemoveChild(_mission);
        _mission.QueueFree();
        _mission = null;
    }

    private void ReleaseSheet()
    {
        _advisorBinding?.Dispose();
        _advisorBinding = null;
        _sheetModel = null;
        if (_sheet is not null) FreeChild(_sheetHost, ref _sheet);
    }

    private void OnMissionContextChanged(MissionRuntimeContext? context)
    {
        if (context is null)
        {
            _advisorBinding?.Dispose();
            _advisorBinding = null;
            ReleaseTutorial();
            return;
        }
        BindAdvisor(context);
        BindTutorial(context);
    }

    // The guided first run: the session's tutorial events feed the callout layer; "Got it" and the Settings option go back as
    // session commands (TutorialCalloutFeed), so replays and resumes repeat them.
    private void BindTutorial(MissionRuntimeContext? runtime)
    {
        if (runtime is null || runtime.IsDisposed || _context is not { } context) return;
        if (!ReferenceEquals(runtime, _tutorialRuntime))
        {
            ReleaseTutorial();
            var feed = new TutorialCalloutFeed(new SessionTutorial(runtime.Session), context.Services.Settings.Current.Gameplay.TutorialCallouts, runtime.Rails);
            _tutorialEvents =
            [
                runtime.Events.Register<TutorialCalloutShown>(feed.Observe),
                runtime.Events.Register<TutorialCalloutClosed>(feed.Observe),
            ];
            _tutorial = feed;
            _tutorialRuntime = runtime;
        }
        AttachCallouts(_tutorial!.Callouts, TutorialAnchorRect);
    }

    private void ReleaseTutorial()
    {
        foreach (var registration in _tutorialEvents) registration.Dispose();
        _tutorialEvents = [];
        _tutorial?.Dispose();
        _tutorial = null;
        _tutorialRuntime = null;
    }

    // The console implements ITutorialAnchors; until it does, callouts stay hidden (no anchor on screen).
    private Rect2? TutorialAnchorRect(string anchorId) =>
        (_mission as ITutorialAnchors)?.Find(anchorId) is { } r ? new Rect2(r.X, r.Y, r.Width, r.Height) : null;

    // The sheet's "Ask the engineers" box answers through the running mission's advisor (EncyclopediaAdvisorBinding).
    private void BindAdvisor(MissionRuntimeContext? runtime)
    {
        if (runtime is null || runtime.IsDisposed || _sheetModel is null || _advisorBinding is not null) return;
        _advisorBinding = runtime.BindEncyclopedia(_sheetModel);
    }

    private void OnSettingsChanged(SettingsChange change)
    {
        if (change.Previous.Display != change.Current.Display) ApplyDisplay(change.Current.Display);
        _tutorial?.ApplySetting(change.Current.Gameplay.TutorialCallouts);
    }

    private void OnWindowResized()
    {
        if (_context is { } context) ThemeApplier.ApplyScale(GetTree().Root, context.Services.Settings.Current.Display);
    }

    private void ApplyDisplay(DisplaySettings display)
    {
        if (_context is not { } context) return;
        context.Tokens = ThemeApplier.Apply(GetTree().Root, display);
        _background.Color = context.Tokens.ColorBg0;
        _background.Visible = true;
    }

    private T? Instantiate<T>(string path) where T : Node
    {
        if (!ResourceLoader.Exists(path) || ResourceLoader.Load<PackedScene>(path) is not { } scene)
        {
            _context?.Log.Error(Category, $"scene {path} is missing");
            return null;
        }
        var node = scene.Instantiate();
        if (node is T typed) return typed;
        _context?.Log.Error(Category, $"scene {path} root is {node.GetType().Name}, expected {typeof(T).Name}");
        node.QueueFree();
        return null;
    }

    private static void FreeChild<T>(Control host, ref T? child) where T : Node
    {
        if (child is null) return;
        if (child.GetParent() == host) host.RemoveChild(child);
        child.QueueFree();
        child = null;
    }
}
