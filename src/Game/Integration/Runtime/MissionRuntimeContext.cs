using Usa.Game.Advisor;
using Usa.Game.Advisor.Glue;
using Usa.Game.Comms;
using Usa.Game.Content;
using Usa.Game.Flow.Screens;
using Usa.Game.Guidance;
using Usa.Game.Guidance.Glue;
using Usa.Game.Hosting;
using Usa.Game.Integration.Adapters;
using Usa.Game.Presentation.Console;
using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.CrewFeed;
using Usa.Game.Presentation.Cues;
using Usa.Game.Presentation.Diagram;
using Usa.Game.Presentation.EntryHud;
using Usa.Game.Presentation.Map;
using Usa.Game.Presentation.Map.Planning;
using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Commands;
using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;
using Usa.Simulation.Scenario;
using Usa.Simulation.Session;
using Usa.Simulation.Vehicle;

namespace Usa.Game.Integration.Runtime;

/// <summary>
/// Everything one started mission shows, composed once by <see cref="MissionRuntimeComposer"/>: the view models and models the
/// mission scenes bind to, wired to the session's event marshaller, plus the per-frame and 10 Hz work that keeps them current.
/// Single-threaded (Godot's main thread). Dispose when the mission stops: it unregisters every handler and subscription.
/// </summary>
/// <remarks>
/// <para><b>Event handlers</b>, registered on the marshaller in this order (so each drained event reaches them in it): frame
/// reader, console adapter, comms adapter, entry HUD source, tip service, audio cues, crew feeds, workspace inventory, diagram (jettisoned
/// sections), burn planner (invalidated by burn ends and docking changes), mission end (<see cref="Closing"/>).</para>
/// <para><b>Mission end</b>: on <c>MissionEnded</c> the context sets <see cref="Closing"/> and raises <see cref="ClosingShown"/>; the
/// flow host grades, stops the simulation and records the run, and the player stays on the console (recovery card or crew-lost
/// card) until <see cref="ProceedToDebrief"/>.</para>
/// <para><b>Per frame</b>, <see cref="Tick"/> right after the host's event flush: (1) comms adapter flush (comms events reach
/// the orchestrator in simulation order), (2) flight track update, (3) 3D view source refresh and a held burn order whose mark has come (<see cref="ExecutePlan"/>), (4) audio sample (rate, stuck
/// thruster, pulses, heating, burn, docking), (5) crew feeds, (6) the 10 Hz <see cref="Pump"/> when its interval has passed.
/// Comms holds its AI requests and submissions while <see cref="MissionRuntimeInputs.Frozen"/> is set. The per-frame steps read
/// narrow inputs through <see cref="Frame"/> instead of a session snapshot (review 2, R5/A6); (2), (3) and the feeds'
/// simulation inputs are skipped while its stamp has not moved (frozen, or nothing happened).</para>
/// <para><b>At 10 Hz</b>, <see cref="Pump"/>: (1) console snapshot (the frame reader's full snapshot, rebuilt only when its
/// stamp moved) from knowledge with the tip service's card → console view model, then every readout's MET (stale ages), (2)
/// entry HUD source refresh → <see cref="EntryHud"/> and
/// <see cref="Splashdown"/>, (3) burn planner engine list (<see cref="Engines"/>) and preview, (4) comms confirmation timeout, (5) <see cref="Pumped"/>.</para>
/// <para><b>Resume</b>: the events of a replay (<see cref="MissionHistory"/>) never reach the handlers above; they only restore
/// state (event log rows, entry chute results, the crew feeds' and audio cues' alarm sets), so nothing from before the resume MET is announced; an alarm still sounding at the save resumes its tone (no announcement).</para>
/// <para><b>Teardown</b> (<see cref="Dispose"/>, also when the constructor fails), one checklist in this order: everything
/// <see cref="Own"/>ed in reverse (ticker, view and feed bindings, encyclopedia bindings, the freeze subscription, marshaller
/// registrations, view-model subscriptions, advisor, tips), the burn proposal still computing, the context's own
/// subscriptions, workspace, comms host, comms,
/// crew speech (<see cref="MissionRuntimeInputs.Speech"/>: queued and playing lines dropped, outage pause released), console,
/// audio cues. A failing step does not stop the later ones.</para>
/// </remarks>
public sealed class MissionRuntimeContext : IDisposable
{
    public const double PumpIntervalS = 0.1;

