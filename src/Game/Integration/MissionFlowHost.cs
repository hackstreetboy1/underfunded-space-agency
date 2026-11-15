using Godot;
using Usa.Game.Bootstrap;
using Usa.Game.Content;
using Usa.Game.Flow;
using Usa.Game.Flow.Profile;
using Usa.Game.Hosting;
using Usa.Game.Hosting.Nodes;
using Usa.Game.Integration.Runtime;
using Usa.Game.Presentation.ViewModels;
using Usa.Game.Services.Connection;
using Usa.Game.Services.Logging;
using Usa.Simulation.Data;
using Usa.Simulation.Kernel;
using Usa.Simulation.Scenario;

namespace Usa.Game.Integration;

/// <summary>A mission the flow host is running: the console registers its event handlers on <see cref="Events"/>.</summary>
public sealed record MissionRuntime(MissionLaunch Launch, LoadedMission Mission, EventMarshaller Events);

/// <summary>
/// Runs missions for the screen router: builds queued launches over the next frames, one stage per frame, reporting real
/// progress through <see cref="LoadProgress"/> (so the Loading screen shows it), drives the session with its <see cref="SimulationHost"/> child (which attaches it to the app-wide freeze reasons),
/// answers <c>LoadingCompleted</c>/<c>LoadingFailed</c>, grades the run, stops the simulation and records it in the profile once
/// when the mission ends, then opens the debrief when the player presses "Proceed to debrief" on the console's closing card
/// (<see cref="MissionRuntimeContext.ProceedToDebrief"/>; at once when no runtime is composed), forwards connection loss to the router, and keeps a resume save of the running flight. Once the app's services
/// are attached (<see cref="AttachServices"/>), every started run is composed into a <see cref="MissionRuntimeContext"/>
/// (<see cref="Context"/>, announced by <see cref="ContextChanged"/> before <see cref="MissionStarted"/>) and disposed when it stops.
/// Call <see cref="Initialize"/> before adding it to the tree.
/// </summary>
public sealed partial class MissionFlowHost : Node
{
    public const double ResumeSaveIntervalS = 30;

    /// <summary>The longest the launch waits for the mission scene before it starts anyway (a stuck threaded load).</summary>
    public const double SceneWaitLimitS = 30;

    private const string Category = "MissionFlow";

    private MissionLauncher _launcher = null!;
    private FreezeController _freeze = null!;
    private ConnectionMonitor _connection = null!;
    private ContentCatalog _content = null!;
    private ProfileRecorder _profile = null!;
    private IMonotonicClock _clock = null!;
    private UpdateThrottle _uiThrottle = null!;
    private Log _log = null!;
    private SimulationHost _host = null!;
    private GameServices? _services;
    private DebriefFactsRecorder? _debrief;
    private MissionEnded? _ended;
    private DebriefView? _finished;
    private double _sinceResumeSaveS;
    private MissionLaunchOutcome? _built;
    private ulong? _awaitingScene;
    private double _sceneWaitS;

    /// <summary>A session is running; the mission console binds to it.</summary>
    public event Action<MissionRuntime>? MissionStarted;

    /// <summary>The running session stopped (ended, restarted or abandoned); unbind from it.</summary>
    public event Action<MissionRuntime>? MissionStopped;

    /// <summary>
    /// The composed runtime of a started run (raised before <see cref="MissionStarted"/>), or null when it stops (raised before the
    /// context is disposed, so scenes unbind from live view models).
    /// </summary>
    public event Action<MissionRuntimeContext?>? ContextChanged;

    public ScreenRouter Router { get; private set; } = null!;

    public MissionLauncher Launcher => _launcher;

    public SimulationHost Host => _host;

    public MissionRuntime? Runtime { get; private set; }

    /// <summary>Real progress of the launch being loaded (data load, session compose, scene instancing); the Loading screen binds to it.</summary>
    public LoadProgress LoadProgress { get; } = new();

