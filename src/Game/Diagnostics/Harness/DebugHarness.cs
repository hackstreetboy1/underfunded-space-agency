using System.Globalization;
using System.Text;
using Godot;
using Usa.Game.Bootstrap;
using Usa.Game.Comms;
using Usa.Game.Flow;
using Usa.Game.Hosting;
using Usa.Game.Hosting.Nodes;
using Usa.Game.Integration;
using Usa.Game.Integration.Runtime;
using Usa.Game.Integration.Runtime.Hosting;
using Usa.Simulation.Commands;
using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;
using Usa.Simulation.Session;
using Usa.Simulation.Vehicle;

namespace Usa.Game.Diagnostics.Harness;

/// <summary>
/// Debug harness (ROADMAP P1 step 16, temporary until the P2 console): starts, restarts and abandons missions through the
/// screen router, so <see cref="MissionFlowHost"/> runs them and composes their <see cref="MissionRuntimeContext"/>. Shows
/// state, knowledge and events, a command box that submits <see cref="CommandRequest"/>s by action id through the runtime's
/// single submission path, and the composed runtime in plain controls: the console snapshot (knowledge only), the comms
/// transcript with a text input through <see cref="CommsOrchestrator"/>, the tip card, the entry HUD and the planner's engines.
/// </summary>
public sealed partial class DebugHarness : Control
{
    private const double RefreshIntervalS = 0.25;
    private const int MaxLogLines = 400;
    private const int MaxTranscriptLines = 60;
    private const string AutoAssignee = "(auto)";
    private const string CommandPrefix = "--harness-command=";
    private const string SayPrefix = "--harness-say=";
    private const string RestartPrefix = "--harness-restart-at=";
    private const string ClearOutagePrefix = "--harness-clear-outage-at=";

    private readonly Queue<string> _logLines = new();
    private readonly List<string> _pendingCommands = [];
    private readonly List<string> _pendingSays = [];
    private GameServices _services = null!;
    private MissionFlowHost _flow = null!;
    private SimulationHost _host = null!;
    private OptionButton _missionPicker = null!;
    private OptionButton _difficultyPicker = null!;
    private SpinBox _seed = null!;
    private Button _restart = null!;
    private Button _stop = null!;
    private CheckButton _menuFreeze = null!;
    private CheckButton _truth = null!;
    private CheckBox _verboseEvents = null!;
    private Label _status = null!;
    private Label _compression = null!;
    private Button _advance = null!;
    private Button _cancelAdvance = null!;
    private TreeTable _stateTable = null!;
    private TreeTable _knowledgeTable = null!;
    private RichTextLabel _eventLog = null!;
    private OptionButton _action = null!;
    private OptionButton _assignee = null!;
    private Label _actionHint = null!;
    private LineEdit _args = null!;
    private Button _submit = null!;
    private Button _confirm = null!;
    private Label _commandResult = null!;
    private RichTextLabel _consoleText = null!;
    private RichTextLabel _transcript = null!;
    private LineEdit _commsInput = null!;
    private Button _commsSend = null!;
    private Label _commsPending = null!;
    private Button _commsConfirm = null!;
    private Button _commsCancel = null!;
    private Label _tipText = null!;
    private Button _tipDismiss = null!;
    private Button _tipStronger = null!;
    private Label _entryText = null!;
    private Label _plannerText = null!;

    private MissionRuntime? _runtime;
    private MissionRuntimeContext? _context;
    private IDisposable? _eventRegistration;
    private CommandRequest? _pendingConfirmation;
    private ConfirmationRequest? _commsConfirmation;
    private double _sinceRefreshS = RefreshIntervalS;
    private double? _restartAtS;
    private double? _clearOutageAtS;
    private bool _outageClearedThisRun;
    private double _runningS;
    private bool _logDirty;
    private bool _transcriptDirty;
    private int _hiddenEvents;
    private int _contextsComposed;