    private readonly IMissionHostControl _host;
    private readonly ConsoleLayout _layout;
    private readonly ICrewSpeechOutput? _speech;
    private readonly List<IDisposable> _owned = [];
    private readonly List<Exception> _historyFailures = [];
    private double _sincePumpS;
    private bool _wired;
    private bool _disposed;

    /// <param name="history">A resume's replayed events (state restore only); null for a fresh launch.</param>
    internal MissionRuntimeContext(
        MissionSession session,
        EventMarshaller events,
        MissionRuntimeInputs inputs,
        Func<CommandId>? nextCommandId,
        CommandIdAllocator? commsIds,
        MissionHistory? history)
    {
        Session = session;
        Events = events;
        _host = inputs.Host;
        _layout = inputs.Layout ?? ConsoleLayout.For(session.Data.CrewedVehicleId);
        _speech = inputs.Speech;
        Frozen = inputs.Frozen;
        Strings = inputs.Strings;
        try
        {
            Frame = new SessionFrameReader(session);
            Commands = new RuntimeCommands(session, nextCommandId);
            ConsoleAdapter = new ConsoleSnapshotAdapter(session, _layout, inputs.Strings);
            Console = new MissionConsoleViewModel(inputs.Strings, inputs.UiThrottle);
            if (inputs.TutorialRails)
            {
                var rails = new Usa.Simulation.Session.TutorialRails(session.Data.Tutorial, action => IsHarmless(session, action));
                rails.Resume(session.Tutorial, session.State);
                Rails = rails;
            }
            Bridge = new ConsoleCommandBridge(session, ConsoleAdapter.Controls, _host.RequestAdvanceToNextEvent, Commands.Submit, Commands.NextId, Rails);

            CommsAdapter = new CommsSessionAdapter(session, inputs.Strings);
            Comms = new CommsOrchestrator(CommsAdapter, inputs.CommsAi, inputs.CommsContent, session.Actions, session.Data.Facts, inputs.AiGuardrails,
                commsIds ?? new CommandIdAllocator(session.CommandIdPrefix, Commands.NextId), inputs.Time, Rails);

            Tips = new TipService(inputs.TipAi, new TipRephraseValidator(inputs.AiGuardrails),
                new KnowledgeTipFactSource(session.Data.Facts, session.Knowledge, ConsoleAdapter.Readouts), new SessionTipGuidanceControl(session),
                session.Data.Hints.CooldownS, inputs.TipOptions, inputs.Time, inputs.NewRequestId);
            Own(Tips);
            Advisor = new AdvisorService(new ExcerptRetriever(inputs.Encyclopedia), inputs.ExplainAi,
                new AdvisorAnswerValidator(inputs.AiGuardrails, inputs.AdvisorGuardrails), inputs.AdvisorOptions, inputs.Time, inputs.NewRequestId);
            Own(Advisor);

            Track = new FlightTrack(session);
            Spacecraft = new SpacecraftViewSourceAdapter(session, Track, inputs.SpacecraftOptions, Frame);
            MapInput = new OrbitMapInputAdapter(Track)
            {
                FixSource = inputs.Strings.Get("map.sourceGroundTracking"),
                StationLabel = OrbitMapInputAdapter.StationLabels(inputs.Strings, Track.Flight.Stations),
                VehicleLabel = id => session.Data.Vehicles.FirstOrDefault(v => v.Id == id)?.DisplayName,
            };
            Map = MapInput.CreateModel(inputs.Strings, inputs.MapSettings, inputs.ZoneNames is { } zoneNames ? RecoveryZoneGeometry.Labels(zoneNames) : null);
            var propose = session.State.TryGet($"{StatePaths.Difficulty}nav_assist", out var assist) && assist.Kind == StateValueKind.Text &&
                assist.AsText() == "propose";
            var clock = inputs.Clock;
            Planner = new BurnPlannerModel(MapInput.Planner, () => clock.Elapsed, inputs.Strings,
                BurnPlannerSettings.From(Track.Flight.Planning, propose), propose ? MapInput.Planner : null,
                proposalScheduler: inputs.ProposalScheduler);
            Engines = new PlannerEngineSource(session, Track, ConsoleAdapter.Readouts, inputs.Strings);
            BurnOrders = new PlanExecutor(session, Commands, Track);
            AttitudeKnowledge = new AttitudeHoldKnowledge(session, ConsoleAdapter.Readouts);
            Diagram = new DiagramScene(DiagramLayout.Build(session.Data.Vehicles), new DiagramKnowledgeLookup(session.Knowledge, new CrewNames(session.Data.Crew), new AdapterText(inputs.Strings)));
            Workspaces = new RepairWorkspaceFactory(session, Commands, inputs.Strings, inputs.WorkspaceOptions);
            HudWords = EntryHudWords.From(key => Word(inputs.Strings, key));
            EntryHudSource = new EntryHudSourceAdapter(session, inputs.SpacecraftOptions, inputs.ZoneNames);
            EntryHud = EntryHudModel.Build(EntryHudSource, HudWords);
            Splashdown = SplashdownCardModel.Build(EntryHudSource, HudWords);
            CrewFeeds = new CrewFeedDriver(session, inputs.Expressions, clock, inputs.Voices, inputs.CrewFeedSettings, CommsAdapter.IsReadingGauge, Frame);
            if (inputs.AudioSinks is { } sinks)
            {
                AudioCues = new AudioCueMapper(sinks.CreateSink(), clock, inputs.AudioSettings, session.Data.CrewedVehicleId);
                AudioSampler = new AudioStateSampler(session, Track, inputs.SpacecraftOptions);
            }
            RestoreHistory(history);
            Wire();
            if (inputs.Frozen is { } frozen) Own(frozen.Subscribe(Comms.SetFrozen));
            foreach (var vehicle in Frame.Snapshot().Vehicles)
            {
                foreach (var section in vehicle.Sections)
                {
                    if (!section.Attached) Diagram.HideSection(vehicle.Id, section.Id);
                }
            }
            CommsHost = inputs.CommsHosts?.Attach(Comms);
            Tick(0);
            Pump();
        }
        catch (Exception failure)
        {
            _disposed = true;
            try
            {
                Release();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException("Composing the mission runtime failed, and so did cleaning up after it.", failure, cleanup);
            }
            throw;
        }
    }

