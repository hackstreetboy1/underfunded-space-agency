using Godot;
using Usa.Game.Diagnostics;
using Usa.Game.Flow.Backend;
using Usa.Game.Flow.Nodes;
using Usa.Game.Flow.Profile;
using Usa.Game.Hosting;
using Usa.Game.Input;
using Usa.Game.Integration;
using Usa.Game.Integration.Runtime.Hosting;
using Usa.Game.Presentation.Audio;
using Usa.Game.Presentation.Audio.Mixing;
using Usa.Game.Presentation.ViewModels;
using Usa.Game.Services.Ai;
using Usa.Game.Services.Backend;
using Usa.Game.Services.Connection;
using Usa.Game.Services.Data;
using Usa.Game.Services.Logging;
using Usa.Game.Services.Saves;
using Usa.Game.Services.Settings;
using Usa.Game.Services.Storage;
using Usa.Game.Services.Voice;
using Usa.Game.UI.Frontend;

namespace Usa.Game.Bootstrap;

/// <summary>
/// Composition root (autoload). Builds application services once and hands them to scenes. Owns the app's one
/// <see cref="FreezeController"/>: the screen router sets its reasons (Menu, ServiceOutage while a mission runs, Loading),
/// the mission's <c>SimulationHost</c> attaches the session to it, and its <see cref="FreezeController.Frozen"/> signal
/// holds audio, speech and the mic here. Comms is per run: <c>MissionRuntimeComposer</c> builds each run's orchestrator
/// (command ids from <c>LoadedMission.CreateCommsIds</c>) and its <c>CommsController</c>, and subscribes it to the same
/// signal. Once the services exist they are attached to the <see cref="MissionFlowHost"/>, which composes each started
/// run into its <c>MissionRuntimeContext</c>.
/// </summary>
public sealed partial class GameRoot : Node
{
    /// <summary>The roadmap phase this build belongs to (repros and the boot log print it next to the version).</summary>
    public const string BuildPhase = "slice";
    private const string Category = "GameRoot";

    private GameServices? _services;
    private IDisposable? _frozenSubscription;
    private bool _quitting;

    /// <summary>Available from the autoload's <c>_Ready</c>, i.e. before any scene's <c>_Ready</c>.</summary>
    public GameServices Services => _services ?? throw new InvalidOperationException("GameRoot has not composed its services yet.");

    public bool IsComposed => _services is not null;

    public override void _EnterTree()
    {
        ProcessMode = ProcessModeEnum.Always;
        InputBindings.RegisterDefaults();
    }

    public override void _Ready()
    {
        var log = Log.CreateDefault(OS.IsDebugBuild());

        // Before settings touch the buses: AudioBusSetup only equips buses it creates, so it must create Speech.
        AudioBusSetup.EnsureBuses(MixProfile.Default);

        var store = new UserFileStore(UserPaths.Root, log);
        var settings = SettingsService.Load(store, log);
        settings.ApplyToEngine();
        var defaultBindings = GodotFlowResources.CaptureDefaultBindings(settings);
        var launchCount = new LaunchCounter(store).RegisterLaunch();

        var gateway = new AiGateway(new Uri(settings.Current.BackendUrl), log);
        // Only the local proxy is asked; it spends at most one NASA call per UTC day for every launch together.
        var apod = new ApodService(new ApodClient(() => gateway.BaseUrl), log);
        _ = apod.Start();
        var freeze = new FreezeController(ex => log.Error(Category, $"Freeze subscriber failed: {ex}"));

        var connection = new ConnectionMonitor();
        connection.Initialize(gateway, log);
        AddChild(connection);

        // Pre-mission ground-link status, and (dev convenience) a proxy started for the player when nothing answers at boot.
        var launchOptions = GodotBackendLaunch.ResolveOptions(new Uri(settings.Current.BackendUrl), settings.Current.BackendAutoStart);
        var backendLauncher = GodotBackendLaunch.CreateLauncher(launchOptions, log);
        var backend = new BackendStatusTracker(connection.ProbeNow, new BackendStarterAdapter(backendLauncher));
        var backendHost = new BackendStatusHost();
        backendHost.Initialize(backend, connection, backendLauncher);
        AddChild(backendHost);
        log.Info(Category, $"proxy auto-start {(launchOptions.AutoStart ? "on" : "off")}, start script {(launchOptions.ScriptPath is null ? "not found" : "found")}");

        var mic = new MicCapture { Name = nameof(MicCapture) };
        mic.Initialize(log);
        AddChild(mic);
        mic.SetMicEnabled(settings.Current.Voice.MicEnabled);

        var speech = new SpeechPlayer { Name = nameof(SpeechPlayer) };
        speech.Initialize(log);
        AddChild(speech);

        var audio = new AudioDirector { Name = nameof(AudioDirector) };
        AddChild(audio);

        var data = new GodotDataSource();
        var clock = new StopwatchClock();
        var uiThrottle = new UpdateThrottle(clock);
        var version = ProjectSettings.GetSetting(AppVersion.SettingPath, AppVersion.Current).AsString();
        var saves = new SaveService(store, log, version);
        var profile = new ProfileRecorder(new SaveServiceProfileStore(saves));
        var content = new ContentCatalog(data);
        var missions = new MissionLoader(data, log, MissionDirectoryScan.List(data));

        var flow = new MissionFlowHost();
        flow.Initialize(new MissionLauncher(missions, saves, log), freeze, connection, content, profile, clock, uiThrottle, log);
        AddChild(flow);

        _services = new GameServices(
            log,
            data,
            settings,
            defaultBindings,
            gateway,
            connection,
            mic,
            speech,
            saves,
            profile,
            launchCount,
            content,
            missions,
            flow,
            audio,
            freeze,
            clock,
            uiThrottle)
        {
            Backend = backend,
            Apod = apod,
        };
        flow.AttachServices(_services);
        DebugTools.Install(this, _services, version);

        ApplyMixVolumes(audio, settings.Current.Audio);
        settings.Changed += OnSettingsChanged;
        _frozenSubscription = freeze.Frozen.Subscribe(OnFrozenChanged);

        // Quitting goes through the audio director so voices stop before the generator playbacks are torn down.
        GetTree().AutoAcceptQuit = false;
        flow.Router.QuitRequested += OnQuitRequested;

        // The main scene's _Ready runs after this autoload's; attach the front end once it is in the tree.
        Callable.From(AttachFrontend).CallDeferred();

        log.Info(Category, $"Underfunded Space Agency booted ({BuildPhase}, Godot {Engine.GetVersionInfo()["string"]}, {_services.Missions.MissionIds.Count} mission(s)).");
    }