    public override void _Ready()
    {
        _services = GetNode<GameRoot>("/root/GameRoot").Services;
        _flow = _services.Flow;
        _host = _flow.Host;
        _missionPicker = GetNode<OptionButton>("%Mission");
        _difficultyPicker = GetNode<OptionButton>("%Difficulty");
        _seed = GetNode<SpinBox>("%Seed");
        _restart = GetNode<Button>("%Restart");
        _stop = GetNode<Button>("%Stop");
        _menuFreeze = GetNode<CheckButton>("%MenuFreeze");
        _truth = GetNode<CheckButton>("%Truth");
        _verboseEvents = GetNode<CheckBox>("%VerboseEvents");
        _status = GetNode<Label>("%Status");
        _compression = GetNode<Label>("%Compression");
        _advance = GetNode<Button>("%Advance");
        _cancelAdvance = GetNode<Button>("%CancelAdvance");
        _stateTable = new TreeTable(GetNode<Tree>("%StateTable"), "Reading", "Value", "Detail");
        _knowledgeTable = new TreeTable(GetNode<Tree>("%KnowledgeTable"), KnowledgeColumns(truth: false));
        _eventLog = GetNode<RichTextLabel>("%EventLog");
        _action = GetNode<OptionButton>("%Action");
        _assignee = GetNode<OptionButton>("%Assignee");
        _actionHint = GetNode<Label>("%ActionHint");
        _args = GetNode<LineEdit>("%Args");
        _submit = GetNode<Button>("%Submit");
        _confirm = GetNode<Button>("%Confirm");
        _commandResult = GetNode<Label>("%CommandResult");
        _consoleText = GetNode<RichTextLabel>("%ConsoleText");
        _transcript = GetNode<RichTextLabel>("%Transcript");
        _commsInput = GetNode<LineEdit>("%CommsInput");
        _commsSend = GetNode<Button>("%CommsSend");
        _commsPending = GetNode<Label>("%CommsPending");
        _commsConfirm = GetNode<Button>("%CommsConfirm");
        _commsCancel = GetNode<Button>("%CommsCancel");
        _tipText = GetNode<Label>("%TipText");
        _tipDismiss = GetNode<Button>("%TipDismiss");
        _tipStronger = GetNode<Button>("%TipStronger");
        _entryText = GetNode<Label>("%EntryText");
        _plannerText = GetNode<Label>("%PlannerText");

        // Truth must never reach a release build.
        _truth.Visible = OS.IsDebugBuild();

        foreach (var id in _services.Missions.MissionIds) _missionPicker.AddItem(id);
        foreach (var id in _services.Missions.DifficultyIds) _difficultyPicker.AddItem(id);
        _ = Select(_difficultyPicker, "normal");

        GetNode<Button>("%Start").Pressed += StartFromPickers;
        _restart.Pressed += Restart;
        _stop.Pressed += StopMission;
        _menuFreeze.Toggled += SetMenuFreeze;
        GetNode<Button>("%ClearOutage").Pressed += ClearOutage;
        _truth.Toggled += _ => RefreshTables();
        _advance.Pressed += () => Log(_host.RequestAdvanceToNextEvent() ? "» advancing to next event" : "» advance not allowed now");
        _cancelAdvance.Pressed += _host.CancelAdvanceToNextEvent;
        _action.ItemSelected += _ => UpdateActionHint();
        _submit.Pressed += SubmitCommand;
        _args.TextSubmitted += _ => SubmitCommand();
        _confirm.Pressed += ConfirmCommand;
        _commsSend.Pressed += SendComms;
        _commsInput.TextSubmitted += _ => SendComms();
        _commsConfirm.Pressed += () => CloseCommsConfirmation(confirm: true);
        _commsCancel.Pressed += () => CloseCommsConfirmation(confirm: false);
        _tipDismiss.Pressed += () => Log(_context?.Console.Tip.Dismiss() == true ? "» tip dismissed" : "» no expanded tip to dismiss");
        _tipStronger.Pressed += () => Log(_context?.Console.Tip.RequestStrongerHint() == true ? "» stronger hint requested" : "» no stronger hint now");

        _flow.MissionStarted += OnMissionStarted;
        _flow.MissionStopped += OnMissionStopped;
        _flow.ContextChanged += OnContextChanged;
        _services.Router.Rejected += OnRouterRejected;
        if (_flow.Runtime is { } running) OnMissionStarted(running);
        OnContextChanged(_flow.Context);

        SetSessionControls(running: _runtime is not null);
        Log("» pick a mission, difficulty and seed, then Start");
        Callable.From(RunCommandLine).CallDeferred();
    }

    public override void _ExitTree()
    {
        _flow.MissionStarted -= OnMissionStarted;
        _flow.MissionStopped -= OnMissionStopped;
        _flow.ContextChanged -= OnContextChanged;
        _services.Router.Rejected -= OnRouterRejected;
        OnContextChanged(null);
        _eventRegistration?.Dispose();
        _eventRegistration = null;
    }