    /// <summary>What the mission scenes bind to while a run is composed; null between runs or before <see cref="AttachServices"/>.</summary>
    public MissionRuntimeContext? Context { get; private set; }

    public void Initialize(
        MissionLauncher launcher,
        FreezeController freeze,
        ConnectionMonitor connection,
        ContentCatalog content,
        ProfileRecorder profile,
        IMonotonicClock clock,
        UpdateThrottle uiThrottle,
        Log log)
    {
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _freeze = freeze ?? throw new ArgumentNullException(nameof(freeze));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _uiThrottle = uiThrottle ?? throw new ArgumentNullException(nameof(uiThrottle));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        Router = new ScreenRouter(launcher, new FreezeControlAdapter(freeze));
    }

    /// <summary>The app's services, built after this host; from now on each started run is composed.</summary>
    public void AttachServices(GameServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!ReferenceEquals(services.Flow, this)) throw new ArgumentException("The services belong to another flow host.", nameof(services));
        _services = services;
    }

    public override void _Ready()
    {
        if (Router is null) throw new InvalidOperationException($"{nameof(MissionFlowHost)}.{nameof(Initialize)} must be called before adding it to the tree.");
        Name = nameof(MissionFlowHost);
        ProcessMode = ProcessModeEnum.Always;
        _host = new SimulationHost { Name = nameof(SimulationHost) };
        AddChild(_host);
        _launcher.Discarded += OnDiscarded;
        _connection.Lost += OnConnectionLost;
        _connection.Restored += OnConnectionRestored;
    }

    public override void _ExitTree()
    {
        if (Runtime is not null && _ended is null) _launcher.SaveResume();
        _launcher.FlushSaves();
        _launcher.Discarded -= OnDiscarded;
        _connection.Lost -= OnConnectionLost;
        _connection.Restored -= OnConnectionRestored;
        StopRuntime();
    }

    public override void _Process(double delta)
    {
        AdvanceLoading(delta);
        if (Runtime is not { } runtime || _awaitingScene is not null) return;

        _sinceResumeSaveS += delta;
        if (_sinceResumeSaveS >= ResumeSaveIntervalS && _ended is null && !runtime.Mission.Session.IsFrozen)
        {
            _sinceResumeSaveS = 0;
            _launcher.SaveResumeInBackground();
        }
    }

    /// <summary>
    /// One loading stage per frame, so the Loading screen draws between them (<see cref="LoadProgress"/>): announce the data load,
    /// build the queued launch, compose its runtime, then wait for the mission scene (frozen by the router's Loading freeze)
    /// before answering <c>LoadingCompleted</c>.
    /// </summary>
    private void AdvanceLoading(double delta)
    {
        if (_awaitingScene is { } seed)
        {
            _sceneWaitS += delta;
            if (!LoadProgress.SceneSettled && _sceneWaitS < SceneWaitLimitS) return;
            if (!LoadProgress.SceneSettled) _log.Warn(Category, $"mission scene not ready after {SceneWaitLimitS} s; starting without waiting for it");
            _awaitingScene = null;
            CompleteLoading(seed);
            return;
        }

        if (_built is { } built)
        {
            _built = null;
            Begin(built);
            return;
        }

        if (!_launcher.HasPending) return;
        if (LoadProgress.Stage != LoadStage.DataLoad)
        {
            LoadProgress.Begin();
            return;
        }
        if (_launcher.ProcessPending() is not { } outcome) return;
        if (outcome.Mission is null)
        {
            Begin(outcome);
            return;
        }
        LoadProgress.Enter(LoadStage.SessionCompose);
        _built = outcome;
    }

    private void Begin(MissionLaunchOutcome outcome)
    {
        if (outcome.Mission is not { } mission)
        {
            foreach (var error in outcome.Errors) _log.Error(Category, $"launch {outcome.Launch.MissionId}: {error}");
            LoadProgress.Reset();
            Router.LoadingFailed();
            return;
        }

        var events = new EventMarshaller(_clock, (exception, simEvent) =>
            _log.Error(Category, $"handler failed on {simEvent.GetType().Name}: {exception.GetType().Name}: {exception.Message}"));
        var debrief = new DebriefFactsRecorder(mission.Session.Data);
        RecordHistory(debrief, mission.History);
        events.Register<ISimEvent>(debrief.Observe);
        events.Register<MissionEnded>(OnMissionEnded);

        _debrief = debrief;
        _ended = null;
        _finished = null;
        _sinceResumeSaveS = 0;
        var runtime = new MissionRuntime(outcome.Launch, mission, events);
        Runtime = runtime;
        _host.Initialize(mission.Port, mission.Session.Data.TimeCompression, events, _uiThrottle, clock: _clock, freeze: _freeze);
        if (!ComposeContext(runtime))
        {
            _host.Shutdown();
            Runtime = null;
            _debrief = null;
            _launcher.Complete();
            LoadProgress.Reset();
            Router.LoadingFailed();
            return;
        }

        // The shell holds this stage while it loads and instances the console; without one the launch completes at once.
        _awaitingScene = mission.Session.Seed;
        _sceneWaitS = 0;
        LoadProgress.Enter(LoadStage.SceneInstancing);
        if (!LoadProgress.SceneSettled) return;
        _awaitingScene = null;
        CompleteLoading(mission.Session.Seed);
    }

    private void CompleteLoading(ulong seed)
    {
        if (Runtime is not { } runtime) return;
        LoadProgress.Finish();
        Router.LoadingCompleted(seed);
        LoadProgress.Reset();
        if (Context is { } context)
        {
            ContextChanged?.Invoke(context);
            foreach (var unbound in context.UnboundEvents())
                _log.Warn(Category, $"no mission scene handles {nameof(MissionRuntimeContext)}.{unbound}");
        }
        MissionStarted?.Invoke(runtime);
    }

    // The replay already happened: only the debrief learns from it, never the live runtime (that starts at the resume MET).
    private void RecordHistory(DebriefFactsRecorder debrief, MissionHistory history)
    {
        var failed = 0;
        foreach (var simEvent in history.Events)
        {
            try
            {
                debrief.Observe(simEvent);
            }
            catch (Exception ex)
            {
                if (failed++ == 0) _log.Error(Category, $"debrief history: {simEvent.GetType().Name}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        if (history.Events.Count > 0)
            _log.Info(Category, $"resumed at MET {history.ResumedAt}: {history.Events.Count} replayed event(s) recorded for the debrief only ({failed} refused)");
    }

    private bool ComposeContext(MissionRuntime runtime)
    {
        if (_services is not { } services) return true;
        try
        {
            var context = MissionRuntimeComposer.Compose(runtime, services, owner: this);
            Context = context;
            runtime.Mission.Port.ConfirmationPending = () => context.Comms.PendingConfirmation is not null;
            runtime.Mission.Port.HeldOrderAt = () => context.BurnOrders.Pending?.OrderAt;
            foreach (var failure in context.HistoryRestoreFailures)
                _log.Warn(Category, $"resume state restore skipped an event: {failure.GetType().Name}: {failure.Message}");
            _log.Info(Category, $"composed runtime for {runtime.Mission.Session.SessionId}");
            return true;
        }
        catch (Exception ex)
        {
            // The composer released whatever it had wired before rethrowing.
            _log.Error(Category, $"runtime for {runtime.Launch.MissionId} not composed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    // Raised inside the host's event flush; finishing the run there would tear the host down mid-flush.
    private void OnMissionEnded(MissionEnded ended)
    {
        if (_ended is not null) return;
        _ended = ended;
        Callable.From(FinishMission).CallDeferred();
    }

    /// <summary>
    /// The run is over: grade it, stop driving the simulation and record it in the profile (which also drops the resume save).
    /// With a composed runtime the player stays on the console with the closing card (recovery card, or the crew-lost card)
    /// until <see cref="MissionRuntimeContext.ProceedToDebrief"/>; without one the debrief opens at once.
    /// </summary>
    private void FinishMission()
    {
        if (Runtime is not { } runtime || _ended is not { } ended || _debrief is not { } debrief || _finished is not null) return;
        var session = runtime.Mission.Session;
        DebriefView? view = null;
        try
        {
            var breakdown = session.Finish();
            view = _content.Get(session.MissionId).DebriefComposer.Compose(breakdown, debrief.Build(ended.Outcome == MissionOutcomeKind.Recovered, session.State, session.Data.Scenario.Mission.Crew));
        }
        catch (Exception ex)
        {
            _log.Error(Category, $"debrief for {session.MissionId} not composed: {ex.GetType().Name}: {ex.Message}");
        }

        if (view is null)
        {
            // Never leave the player on a dead console: back to the main menu with a notice (the router completes the launcher).
            _log.Warn(Category, $"{session.MissionId} ended ({ended.Outcome}) at MET {ended.Time} without a debrief; returning to the main menu");
            if (!Router.MissionAborted(MissionAbortReason.DebriefUnavailable)) _launcher.Complete();
            return;
        }

        _finished = view;
        _host.Shutdown();
        var status = _profile.Record(new CompletedMission(runtime.Launch, session.Seed, view));
        _log.Info(Category, $"{session.MissionId}/{session.DifficultyId} ended ({ended.Outcome}) at MET {ended.Time}; profile {status}");

        if (Context is { } context && context.Closing is not null)
        {
            context.ProceedToDebriefRequested += OnProceedToDebrief;
            return;
        }
        OpenDebrief();
    }

    // Raised from the closing card's button inside the context; opening the debrief disposes that context, so do it after.
    private void OnProceedToDebrief() => Callable.From(OpenDebrief).CallDeferred();

    private void OpenDebrief()
    {
        if (Runtime is not { } runtime || _finished is not { } view) return;
        if (Context is { } context) context.ProceedToDebriefRequested -= OnProceedToDebrief;
        if (Router.MissionEnded(view)) return;
        _log.Warn(Category, $"{runtime.Mission.Session.MissionId}: the router refused the debrief; returning to the main menu");
        if (!Router.MissionAborted(MissionAbortReason.DebriefUnavailable)) _launcher.Complete();
    }

    private void OnDiscarded(LoadedMission mission)
    {
        if (_built?.Mission == mission) _built = null;
        if (Runtime?.Mission == mission) StopRuntime();
    }

    /// <summary>
    /// Stops the running mission. The teardown checklist itself is <see cref="MissionRuntimeContext.Dispose"/> (ticker, view and
    /// feed bindings, tips, advisor, comms host, comms, crew speech, console, audio cues); then the host stops driving the session.
    /// </summary>
    private void StopRuntime()
    {
        if (Runtime is not { } runtime) return;
        _awaitingScene = null;
        DisposeContext();
        _host.Shutdown();
        Runtime = null;
        _debrief = null;
        MissionStopped?.Invoke(runtime);
    }

    private void DisposeContext()
    {
        if (Context is not { } context) return;
        Context = null;
        try
        {
            ContextChanged?.Invoke(null);
        }
        finally
        {
            try
            {
                context.Dispose();
                _log.Info(Category, $"disposed runtime for {context.Session.SessionId}");
            }
            catch (AggregateException ex)
            {
                foreach (var inner in ex.InnerExceptions)
                    _log.Error(Category, $"runtime teardown step failed: {inner.GetType().Name}: {inner.Message}");
            }
        }
    }

    private void OnConnectionLost(ConnectionReport report) => Router.OnConnectionLost();

    private void OnConnectionRestored(ConnectionReport report) => Router.OnConnectionRestored();
}