    /// <summary>Every request the console bridge and the workspace submitted, as the session logged it, with its answer (comms reports its own).</summary>
    public event Action<CommandRequest, ValidationResult>? CommandSubmitted;

    /// <summary>"Request…" and "Ask": place the text (and suggested request) in the comms input for the player to send.</summary>
    public event Action<CommsPrefill>? CommsPrefillRequested;

    /// <summary>Stop on a task that is unsafe to interrupt: confirm, then <see cref="ConsoleCommandBridge.ConfirmStopTask"/>.</summary>
    public event Action<TaskId>? StopConfirmationRequested;

    /// <summary>
    /// The mission ended (recovered, or every crew member lost): the console shows <see cref="Closing"/> and keeps the player
    /// there until <see cref="ProceedToDebrief"/>. Raised once, while the event that ended the mission is drained.
    /// </summary>
    public event Action<MissionClosingCard>? ClosingShown;

    /// <summary>The player pressed "Proceed to debrief"; the flow host (<c>MissionFlowHost</c>) opens the debrief.</summary>
    public event Action? ProceedToDebriefRequested;

    /// <summary>
    /// After every <see cref="Pump"/> (10 Hz): the models it rebuilt (<see cref="EntryHud"/>, <see cref="Splashdown"/>, the
    /// planner's preview) are current; mission scenes refresh their views here.
    /// </summary>
    public event Action? Pumped;

    public MissionSession Session { get; }

    /// <summary>The player-facing words the models were built with (<see cref="MissionRuntimeInputs.Strings"/>).</summary>
    public ITextSource Strings { get; }

    /// <summary>Per-frame reads of the session (change stamp, knowledge revision, attitudes) and the pump's cached snapshot.</summary>
    public SessionFrameReader Frame { get; }

    public EventMarshaller Events { get; }

    /// <summary>The single submission path (id provider + session).</summary>
    public RuntimeCommands Commands { get; }

    public MissionConsoleViewModel Console { get; }

    public ConsoleSnapshotAdapter ConsoleAdapter { get; }

    public ConsoleCommandBridge Bridge { get; }

    /// <summary>
    /// The tutorial rails of the guided first run (<see cref="Usa.Simulation.Guidance.ITutorialRails"/>), consulted by the console, comms and the bridge;
    /// null when <see cref="MissionRuntimeInputs.TutorialRails"/> is off.
    /// </summary>
    public Usa.Simulation.Session.TutorialRails? Rails { get; }