    /// <summary>
    /// Unattended use (captures, quick checks): <c>-- --harness-autostart</c> starts the picked mission;
    /// <c>--harness-command=action_id:name=value,name=value</c> submits that command once it runs (confirming it if asked);
    /// <c>--harness-say=text</c> sends that line through comms; <c>--harness-restart-at=30</c> restarts after 30 s running;
/// <c>--harness-clear-outage-at=12</c> clears the outage freeze after 12 s running (again after each restart).
    /// </summary>
    private void RunCommandLine()
    {
        var args = OS.GetCmdlineUserArgs();
        if (!args.Contains("--harness-autostart")) return;
        if (args.Contains("--harness-truth")) _truth.ButtonPressed = true;
        _pendingCommands.AddRange(args.Where(a => a.StartsWith(CommandPrefix, StringComparison.Ordinal)).Select(a => a[CommandPrefix.Length..]));
        _pendingSays.AddRange(args.Where(a => a.StartsWith(SayPrefix, StringComparison.Ordinal)).Select(a => a[SayPrefix.Length..]));
        _restartAtS = SecondsArg(args, RestartPrefix);
        _clearOutageAtS = SecondsArg(args, ClearOutagePrefix);
        StartFromPickers();
    }

    private static double? SecondsArg(string[] args, string prefix)
    {
        var arg = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal));
        return arg is not null && double.TryParse(arg[prefix.Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ? seconds : null;
    }

    private void RunPendingCommandLine()
    {
        if (_context is null) return;
        foreach (var spec in _pendingCommands)
        {
            var colon = spec.IndexOf(':', StringComparison.Ordinal);
            var actionId = colon < 0 ? spec : spec[..colon];
            if (!Select(_action, actionId))
            {
                Log($"» unknown action '{actionId}'");
                continue;
            }
            UpdateActionHint();
            _args.Text = colon < 0 ? "" : spec[(colon + 1)..];
            SubmitCommand();
            if (_pendingConfirmation is not null) ConfirmCommand();
        }
        _pendingCommands.Clear();
        foreach (var line in _pendingSays)
        {
            _commsInput.Text = line;
            SendComms();
        }
        _pendingSays.Clear();
    }

    public override void _Process(double delta)
    {
        UpdateCompressionReadout();
        if (_runtime is not null) _runningS += delta;
        if (_restartAtS is { } restartAt && _runningS >= restartAt && _context is not null)
        {
            _restartAtS = null;
            Restart();
        }
        if (_clearOutageAtS is { } clearAt && _runningS >= clearAt && !_outageClearedThisRun && _services.Connection.IsLost)
        {
            _outageClearedThisRun = true;
            ClearOutage();
        }
        _sinceRefreshS += delta;
        if (_sinceRefreshS < RefreshIntervalS) return;
        _sinceRefreshS = 0;
        RefreshTables();
        RefreshRuntime();
        FlushLog();
    }

    private void StartFromPickers()
    {
        if (_missionPicker.Selected < 0 || _difficultyPicker.Selected < 0)
        {
            Log("» no mission or difficulty to start");
            return;
        }
        var router = _services.Router;
        if (router.MissionInProgress) StopMission();
        while (router.Current is Screen.Briefing or Screen.MissionSelect or Screen.Settings or Screen.Credits or Screen.Encyclopedia)
        {
            if (!router.Back()) break;
        }
        var selection = new MissionSelection(_missionPicker.GetItemText(_missionPicker.Selected), _difficultyPicker.GetItemText(_difficultyPicker.Selected), (ulong)_seed.Value);
        var started = router.OpenMissionSelect() && router.SelectMission(selection) && router.StartMission(guidedFirstRun: false);
        Log(started ? $"» loading {selection.MissionId}/{selection.Difficulty} seed {selection.Seed}" : $"» the router refused to start (on {router.Current})");
        FlushLog();
    }

    private void Restart()
    {
        var router = _services.Router;
        if (!router.MissionInProgress) return;
        if (router.ConnectionOverlayVisible) ClearOutage();
        if (router.EscMenu == EscMenuState.Closed) router.OpenEscMenu();
        Log(router.RequestRestart() && router.ConfirmEscAction() ? "» restarting (same seed)" : $"» restart refused (router on {router.Current})");
    }

    private void StopMission()
    {
        var router = _services.Router;
        if (!router.MissionInProgress && _flow.Runtime is null) return;
        if (router.ConnectionOverlayVisible) ClearOutage();
        if (router.EscMenu == EscMenuState.Closed) router.OpenEscMenu();
        if (!(router.RequestAbandon() && router.ConfirmEscAction()))
        {
            Log($"» abandon through the Esc menu refused (router on {router.Current}); abandoning the launch directly");
            _flow.Launcher.Abandon();
        }
    }

    /// <summary>Clears the connection-outage freeze (router overlay and freeze reason) and lets comms and speech resume.</summary>
    private void ClearOutage()
    {
        _services.Router.OnConnectionRestored();
        _services.Speech.SetPaused(false);
        _context?.Comms.OnConnectionRestored();
        Log("» outage freeze cleared (the backend may still be offline; comms falls back to its templates)");
    }

    // The router is the single writer of freeze reasons (it keeps its own copy of them), so the menu freeze goes through the
    // Esc menu the way the player would freeze the game.
    private void SetMenuFreeze(bool frozen)
    {
        var router = _services.Router;
        var done = frozen ? router.EscMenu != EscMenuState.Closed || router.OpenEscMenu() : router.EscMenu == EscMenuState.Closed || router.ResumeMission();
        Log(done ? $"» Esc menu {(frozen ? "opened (menu freeze)" : "closed")}" : $"» menu freeze refused (router on {router.Current})");
        _menuFreeze.SetPressedNoSignal(router.EscMenu != EscMenuState.Closed);
    }

    private void OnRouterRejected(FlowRejection rejection) => Log($"» router: {rejection.Action} refused on {rejection.Screen}: {rejection.Reason}");

    private void OnMissionStarted(MissionRuntime runtime)
    {
        _runtime = runtime;
        _runningS = 0;
        _outageClearedThisRun = false;
        _hiddenEvents = 0;
        _eventRegistration?.Dispose();
        _eventRegistration = runtime.Events.Register<ISimEvent>(OnSimEvent);
        PopulateCommandPickers(runtime.Mission.Session);
        SetSessionControls(running: true);
        Log($"» started {runtime.Mission.Request} ({runtime.Mission.Session.SessionId}) at MET {runtime.Mission.Session.Now}");
        RefreshTables();
    }

    private void OnMissionStopped(MissionRuntime runtime)
    {
        _eventRegistration?.Dispose();
        _eventRegistration = null;
        var session = runtime.Mission.Session;
        Log($"» stopped {runtime.Mission.Request} at MET {session.Now}, final hash {session.StateHash()[..12]}");
        if (_runtime == runtime) _runtime = null;
        _pendingConfirmation = null;
        if (IsInsideTree()) SetSessionControls(running: _runtime is not null);
    }

    private void OnContextChanged(MissionRuntimeContext? context)
    {
        if (_context is { } previous)
        {
            previous.Comms.ConfirmationRequested -= OnCommsConfirmationRequested;
            previous.Comms.ConfirmationClosed -= OnCommsConfirmationClosed;
            previous.Comms.Transcript.Changed -= OnTranscriptChanged;
            previous.CommandSubmitted -= OnContextCommandSubmitted;
        }
        _context = context;
        _commsConfirmation = null;
        _transcriptDirty = true;
        if (context is null) return;

        _contextsComposed++;
        context.Comms.ConfirmationRequested += OnCommsConfirmationRequested;
        context.Comms.ConfirmationClosed += OnCommsConfirmationClosed;
        context.Comms.Transcript.Changed += OnTranscriptChanged;
        context.CommandSubmitted += OnContextCommandSubmitted;
        Log($"» runtime composed (#{_contextsComposed}): comms host {(context.CommsHost is null ? "none" : "attached")}, {context.Planner.Engines.Count} planner engine(s)");
        Callable.From(RunPendingCommandLine).CallDeferred();
    }

    private void OnTranscriptChanged(TranscriptChange change) => _transcriptDirty = true;

    private void OnCommsConfirmationRequested(ConfirmationRequest request)
    {
        _commsConfirmation = request;
        Log($"» comms asks to confirm: {request.Title}");
    }

    private void OnCommsConfirmationClosed(ConfirmationClosed closed)
    {
        if (_commsConfirmation?.Id == closed.Request.Id) _commsConfirmation = null;
        Log($"» comms confirmation {closed.Request.Title}: {closed.Outcome}");
    }

    private void OnContextCommandSubmitted(CommandRequest request, ValidationResult result) =>
        Log($"» console/workspace {request.Id.Value} {request.Action.Value}: {result.Status}");

    private void SendComms()
    {
        var text = _commsInput.Text.Trim();
        if (_context is not { } context || text.Length == 0) return;
        var id = context.CommsHost is CommsControllerHost host ? host.Controller.SendText(text) : context.Comms.SubmitText(text, addressee: null);
        Log(id is { } commandId ? $"» comms sent '{text}' as {commandId.Value}" : $"» comms held or refused '{text}'");
        _commsInput.Text = "";
        _transcriptDirty = true;
    }

    private void CloseCommsConfirmation(bool confirm)
    {
        if (_context is not { } context || _commsConfirmation is not { } request) return;
        var done = confirm ? context.Comms.Confirm(request.Id) : context.Comms.CancelConfirmation(request.Id);
        if (!done) Log($"» comms confirmation {request.Id} no longer open");
    }

    private void OnSimEvent(ISimEvent simEvent)
    {
        if (!_verboseEvents.ButtonPressed && simEvent is ComponentStateChanged or ObservationRecorded)
        {
            _hiddenEvents++;
            return;
        }
        Log(simEvent.ToString() ?? simEvent.GetType().Name);
    }

    private void SubmitCommand()
    {
        if (_runtime is not { } runtime || _action.Selected < 0) return;
        var session = runtime.Mission.Session;
        var actionId = new ActionId(_action.GetItemText(_action.Selected));
        session.Actions.TryGet(actionId, out var action);
        if (!HarnessCommandArgs.TryParse(_args.Text, action, out var args, out var error))
        {
            _commandResult.Text = $"Arguments: {error}";
            return;
        }

        var assigneeText = _assignee.GetItemText(_assignee.Selected);
        CrewId? assignee = assigneeText == AutoAssignee ? null : new CrewId(assigneeText);
        var id = _context?.Commands.NextId() ?? session.NextCommandId();
        var request = new CommandRequest(id, CommandSource.Text, actionId, assignee, args, Confirmed: false);
        Report(request, Submit(request));
    }

    private void ConfirmCommand()
    {
        if (_runtime is null || _pendingConfirmation is not { } pending) return;
        _pendingConfirmation = null;
        var confirmed = pending with { Confirmed = true };
        Report(confirmed, Submit(confirmed));
    }

    private ValidationResult Submit(CommandRequest request) =>
        _context is { } context ? context.Commands.Submit(request) : _runtime!.Mission.Port.Submit(request);

    private void Report(CommandRequest request, ValidationResult result)
    {
        var text = new StringBuilder($"{request.Id.Value} {request.Action.Value}: {result.Status}");
        if (result.ReasonCode is { } reason) text.Append(CultureInfo.InvariantCulture, $" ({reason})");
        if (result.Detail is { } detail) text.Append(CultureInfo.InvariantCulture, $" - {detail}");
        if (result.Impact is { } impact)
        {
            text.Append(CultureInfo.InvariantCulture, $"\nImpact {impact.Impact}");
            if (impact.EffectTextKeys.Count > 0) text.Append(CultureInfo.InvariantCulture, $"; effects: {string.Join(", ", impact.EffectTextKeys)}");
            foreach (var change in impact.ObjectiveChanges) text.Append(CultureInfo.InvariantCulture, $"; {change.Objective.Value}: {change.CurrentState} -> {change.PredictedState}");
        }

        _pendingConfirmation = result.Status == ValidationStatus.NeedsConfirmation ? request : null;
        _confirm.Disabled = _pendingConfirmation is null;
        _commandResult.Text = text.ToString();
        Log($"» {text.ToString().Replace('\n', ' ')}");
        RefreshTables();
    }

    private void PopulateCommandPickers(MissionSession session)
    {
        _action.Clear();
        foreach (var action in session.Actions.Actions.OrderBy(a => a.Id.Value, StringComparer.Ordinal)) _action.AddItem(action.Id.Value);
        _assignee.Clear();
        _assignee.AddItem(AutoAssignee);
        _assignee.AddItem(CommandRequest.AnyCrew.Value);
        foreach (var crew in session.Snapshot().Crew) _assignee.AddItem(crew.Id.Value);
        UpdateActionHint();
    }

    private void UpdateActionHint()
    {
        if (_runtime is not { } runtime || _action.Selected < 0 || !runtime.Mission.Session.Actions.TryGet(new ActionId(_action.GetItemText(_action.Selected)), out var action))
        {
            _actionHint.Text = "";
            return;
        }
        var parameters = action.Parameters.Count == 0 ? "no arguments" : string.Join("  ", action.Parameters.Select(Describe));
        _actionHint.Text = $"{action.Kind}, impact {action.BaseImpact}: {parameters}";
    }

    private static string Describe(ActionParameter parameter)
    {
        var range = parameter.Values is { Count: > 0 } values
            ? $" [{string.Join("|", values.Take(6))}{(values.Count > 6 ? "|…" : "")}]"
            : parameter.Min is not null || parameter.Max is not null ? $" [{parameter.Min}..{parameter.Max}]" : "";
        return $"{parameter.Name}:{parameter.Type}{range}";
    }

    private void RefreshTables()
    {
        var router = _services.Router;
        _menuFreeze.SetPressedNoSignal(router.EscMenu != EscMenuState.Closed);
        if (_runtime is not { } runtime)
        {
            _status.Text = $"No session (router on {router.Current}). Freeze: {_services.Freeze.Reasons.Value}. Backend {(_services.Connection.IsLost ? "LOST" : "ok/unknown")}.";
            return;
        }

        var truth = _truth.Visible && _truth.ButtonPressed;
        var session = runtime.Mission.Session;
        var snapshot = runtime.Mission.Port.Snapshot();

        _stateTable.Begin();
        var vehicle = session.Data.CrewedVehicleId;
        foreach (var row in HarnessStateRows.Build(snapshot, session.State, truth, vehicle)) _stateTable.Row(row.Key, row.Label, row.Value, row.Detail);
        _stateTable.End();

        _knowledgeTable.SetColumns(KnowledgeColumns(truth));
        _knowledgeTable.Begin();
        var readings = HarnessStateRows.Readings(vehicle);
        foreach (var fact in snapshot.Facts) _knowledgeTable.Row(fact.Id.Value, KnowledgeCells(session, fact, truth, readings));
        _knowledgeTable.End();

        var report = _services.Connection.LastReport;
        _status.Text = $"{session.SessionId} | router {router.Current}{(router.ConnectionOverlayVisible ? " (outage overlay)" : "")} | " +
            $"{(snapshot.IsFrozen ? $"FROZEN ({snapshot.FreezeReasons})" : "running")} | " +
            $"backend {report.Backend}, language {report.Language}, voice {report.Voice}{(_services.Connection.IsLost ? " (LOST)" : "")}" +
            $" | runtimes composed {_contextsComposed}" +
            (_hiddenEvents > 0 ? $" | {_hiddenEvents} component/observation events hidden" : "");
        if (_pendingConfirmation is null) _confirm.Disabled = true;
    }

    private void RefreshRuntime()
    {
        if (_context is not { } context || context.IsDisposed)
        {
            _consoleText.Text = "No composed runtime.";
            if (_transcriptDirty) _transcript.Text = "";
            _transcriptDirty = false;
            _tipText.Text = "";
            _entryText.Text = "";
            _plannerText.Text = "";
            _commsPending.Text = HarnessRuntimeText.Confirmation(null);
            SetRuntimeControls(enabled: false);
            return;
        }

        SetRuntimeControls(enabled: true);
        var console = context.LatestConsoleSnapshot;
        _consoleText.Text = HarnessRuntimeText.Console(console);
        if (_transcriptDirty)
        {
            _transcriptDirty = false;
            _transcript.Text = HarnessRuntimeText.Transcript(context.Transcript, MaxTranscriptLines);
        }
        var now = console?.Met ?? context.Session.Now;
        _tipText.Text = HarnessRuntimeText.Tip(console?.Tip, console?.TipCooldownUntil, now) + $"\ncard {context.Console.Tip.State.Value}";
        _tipDismiss.Disabled = console?.Tip is null;
        _tipStronger.Disabled = !context.Console.Tip.CanStrengthen.Value;
        _entryText.Text = HarnessRuntimeText.EntryHud(context.EntryHud, context.Splashdown);
        _plannerText.Text = HarnessRuntimeText.Planner(context.Planner);
        _commsPending.Text = HarnessRuntimeText.Confirmation(_commsConfirmation);
        _commsConfirm.Disabled = _commsConfirmation is null;
        _commsCancel.Disabled = _commsConfirmation is null;
    }

    private void SetRuntimeControls(bool enabled)
    {
        _commsSend.Disabled = !enabled;
        _commsInput.Editable = enabled;
        if (enabled) return;
        _commsConfirm.Disabled = true;
        _commsCancel.Disabled = true;
        _tipDismiss.Disabled = true;
        _tipStronger.Disabled = true;
    }

    private static string[] KnowledgeColumns(bool truth) =>
        truth ? ["Fact", "State", "Known value", "Source", "MET", "Truth"] : ["Fact", "State", "Known value", "Source", "MET"];

    private static string[] KnowledgeCells(MissionSession session, FactView fact, bool truth, IReadOnlyList<(string Key, string Label, string TruthPath, string FactId, string Unit)> readings)
    {
        var known = session.Knowledge.Describe(fact.Id);
        var latest = known.Latest ?? known.Estimate;
        var source = latest is null ? "" : $"{latest.Method}{(latest.Observer is { } observer ? $" / {observer.Value}" : "")}";
        var value = HarnessStateRows.KnownValue(fact);
        if (fact.Unit.Length > 0 && value.Length > 0) value = $"{value} {fact.Unit}";
        if (fact.GroundBand is { Length: > 0 } band) value = value.Length > 0 ? $"{value} (ground: {band})" : $"ground: {band}";
        var met = fact.ObservedAt?.ToString() ?? "";
        return truth
            ? [$"{fact.Id.Value}  {fact.Label}", fact.State, value, source, met, TruthOf(session, fact.Id, readings)]
            : [$"{fact.Id.Value}  {fact.Label}", fact.State, value, source, met];
    }

    private static string TruthOf(MissionSession session, FactId fact, IReadOnlyList<(string Key, string Label, string TruthPath, string FactId, string Unit)> readings)
    {
        foreach (var reading in readings)
        {
            if (reading.FactId == fact.Value) return $"{HarnessStateRows.Truth(session.State, reading.TruthPath)} {reading.Unit}";
        }
        return session.Data.Facts.TryResolve(fact, out var definition) && definition.SourceComponent is { } component
            ? $"{component.Value}: {HarnessStateRows.Truth(session.State, $"{component.Value}.condition")}"
            : "";
    }

    private void UpdateCompressionReadout()
    {
        if (!_host.IsRunning)
        {
            _compression.Text = "Compression: no session";
            _advance.Disabled = true;
            _cancelAdvance.Disabled = true;
            return;
        }
        var frame = _host.LastFrame;
        _compression.Text =
            $"Compression x{_host.CompressionRate:0.##} ({_host.CompressionReason}), effective x{_host.EffectiveRate:0.##} | " +
            $"frame {frame.TicksRun} ticks{(frame.BudgetExhausted ? ", over budget" : "")}{(frame.DroppedMs > 0 ? $", dropped {frame.DroppedMs:0} ms" : "")}" +
            $"{(frame.Frozen ? ", frozen" : "")} | MET {frame.MetAfter}{(_host.IsAdvancingToEvent ? " | advancing to next event" : "")}";
        _advance.Disabled = !_host.CanAdvanceToNextEvent;
        _cancelAdvance.Disabled = !_host.IsAdvancingToEvent;
    }

    private void SetSessionControls(bool running)
    {
        _restart.Disabled = !running;
        _stop.Disabled = !running;
        _submit.Disabled = !running;
        _confirm.Disabled = true;
        if (!running)
        {
            _action.Clear();
            _assignee.Clear();
            _actionHint.Text = "";
        }
    }

    private void Log(string line)
    {
        _logLines.Enqueue(line);
        while (_logLines.Count > MaxLogLines) _logLines.Dequeue();
        _logDirty = true;
    }

    private void FlushLog()
    {
        if (!_logDirty) return;
        _logDirty = false;
        _eventLog.Text = string.Join('\n', _logLines);
    }

    private static bool Select(OptionButton picker, string text)
    {
        for (var i = 0; i < picker.ItemCount; i++)
        {
            if (picker.GetItemText(i) != text) continue;
            picker.Select(i);
            return true;
        }
        return false;
    }
}