    // _Input, ahead of the GUI: Alt+Enter must not also submit a focused text field or focus comms (Enter).
    public override void _Input(InputEvent @event)
    {
        if (_services is not { } services || !@event.IsActionPressed(InputBindings.ToggleFullscreen)) return;
        GetViewport().SetInputAsHandled();
        services.Settings.ToggleFullscreen();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) OnQuitRequested();
    }

    public override void _ExitTree()
    {
        if (_services is not { } services) return;
        services.Router.QuitRequested -= OnQuitRequested;
        services.Settings.Changed -= OnSettingsChanged;
        _frozenSubscription?.Dispose();
        _frozenSubscription = null;
        services.Freeze.Attach(null);
        // Heartbeats end before the gateway does, so quitting is never logged as a failed health check.
        services.Connection.Shutdown();
        services.Apod?.Dispose();
        services.Gateway.Dispose();
        _services = null;
    }

    private void AttachFrontend()
    {
        if (_services is not { } services) return;
        if (GetTree().CurrentScene is FrontendShell shell) shell.Initialize(services);
        else services.Log.Info(Category, "main scene is not the front-end shell; no screens attached");
    }

    private void OnQuitRequested()
    {
        if (_quitting) return;
        _quitting = true;
        if (_services is { } services) services.Audio.ReleaseVoicesThen(() => GetTree().Quit());
        else GetTree().Quit();
    }

    private void OnSettingsChanged(SettingsChange change)
    {
        var services = Services;
        services.Gateway.SetBaseUrl(new Uri(change.Current.BackendUrl));
        SetMicEnabled(services, change.Current.Voice.MicEnabled);
        ApplyMixVolumes(services.Audio, change.Current.Audio);
    }

    // Mid-mission the toggle goes through comms so capture and the push-to-talk state agree; a new run's controller
    // reads the mic's state when it enters the tree.
    private static void SetMicEnabled(GameServices services, bool enabled)
    {
        if (services.Flow.Context?.CommsHost is CommsControllerHost { Controller: var comms } && IsInstanceValid(comms) && comms.IsInsideTree())
            comms.SetMicEnabled(enabled);
        else
            services.Mic.SetMicEnabled(enabled);
    }

    private void OnFrozenChanged(bool frozen)
    {
        if (_services is not { } services) return;
        services.Audio.SetFrozen(frozen);
        services.Speech.SetFrozen(frozen);
        services.Mic.SetFrozen(frozen);
    }

    // SettingsService writes plain linear-to-dB volumes; the director adds each bus's mix level on top, so the five mix
    // buses are re-applied through it after every settings change (Master stays with SettingsService).
    private static void ApplyMixVolumes(AudioDirector audio, AudioSettings volumes)
    {
        audio.SetBusVolume(AudioBus.Speech, volumes.SpeechLinear);
        audio.SetBusVolume(AudioBus.Alarms, volumes.AlarmsLinear);
        audio.SetBusVolume(AudioBus.Sfx, volumes.SfxLinear);
        audio.SetBusVolume(AudioBus.Ambience, volumes.AmbienceLinear);
        audio.SetBusVolume(AudioBus.Ui, volumes.UiLinear);
    }
}