    /// <summary>The snapshot most recently applied to <see cref="Console"/>.</summary>
    public ConsoleSnapshot? LatestConsoleSnapshot { get; private set; }

    public CommsSessionAdapter CommsAdapter { get; }

    public CommsOrchestrator Comms { get; }

    public Transcript Transcript => Comms.Transcript;

    /// <summary>The client host (the game's <c>CommsController</c>); null without a host factory.</summary>
    public ICommsHost? CommsHost { get; }

    public TipService Tips { get; }

    public AdvisorService Advisor { get; }

    public FlightTrack Track { get; }

    public SpacecraftViewSourceAdapter Spacecraft { get; }

    /// <summary>
    /// The Earth's prime-meridian angle at MET 0 (<c>flight.json</c> <c>earthRotationAngleAtMetZeroDeg</c>), the value the
    /// recovery zones turn from; set it on the 3D view's <c>EarthBackdrop</c> so the rendered Earth and the zones agree.
    /// </summary>
    public double EarthRotationAngleAtMetZeroDeg => Track.Flight.Definition.EarthRotationAngleAtMetZeroDeg;

    public OrbitMapInputAdapter MapInput { get; }

    public OrbitMapModel Map { get; }

    public BurnPlannerModel Planner { get; }

    /// <summary>Keeps <see cref="Planner"/>'s engine list current (refreshed at 10 Hz).</summary>
    public PlannerEngineSource Engines { get; }

    /// <summary>The planner's Execute: the crew order or remote command each engine requires (<see cref="ExecutePlan"/>).</summary>
    public PlanExecutor BurnOrders { get; }

    /// <summary>The planner's attitude conditions as the ground knows them (refreshed at 10 Hz).</summary>
    public AttitudeHoldKnowledge AttitudeKnowledge { get; }

    public DiagramScene Diagram { get; }

    /// <summary>Creates workspace models wired to the session.</summary>
    public RepairWorkspaceFactory Workspaces { get; }

    public EntryHudSourceAdapter EntryHudSource { get; }

    /// <summary>The entry HUD and splashdown card words from <c>strings.json</c> (a missing key leaves its word empty).</summary>
    public EntryHudWords HudWords { get; }

    /// <summary>Rebuilt at 10 Hz.</summary>
    public EntryHudModel EntryHud { get; private set; }

    /// <summary>Rebuilt at 10 Hz.</summary>
    public SplashdownCardModel Splashdown { get; private set; }

    /// <summary>The closing card once the mission has ended; null while it is flying.</summary>
    public MissionClosingCard? Closing { get; private set; }

    private bool _proceedPending;

    /// <summary>One feed per crew member (<see cref="CrewFeedDriver.Feeds"/>).</summary>
    public CrewFeedDriver CrewFeeds { get; }

    /// <summary>Null when no audio sink factory was given.</summary>
    public AudioCueMapper? AudioCues { get; }

    public AudioStateSampler? AudioSampler { get; }

    /// <summary>The app-wide freeze signal the run follows (<see cref="MissionRuntimeInputs.Frozen"/>); null when none was given.</summary>
    public IReadOnlyObservable<bool>? Frozen { get; }

    public bool IsDisposed => _disposed;

    /// <summary>The orbit map for this frame: the tracked flight plus the planner's node and preview.</summary>
    public OrbitMapFrame BuildMapFrame() => Map.BuildFrame(MapInput.Build(Planner.BuildPlan(), Planner.Preview));

    /// <summary>
    /// The console calls this whenever the main view changes: attitude-thruster pulse sounds play only while the external view
    /// is the active main view (GDD 14). Until the first call they play.
    /// </summary>
    /// <summary>
    /// The burn planner's Execute (<c>OrbitMapView.ExecuteRequested</c>) for the planner's selected engine: the order the
    /// mission data defines for it (<c>oams_burn</c>, <c>lodestar_burn</c>, <c>retrofire</c>) with the plan's direction and
    /// duration, through the normal command path. High-impact orders come back <see cref="PlanExecutionStatus.NeedsConfirmation"/>
    /// first: show the confirmation (effects from <see cref="BurnOrder.Action"/>) and call again with
    /// <paramref name="confirmed"/>. A crew order is given at its mark (planned start − task duration), held until then and
    /// sent by <see cref="Tick"/>; the answer reaches <see cref="CommandSubmitted"/>.
    /// </summary>
    public PlanExecution ExecutePlan(ManeuverPlan plan, bool confirmed = false)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Planner.SelectedEngine is { } engine
            ? BurnOrders.Execute(plan, engine.Id, confirmed)
            : new PlanExecution(PlanExecutionStatus.Unavailable, null, Reason: "no engine selected");
    }

    /// <summary>Drops a held burn order (the planner's Clear).</summary>
    public void CancelBurnOrder() => BurnOrders.Cancel();

    public void SetExternalViewVisible(bool visible)
    {
        if (AudioSampler is { } sampler) sampler.ExternalViewVisible = visible;
    }

    /// <summary>Connects an encyclopedia sheet's "Ask the engineers" box to <see cref="Advisor"/> until disposed (or the context is).</summary>
    public IDisposable BindEncyclopedia(EncyclopediaSheetModel sheet)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var binding = new OnceDisposable(new EncyclopediaAdvisorBinding(sheet, Advisor));
        _owned.Add(binding);
        return binding;
    }

    /// <summary>Per-frame work, called once per frame after the host flushed the session's events (order on the class).</summary>
    /// <param name="realDeltaS">Real seconds since the previous call; drives the 10 Hz <see cref="Pump"/>.</param>
    public void Tick(double realDeltaS)
    {
        if (_disposed) return;
        CommsAdapter.Flush();
        Track.Update(Session.Now);
        Spacecraft.Refresh();
        BurnOrders.Poll();
        if (AudioCues is not null && AudioSampler is not null) AudioCues.Sample(AudioSampler.Sample());
        CrewFeeds.Update();

        if (!double.IsFinite(realDeltaS) || realDeltaS < 0) return;
        _sincePumpS += realDeltaS;
        if (_sincePumpS < PumpIntervalS) return;
        _sincePumpS = Math.Min(_sincePumpS - PumpIntervalS, PumpIntervalS);
        Pump();
    }

    /// <summary>The 10 Hz UI work (order on the class); <see cref="Tick"/> calls it, hosts may call it directly.</summary>
    public void Pump()
    {
        if (_disposed) return;
        // Turning the guide off between two steps closes no callout, so no event tells the rails; they would hold the console.
        if (Rails is { IsEngaged: true } rails && !Session.Tutorial.IsEnabled) rails.Resume(Session.Tutorial, Session.State);
        var snapshot = Frame.Snapshot();
        var now = snapshot.Now;
        var console = ConsoleAdapter.Build(snapshot, _host.Compression, _host.Link) with
        {
            Tip = TipCardMapping.ToSnapshot(Tips.Card(now), _layout),
            TipCooldownUntil = Tips.CooldownUntil is { } until && until > now ? until : null,
        };
        Console.Apply(console);
        LatestConsoleSnapshot = console;
        SetReadoutTimes(now);

        EntryHudSource.Refresh();
        EntryHud = EntryHudModel.Build(EntryHudSource, HudWords);
        Splashdown = SplashdownCardModel.Build(EntryHudSource, HudWords);
        Spacecraft.SetRecovery(Splashdown);
        Engines.Refresh(Planner, snapshot);
        Planner.SetConditions(AttitudeKnowledge.Conditions());
        Planner.Update();
        Comms.Poll();
        Pumped?.Invoke();
    }

    /// <summary>
    /// "Proceed to debrief" on the recovery card or the crew-lost card: hands the finished run to the flow host. False while the
    /// mission is still flying, after teardown, or when no host listens (nothing happens).
    /// </summary>
    public bool ProceedToDebrief()
    {
        if (_disposed || ProceedToDebriefRequested is not { } proceed) return false;
        if (Closing is null)
        {
            // The recovery card enables Proceed from the snapshot, which can show "recovered" a frame or two before the
            // MissionEnded event arrives; a click in that window is kept and carried out when the closing card is set.
            if (!Splashdown.ProceedEnabled) return false;
            _proceedPending = true;
            return true;
        }
        proceed();
        return true;
    }

    /// <summary>Runs the teardown checklist (order on the class). Every step runs; failures are rethrown together at the end.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Release();
    }

    /// <summary>
    /// Events of this context nobody handles. Once the mission scenes have bound (after <c>ContextChanged</c>) this must be
    /// empty: an unhandled <see cref="StopConfirmationRequested"/> leaves the player's Stop button doing nothing.
    /// </summary>
    public IReadOnlyList<string> UnboundEvents()
    {
        var missing = new List<string>(3);
        if (CommandSubmitted is null) missing.Add(nameof(CommandSubmitted));
        if (CommsPrefillRequested is null) missing.Add(nameof(CommsPrefillRequested));
        if (StopConfirmationRequested is null) missing.Add(nameof(StopConfirmationRequested));
        return missing;
    }

    /// <summary>Replayed events a state-restoring module refused (each is skipped); the host logs them.</summary>
    public IReadOnlyList<Exception> HistoryRestoreFailures => _historyFailures;

    /// <summary>
    /// Released first at teardown, newest first (hosting nodes, view bindings, subscriptions, handler registrations). Once the
    /// context is disposed, whatever is handed in is disposed at once.
    /// </summary>
    internal void Own(IDisposable disposable)
    {
        ArgumentNullException.ThrowIfNull(disposable);
        if (_disposed) disposable.Dispose();
        else _owned.Add(disposable);
    }

    private void Wire()
    {
        Own(Events.Register<ISimEvent>(Frame.Observe));
        if (Rails is { } rails) Own(Events.Register<ISimEvent>(rails.Observe));
        Own(Events.Register<ISimEvent>(ConsoleAdapter.Observe));
        Own(Events.Register<ISimEvent>(CommsAdapter.Observe));
        Own(Events.Register<ISimEvent>(EntryHudSource.Observe));
        Own(Events.Register<ISimEvent>(Tips.Observe));
        if (AudioCues is { } cues) Own(Events.Register<ISimEvent>(cues.Handle));
        Own(Events.Register<ISimEvent>(CrewFeeds.Observe));
        Own(Events.Register<ISimEvent>(Workspaces.OnSimEvent));
        Own(Events.Register<SectionJettisoned>(e => Diagram.HideSection(e.VehicleId, e.SectionId)));
        Own(Events.Register<BurnEnded>(_ => Planner.Invalidate()));
        Own(Events.Register<DockingStateChanged>(_ => Planner.Invalidate()));
        Own(Events.Register<MissionEnded>(OnMissionEnded));

        // Bridge.Attach would also send the tip card's buttons straight to the director; they go through the tip service instead.
        var controls = Console.Systems.Controls;
        Own(controls.ExecuteRequested.Subscribe(intent => Bridge.Execute(intent)));
        Own(controls.CrewOrderRequested.Subscribe(Bridge.RequestCrewOrder));
        Own(Console.Alarms.AcknowledgeRequested.Subscribe(request => Bridge.Acknowledge(request)));
        Own(Console.TaskSlot.StopRequested.Subscribe(request => Bridge.Stop(request)));
        Own(Console.Header.AdvanceRequested.Subscribe(request => Bridge.AdvanceToNextEvent(request)));
        Own(Console.AskRequested.Subscribe(Bridge.Ask));
        Own(Console.Tip.StrongerHintRequested.Subscribe(_ => Tips.RequestStrongerHint()));
        Own(Console.Tip.Dismissed.Subscribe(tipId => Tips.Dismiss(tipId)));

        Commands.Submitted += OnSubmitted;
        Bridge.PrefillRequested += OnPrefillRequested;
        Bridge.StopConfirmationRequested += OnStopConfirmationRequested;
        Comms.Transcript.Changed += CrewFeeds.OnTranscriptChanged;
        _wired = true;
    }

    // Only modules that keep state; none of them raises anything for the player here.
    private void RestoreHistory(MissionHistory? history)
    {
        if (history is null) return;
        foreach (var simEvent in history.Events)
        {
            Restore(ConsoleAdapter.Observe, simEvent);
            Restore(EntryHudSource.Observe, simEvent);
            Restore(CrewFeeds.ObserveHistory, simEvent);
            if (AudioCues is { } cues) Restore(cues.Restore, simEvent);
        }
        // The alarm sound the uninterrupted run had at the save: unacknowledged alarms keep sounding (QA-4).
        if (AudioCues is not { } restored) return;
        try
        {
            restored.ResumeAlarms();
        }
        catch (Exception ex)
        {
            _historyFailures.Add(ex);
        }
    }

    private void Restore(Action<ISimEvent> module, ISimEvent simEvent)
    {
        try
        {
            module(simEvent);
        }
        catch (Exception ex)
        {
            _historyFailures.Add(ex);
        }
    }

    // Null-tolerant: a failed constructor runs it over a partly built context.
    private void Release()
    {
        List<Exception>? failures = null;
        for (var i = _owned.Count - 1; i >= 0; i--) Step(_owned[i].Dispose, ref failures);
        _owned.Clear();
        if (Planner is { } planner) Step(planner.Clear, ref failures);
        if (_wired)
        {
            Commands.Submitted -= OnSubmitted;
            Bridge.PrefillRequested -= OnPrefillRequested;
            Bridge.StopConfirmationRequested -= OnStopConfirmationRequested;
            Comms.Transcript.Changed -= CrewFeeds.OnTranscriptChanged;
            _wired = false;
        }
        if (Workspaces is { } workspaces) Step(workspaces.Dispose, ref failures);
        if (CommsHost is { } commsHost) Step(commsHost.Dispose, ref failures);
        if (Comms is { } comms) Step(comms.Dispose, ref failures);
        if (_speech is { } speech) Step(speech.Silence, ref failures);
        if (Console is { } console) Step(console.Dispose, ref failures);
        if (AudioCues is { } cues) Step(cues.Reset, ref failures);
        if (failures is not null) throw new AggregateException("Mission runtime teardown failed.", failures);
    }

    private static void Step(Action step, ref List<Exception>? failures)
    {
        try
        {
            step();
        }
        catch (Exception ex)
        {
            (failures ??= []).Add(ex);
        }
    }

    /// <summary>Stale badges age from each readout's observation MET; the console view model does not track MET itself.</summary>
    private void SetReadoutTimes(MissionTime now)
    {
        var tiles = Console.Resources.Tiles;
        for (var i = 0; i < tiles.Count; i++)
        {
            tiles[i].Primary.Readout.SetMissionTime(now);
            tiles[i].Secondary.Readout.SetMissionTime(now);
        }
        foreach (var tab in Console.Systems.Tabs)
        {
            var groups = tab.ReadoutGroups;
            for (var g = 0; g < groups.Count; g++)
            {
                var readouts = groups[g].Readouts;
                for (var r = 0; r < readouts.Count; r++) readouts[r].Readout.SetMissionTime(now);
            }
        }
    }

    // The rails always let these through: a read never changes the vehicle, and ACK only silences an alarm.
    private static bool IsHarmless(MissionSession session, ActionId action) =>
        session.Actions.TryGet(action, out var catalog) && catalog.Definition is { Kind: Usa.Simulation.Commands.Catalog.ActionKind.GlanceRead } or { Meta: Usa.Simulation.Commands.Catalog.MetaCommandKind.AcknowledgeAlarms };

    private static string Word(ITextSource strings, string key)
    {
        try
        {
            return strings.Get(key);
        }
        catch (KeyNotFoundException)
        {
            return string.Empty;
        }
    }

    private void OnMissionEnded(MissionEnded ended)
    {
        if (Closing is not null) return;
        var names = new CrewNames(Session.Data.Crew);
        var lost = new List<string>();
        foreach (var id in Session.Data.Scenario.Mission.Crew)
        {
            var alive = ended.Outcome != MissionOutcomeKind.AllCrewLost &&
                Session.State.TryGet($"crew.{id.Value}.alive", out var value) && value.Kind == StateValueKind.Bool && value.AsBool();
            if (!alive) lost.Add(names.FullName(id));
        }
        Closing = MissionClosingCard.Build(ended.Outcome, ended.Time, lost, Strings);
        ClosingShown?.Invoke(Closing);
        if (_proceedPending)
        {
            _proceedPending = false;
            ProceedToDebrief();
        }
    }

    private void OnSubmitted(CommandRequest request, ValidationResult result) => CommandSubmitted?.Invoke(request, result);

    private void OnPrefillRequested(CommsPrefill prefill) => CommsPrefillRequested?.Invoke(prefill);

    private void OnStopConfirmationRequested(TaskId task) => StopConfirmationRequested?.Invoke(task);

    /// <summary>The binding's own Dispose is not idempotent; the caller and the context may both dispose it.</summary>
    private sealed class OnceDisposable(IDisposable inner) : IDisposable
    {
        private IDisposable? _inner = inner;

        public void Dispose() => Interlocked.Exchange(ref _inner, null)?.Dispose();
    }
}
