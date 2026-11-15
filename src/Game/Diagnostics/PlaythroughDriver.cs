using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Godot;
using Usa.Game.Bootstrap;
using Usa.Game.Flow;
using Usa.Game.Integration;
using Usa.Game.Presentation.Console.MainViews;
using Usa.Game.Services.Data;
using Usa.Game.Services.Saves;
using Usa.Game.UI.Console;
using Usa.Game.UI.Frontend;
using Usa.Simulation.Commands;

using Usa.Simulation.Session.Scripts;

namespace Usa.Game.Diagnostics;

/// <summary>
/// Plays a scenario script (tests/scenarios/*.scenario.json) through the real front end and mission console
/// (diagnostics/playable/Playthrough.tscn). The script is first played headless with <see cref="ScenarioPlayer"/>; its command
/// log and event times then give checkpoints. Each checkpoint is entered the way a player continues a saved run (a resume
/// save replayed to that MET through <see cref="ScreenRouter.Continue"/>), shown in the console view it is about, captured,
/// and abandoned through the Esc menu. The last checkpoint (after splashdown) runs live, sped up, until the debrief.
/// User arguments after <c>--</c>: <c>--scenario=path_a_easy</c>, <c>--out=&lt;folder&gt;</c> (screenshots; omit headless),
/// <c>--speed=20</c> (Engine.TimeScale for the live end). Prints <c>[Play]</c> lines; exits 1 on a stalled or refused step.
/// </summary>
public sealed partial class PlaythroughDriver : Node
{
    private const string MainScene = "res://ui/screens/Main.tscn";
    private const double StepTimeoutS = 240;
    private const double ShotDelayS = 2.5;
    private const string MissionSessionRegisterDesign = Usa.Simulation.Session.MissionSession.RegisterDesignAction;

    private readonly Queue<Stage> _stages = new();
    private GameRoot _root = null!;
    private ScenarioResult _played = null!;
    private string _out = "";
    private string _only = "";
    private double _lastAdvanceS = -10;
    private bool _layoutDump;
    private bool _overlap;
    private bool _guided;
    private double _guidedSpeed = 8;
    private double _guidedReactionS = 1.1;
    private bool _guidedCommsCheck;
    private bool _guidedExternal;
    private double _lastProceedS = -10;
    private double _speed = 20;
    private Stage? _stage;
    private double _stageS;
    private bool _done;
    private int _shot;
    private int _failures;

    private enum Phase { Waiting, Settling, Leaving }

    private sealed record Checkpoint(string Name, long MetMs, MainViewKind? View, bool RunToDebrief = false);

    private sealed class Stage(string name, Func<bool> start, Func<bool> ready, Action? onReady = null, string? capture = null, Func<bool>? leave = null, double delayS = ShotDelayS, double timeoutS = StepTimeoutS)
    {
        public string Name { get; } = name;
        public Func<bool> Start { get; } = start;
        public Func<bool> Ready { get; } = ready;
        public Action? OnReady { get; } = onReady;
        public string? Capture { get; } = capture;
        public Func<bool>? Leave { get; } = leave;
        public double DelayS { get; } = delayS;
        public double TimeoutS { get; } = timeoutS;
        public Phase Phase { get; set; }
        public double PhaseS { get; set; }
    }

    public override void _Ready()
    {
        if (GetNodeOrNull<GameRoot>("/root/GameRoot") is not { IsComposed: true } root)
        {
            Finish("GameRoot autoload missing or not composed");
            return;
        }

        _root = root;
        var args = OS.GetCmdlineUserArgs()
            .Select(a => a.TrimStart('-').Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal);
        var scenario = args.GetValueOrDefault("scenario", "path_a_easy");
        _out = args.GetValueOrDefault("out", "");
        _only = args.GetValueOrDefault("only", "");
        _layoutDump = args.ContainsKey("layout");
        // --window=1280x720: the review size. Godot 4.7.2 ignores --windowed/--resolution when the project starts
        // fullscreen, so the driver sets the window itself once the game has applied its display setting.
        if (args.TryGetValue("window", out var window) && window.Split('x') is [var w, var h]
            && int.TryParse(w, CultureInfo.InvariantCulture, out var width) && int.TryParse(h, CultureInfo.InvariantCulture, out var height))
        {
            Callable.From(() =>
            {
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetSize(new Vector2I(width, height));
                GD.Print($"[Play] window set to {DisplayServer.WindowGetSize()} (asked {width}x{height})");
            }).CallDeferred();
        }
        _overlap = args.ContainsKey("overlap");
        _guided = args.ContainsKey("guided");
        if (args.TryGetValue("guided-speed", out var gs) && double.TryParse(gs, NumberStyles.Float, CultureInfo.InvariantCulture, out var g)) _guidedSpeed = g;
        if (args.TryGetValue("reaction", out var rs) && double.TryParse(rs, NumberStyles.Float, CultureInfo.InvariantCulture, out var r)) _guidedReactionS = r;
        _guidedCommsCheck = args.ContainsKey("comms-check");
        _guidedExternal = args.ContainsKey("external");
        if (args.TryGetValue("speed", out var speed) && double.TryParse(speed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) _speed = parsed;

        var path = ProjectSettings.GlobalizePath($"res://tests/scenarios/{scenario}.scenario.json");
        var script = ScenarioScriptLoader.Load(path);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        _played = new ScenarioPlayer(new GodotDataSource()).Play(script);
        GD.Print($"[Play] {scenario}: headless reference run ended '{_played.Ended}' at {Met(_played.EndMetMs)}, grade {_played.Score.Grade}, " +
                 $"{_played.Log.Entries.Count} commands, {_played.Failures.Count} expectation failure(s) ({watch.Elapsed.TotalSeconds:0.0} s)");
        foreach (var failure in _played.Failures.Take(5)) GD.Print($"[Play]   reference: {failure}");

        Plan(script);
        Callable.From(OpenMain).CallDeferred();
    }

    public override void _Process(double delta)
    {
        if (_done || _root is null) return;
        if (GetTree().CurrentScene is not FrontendShell shell) return;
        if (shell.Context is null) shell.Initialize(_root.Services);

        if (_stage is null)
        {
            if (_stages.Count == 0)
            {
                Finish(_failures == 0 ? null : $"{_failures} stage(s) failed");
                return;
            }
            _stage = _stages.Dequeue();
            _stageS = 0;
            _stage.Phase = Phase.Waiting;
            _stage.PhaseS = 0;
            if (!_stage.Start())
            {
                Fail($"{_stage.Name}: refused to start (router on {_root.Services.Router.Current})");
                _stage = null;
            }
            return;
        }

        var stage = _stage;
        _stageS += delta / Math.Max(Engine.TimeScale, 1e-6);
        stage.PhaseS += delta / Math.Max(Engine.TimeScale, 1e-6);
        switch (stage.Phase)
        {
            case Phase.Waiting:
                if (stage.Ready())
                {
                    stage.OnReady?.Invoke();
                    stage.Phase = Phase.Settling;
                    stage.PhaseS = 0;
                    GD.Print($"[Play] {stage.Name}: ready after {_stageS:0.0} s ({Describe()})");
                }
                else if (_stageS > stage.TimeoutS)
                {
                    Fail($"{stage.Name}: not ready after {stage.TimeoutS:0} s ({Describe()})");
                    Abort();
                }
                else if ((int)(_stageS / 10) != (int)((_stageS - delta / Math.Max(Engine.TimeScale, 1e-6)) / 10))
                {
                    GD.Print($"[Play] {stage.Name}: waiting {_stageS:0} s ({Describe()})");
                }
                break;
            case Phase.Settling:
                if (stage.PhaseS < stage.DelayS) return;
                if (stage.Capture is { } name) Capture(name);
                stage.Phase = Phase.Leaving;
                stage.PhaseS = 0;
                if (stage.Leave is { } leave && !leave()) Fail($"{stage.Name}: could not leave ({Describe()})");
                break;
            case Phase.Leaving:
                if (_root.Services.Router.Current is Screen.MainMenu or Screen.Debrief || stage.Leave is null)
                {
                    _stage = null;
                }
                else if (stage.PhaseS > StepTimeoutS)
                {
                    Fail($"{stage.Name}: stuck leaving ({Describe()})");
                    Abort();
                }
                break;
        }
    }

    private void Plan(ScenarioScript script)
    {
        var router = _root.Services.Router;
        var keys = _played.EventKeys;
        long? At(string key, double offsetS) => keys.TryGetValue(key, out var ms) ? ms + (long)(offsetS * 1000) : null;
        long? Command(string action) =>
            _played.Log.Entries.FirstOrDefault(e => e.Request.Action.Value == action) is { } entry ? entry.MetMs : null;

        _stages.Enqueue(new Stage("main menu", () => true, () => router.Current == Screen.MainMenu, capture: _guided ? null : "01_main_menu"));
        _stages.Enqueue(new Stage(
            "mission select", router.OpenMissionSelect, () => router.Current == Screen.MissionSelect, capture: _guided ? null : "01b_mission_select", leave: router.Back));
        if (_only.Length == 0) _stages.Enqueue(new Stage(
            "briefing",
            () => router.OpenMissionSelect() && router.SelectMission(new MissionSelection(script.Mission, script.Difficulty, script.Seed)),
            () => router.Current == Screen.Briefing,
            capture: _guided ? null : "02_briefing",
            leave: () => router.Back() && router.Back()));

        if (_overlap) PlanOverlapScreens(script);
        if (_guided)
        {
            EnqueueGuidedRun(script);
            return;
        }

        var checkpoints = new List<Checkpoint>();
        void Add(string name, long? metMs, MainViewKind? view, bool run = false)
        {
            if (metMs is { } ms) checkpoints.Add(new Checkpoint(name, ms, view, run));
            else GD.Print($"[Play] {name}: no such moment in the reference run; skipped");
        }

        Add("03_console_incident", At("event:incident_i1", 30), MainViewKind.Orbit);
        Add("04_external_view", At("event:incident_i1", 40), MainViewKind.External);
        // EVA outside (before the package is in hand) and just after the retrieval, in the External view.
        if (Command("eva_retrieve_package") is { } eva && keys.TryGetValue("event:eva_package_retrieved", out var retrievedMs))
        {
            Add("04e_eva_outside", eva + (retrievedMs - eva) * 2 / 3, MainViewKind.External);
            Add("04f_eva_retrieved", retrievedMs + 5_000, MainViewKind.External);
        }
        // The undock separation (docked offset and blend start) in the External view.
        var undocked = keys.Keys.FirstOrDefault(k => k is "event:undocked" or "event:undock");
        if (undocked is not null)
        {
            foreach (var s in new[] { 0, 1, 2, 4 }) Add($"04u_undock_plus{s}s", At(undocked, s), MainViewKind.External);
        }
        if (Command(MissionSessionRegisterDesign) is { } design) Add("04r_workspace", design - 10_000, MainViewKind.Workspace);
        Add("05_map_planner", Command("retrofire") is { } retro ? retro - 30_000 : null, MainViewKind.Orbit);
        Add("06_entry_hud", At("event:entry_interface", 90), MainViewKind.External);
        Add("06p_peak_heating", At("event:entry_interface", 240), MainViewKind.External);
        Add("06s_splash", At("event:splashdown", -2), MainViewKind.External);
        Add("07_recovery_card", At("event:splashdown", 30), null);
        // A run that ends without a splashdown (crew lost) goes live from shortly before its end.
        Add("08_debrief", At("event:splashdown", 40) ?? Math.Max(0, _played.EndMetMs - 120_000), null, run: true);

        foreach (var checkpoint in checkpoints)
        {
            if (_only.Length == 0 || checkpoint.Name.Contains(_only, StringComparison.Ordinal)) Enqueue(checkpoint, script);
        }

        // The retrofire planned and ordered through the planner panel exactly as a player does it, then flown to the debrief.
        if (Command("retrofire") is { } retroOrder && (_only.Length == 0 || "09_ui_retrofire".Contains(_only, StringComparison.Ordinal)))
        {
            var zone = script.Commands.FirstOrDefault(c => c.Action == "retrofire")?.Zone ?? "";
            EnqueueUiRetrofire(Save(script, retroOrder - 90_000), zone);
        }
    }

    /// <summary>
    /// <c>--overlap=1</c>: the screens a layout review needs beyond the checkpoints — settings, the loading screen, the Esc menu
    /// over the console and the guided first run's callouts.
    /// </summary>
    private void PlanOverlapScreens(ScenarioScript script)
    {
        var router = _root.Services.Router;
        _stages.Enqueue(new Stage("settings", router.OpenSettings, () => router.Current == Screen.Settings, capture: "01c_settings", leave: router.Back));
        var incident = _played.EventKeys.TryGetValue("event:incident_i1", out var i1) ? i1 + 20_000 : _played.EndMetMs / 2;
        var save = Save(script, incident);
        _stages.Enqueue(new Stage(
            "loading", () => router.Continue(save), () => router.Current == Screen.Loading, capture: "02b_loading", leave: null, delayS: 0));
        _stages.Enqueue(new Stage(
            "esc menu",
            () => true,
            InConsole,
            onReady: () => router.OpenEscMenu(),
            capture: "03b_esc_menu",
            leave: Abandon));
        _stages.Enqueue(new Stage(
            "guided first run",
            () => router.OpenMissionSelect() && router.SelectMission(new MissionSelection(script.Mission, script.Difficulty, script.Seed))
                && router.StartMission(guidedFirstRun: true),
            InConsole,
            capture: "03c_tutorial_callouts",
            leave: Abandon,
            delayS: 8));
    }

    private void EnqueueUiRetrofire(ResumeSave save, string zone)
    {
        var router = _root.Services.Router;
        var step = 0;
        var stepS = 0.0;
        _stages.Enqueue(new Stage(
            $"09_ui_retrofire from {Met(save.Metadata.MetMs)} (zone {zone})",
            () =>
            {
                // The debrief leaves through Mission select; Back from there is the main menu, where Continue lives.
                if (router.Current == Screen.Debrief) router.OpenMissionSelect();
                if (router.Current == Screen.MissionSelect) router.Back();
                return router.Continue(save);
            },
            () =>
            {
                var host = _root.Services.Flow.Host;
                if (step < 5 && !InConsole()) return false;
                if (step == 6)
                {
                    if (router.Current == Screen.Debrief) return true;
                    // Still in the mission: the press did not take (yet); look for the button again.
                    if (router.Current == Screen.Mission)
                    {
                        step = 5;
                        stepS = 0;
                    }
                    return false;
                }
                stepS += GetProcessDeltaTime() / Math.Max(Engine.TimeScale, 1e-6);
                if (FindConsole() is not { } console) return false;
                var panel = console.FindChildren("*", "", true, false).OfType<Usa.Game.UI.Map.BurnPlannerPanel>().FirstOrDefault();
                switch (step)
                {
                    case 0:
                        console.MainView.SelectView((int)MainViewKind.Orbit);
                        Next();
                        return false;
                    case 1 when stepS > 1 && panel is not null:
                    {
                        var engines = panel.GetNode<OptionButton>("%EngineSelect");
                        var items = Enumerable.Range(0, engines.ItemCount).Select(i => $"{i}:{engines.GetItemText(i)}{(engines.IsItemDisabled(i) ? " (disabled)" : "")}").ToArray();
                        GD.Print($"[Play] ui retro: engine list [{string.Join(" | ", items)}], selected {engines.Selected}");
                        var retro = Enumerable.Range(0, engines.ItemCount).FirstOrDefault(i => engines.GetItemText(i).Contains("etro", StringComparison.Ordinal), -1);
                        if (retro < 0)
                        {
                            Fail("ui retro: no retro engine in the planner's engine list (a player cannot plan the retrofire)");
                            return Bail();
                        }
                        engines.Select(retro);
                        engines.EmitSignal(OptionButton.SignalName.ItemSelected, retro);
                        Next();
                        return false;
                    }
                    case 2 when stepS > 1 && panel is not null:
                    {
                        var goals = panel.GetNode<OptionButton>("%GoalSelect");
                        var items = Enumerable.Range(0, goals.ItemCount).Select(i => goals.GetItemText(i)).ToArray();
                        GD.Print($"[Play] ui retro: proposal goals [{string.Join(" | ", items)}]");
                        var wanted = zone.Split('_').FirstOrDefault() ?? "";
                        var goal = Enumerable.Range(0, goals.ItemCount).FirstOrDefault(i => goals.GetItemText(i).Contains(wanted, StringComparison.OrdinalIgnoreCase), -1);
                        if (goal < 0)
                        {
                            Fail($"ui retro: no proposal goal for zone '{zone}'");
                            return Bail();
                        }
                        goals.Select(goal);
                        goals.EmitSignal(OptionButton.SignalName.ItemSelected, goal);
                        var propose = panel.GetNode<Button>("%ProposeButton");
                        GD.Print($"[Play] ui retro: goal '{goals.GetItemText(goal)}', Propose {(propose.Disabled ? "disabled" : "pressed")}");
                        if (!propose.Disabled) propose.EmitSignal(BaseButton.SignalName.Pressed);
                        Next();
                        return false;
                    }
                    case 3 when panel is not null:
                    {
                        var execute = panel.GetNode<Button>("%ExecuteButton");
                        if (execute.Disabled)
                        {
                            if (stepS > 30)
                            {
                                Fail($"ui retro: Execute still disabled 30 s after Propose ({execute.TooltipText})");
                                Capture("09a_ui_retro_stuck");
                                return Bail();
                            }
                            return false;
                        }
                        Capture("09a_ui_retro_planned");
                        GD.Print("[Play] ui retro: Execute pressed");
                        execute.EmitSignal(BaseButton.SignalName.Pressed);
                        Next();
                        return false;
                    }
                    case 4:
                    {
                        var dialog = console.FindChildren("*", "", true, false).OfType<Usa.Game.UI.Console.Overlays.ConfirmDialog>().FirstOrDefault();
                        if (dialog?.GetNodeOrNull<Button>("%Confirm") is { } confirm && confirm.IsVisibleInTree() && !confirm.Disabled)
                        {
                            Capture("09b_ui_retro_confirm");
                            GD.Print("[Play] ui retro: confirmed");
                            confirm.EmitSignal(BaseButton.SignalName.Pressed);
                            Engine.TimeScale = _speed;
                            Next();
                        }
                        else if (stepS > 15)
                        {
                            Fail("ui retro: no confirmation dialog after Execute");
                            return Bail();
                        }
                        return false;
                    }
                    case 5:
                    {
                        if (host.LastFrame.MetAfter.Milliseconds % 600_000 < 200) GD.Print($"[Play] ui retro: flying, {Describe()}");
                        if (stepS - _lastAdvanceS > 3 && host.CanAdvanceToNextEvent && host.RequestAdvanceToNextEvent()) _lastAdvanceS = stepS;
                        var main = console.MainView;
                        var proceed = main.RecoveryCard.GetNodeOrNull<Button>("%ProceedButton") is { Disabled: false } r && r.IsVisibleInTree() ? r
                            : main.ClosingCard.GetNodeOrNull<Button>("%Proceed") is { Disabled: false } c && c.IsVisibleInTree() ? c : null;
                        if (proceed is not null && stepS > 1)
                        {
                            GD.Print($"[Play] ui retro: mission over at {host.LastFrame.MetAfter}; pressing '{proceed.Text}' ({proceed.GetParent()?.Name})");
                            proceed.EmitSignal(BaseButton.SignalName.Pressed);
                            Next();
                        }
                        return false;
                    }
                    case 6:
                        return router.Current == Screen.Debrief;
                    default:
                        return false;
                }
            },
            onReady: () => Engine.TimeScale = 1,
            capture: "09c_ui_retro_debrief"));

        void Next()
        {
            step++;
            stepS = 0;
        }

        bool Bail()
        {
            step = 99;
            return false;
        }
    }

    private void Enqueue(Checkpoint checkpoint, ScenarioScript script)
    {
        var router = _root.Services.Router;
        var save = Save(script, checkpoint.MetMs);
        if (!checkpoint.RunToDebrief)
        {
            _stages.Enqueue(new Stage(
                $"{checkpoint.Name} at {Met(checkpoint.MetMs)}",
                () => router.Continue(save),
                () => InConsole(),
                onReady: () =>
                {
                    if (checkpoint.View == MainViewKind.Workspace)
                    {
                        var opened = FindConsole()?.OpenWorkspace() ?? false;
                        GD.Print($"[Play] {checkpoint.Name}: OpenWorkspace() -> {opened}");
                    }
                    else if (checkpoint.View is { } view)
                    {
                        FindConsole()?.MainView.SelectView((int)view);
                    }
                    // The spacewalk shots use the player's "Focus vehicle" button, which frames her while she is outside.
                    if (checkpoint.Name.Contains("_eva_", StringComparison.Ordinal)
                        && FindConsole()?.MainView.External.FindChildren("*", "", true, false).OfType<Button>()
                            .FirstOrDefault(b => b.Text.StartsWith("Focus vehicle", StringComparison.Ordinal)) is { } focus)
                    {
                        focus.EmitSignal(BaseButton.SignalName.Pressed);
                    }
                    if (_layoutDump) Callable.From(DumpLayout).CallDeferred();
                },
                capture: checkpoint.Name,
                leave: Abandon));
            return;
        }

        _stages.Enqueue(new Stage(
            $"live from {Met(checkpoint.MetMs)} to the debrief",
            () =>
            {
                Engine.TimeScale = _speed;
                return router.Continue(save);
            },
            () =>
            {
                // Like a player pressing N now and then (pressing it every frame starves the pacer: PLAYABLE_BUGS.md).
                var host = _root.Services.Flow.Host;
                if (_stageS - _lastAdvanceS > 3 && host.CanAdvanceToNextEvent && host.RequestAdvanceToNextEvent()) _lastAdvanceS = _stageS;
                // Recovered: the player proceeds from the recovery card, as a click would.
                var main = FindConsole()?.MainView;
                var proceed = main?.RecoveryCard.GetNodeOrNull<Button>("%ProceedButton") is { Disabled: false } recovery && recovery.IsVisibleInTree() ? recovery
                    : main?.ClosingCard.GetNodeOrNull<Button>("%Proceed") is { Disabled: false } closing && closing.IsVisibleInTree() ? closing
                    : null;
                if (proceed is not null && _lastProceedS < 0)
                {
                    // First sight of the end card: show it for the record, press on a later frame.
                    _lastProceedS = _stageS;
                    Capture("07b_mission_over");
                }
                else if (proceed is not null && _stageS - _lastProceedS > 2)
                {
                    _lastProceedS = _stageS;
                    GD.Print($"[Play] mission over at {host.LastFrame.MetAfter}; pressing '{proceed.Text}' ({proceed.GetParent()?.Name})");
                    proceed.EmitSignal(BaseButton.SignalName.Pressed);
                }
                return router.Current == Screen.Debrief;
            },
            onReady: () => Engine.TimeScale = 1,
            capture: checkpoint.Name));
    }

    private ResumeSave Save(ScenarioScript script, long metMs)
    {
        var log = new CommandLog();
        // The checkpoints play unguided, as a player who turned "Guided first run" off: the launch logs that choice at MET 0
        // (MissionLaunchTutorial). Without it Easy resumes with the guide on and its rails lock the console.
        if (!_played.Log.Entries.Any(e => Usa.Simulation.Guidance.TutorialCommands.IsTutorialCommand(e.Request.Action.Value)))
        {
            var off = new Dictionary<string, CommandArg>(StringComparer.Ordinal) { [Usa.Simulation.Guidance.TutorialCommands.EnabledArg] = CommandArg.Of(false) };
            log.Append(new Usa.Simulation.Kernel.MissionTime(0), new CommandRequest(
                new Usa.Simulation.Kernel.CommandId("driver-guide-off"), CommandSource.QuickCommand,
                new Usa.Simulation.Kernel.ActionId(Usa.Simulation.Guidance.TutorialCommands.SetEnabled), null, off, Confirmed: true));
        }
        foreach (var entry in _played.Log.Entries)
        {
            if (entry.MetMs >= metMs) break;
            log.Append(new Usa.Simulation.Kernel.MissionTime(entry.MetMs), entry.Request);
        }

        var payload = new ResumePayload(ResumePayload.CurrentSchema, script.Mission, script.Difficulty, script.Seed, metMs, log.ToJson()).Serialize();
        var sha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        return new ResumeSave(
            ResumeSave.CurrentSchemaVersion,
            new ResumeMetadata(script.Mission, script.Difficulty, metMs, DateTimeOffset.UtcNow, AppVersion.Current),
            payload,
            sha);
    }

    private bool InConsole() =>
        _root.Services.Router.Current == Screen.Mission && FindConsole() is { Visible: true } && _root.Services.Flow.Host.IsRunning;

    private bool Abandon()
    {
        var router = _root.Services.Router;
        if (router.EscMenu == EscMenuState.Closed && !router.OpenEscMenu()) return false;
        return router.RequestAbandon() && router.ConfirmEscAction();
    }

    private void Abort()
    {
        Engine.TimeScale = 1;
        var router = _root.Services.Router;
        if (router.Current == Screen.Mission) Abandon();
        _stage = null;
    }

    private string Describe()
    {
        var router = _root.Services.Router;
        var host = _root.Services.Flow.Host;
        return $"router {router.Current}, esc {router.EscMenu}, connection lost {router.ConnectionLost}, MET {host.LastFrame.MetAfter}, " +
               $"rate x{host.EffectiveRate:0.#} target x{host.CompressionRate:0.#} ({host.CompressionReason}), advancing {host.IsAdvancingToEvent}, " +
               $"freeze {_root.Services.Freeze.Reasons.Value}, frame frozen {host.LastFrame.Frozen}, ticks {host.LastFrame.TicksRun}, running {host.IsRunning}";
    }

    /// <summary>Prints, per console column, the chain of widest-minimum descendants: what forces the console wider than the window.</summary>
    private void DumpLayout()
    {
        if (FindConsole() is not { } console) return;
        GD.Print($"[Play] layout: viewport {GetViewport().GetVisibleRect().Size}, shell min {console.GetNode<Control>("Shell").GetCombinedMinimumSize()}");
        foreach (var column in new[] { "Header", "ResourceStrip", "Body/Columns/LeftColumn", "Body/Columns/CentreColumn", "Body/Columns/RightColumn" })
        {
            Control? node = console.GetNode<Control>($"Shell/{column}");
            var chain = new StringBuilder();
            for (var depth = 0; node is not null && depth < 12; depth++)
            {
                chain.Append(CultureInfo.InvariantCulture, $" > {node.Name}({node.GetClass()}) {node.GetCombinedMinimumSize().X:0}");
                node = node.GetChildren().OfType<Control>().Where(c => c.Visible).MaxBy(c => c.GetCombinedMinimumSize().X);
            }
            GD.Print($"[Play] layout {column}:{chain}");
        }
    }

    private MissionConsole? FindConsole() =>
        GetTree().CurrentScene?.FindChildren("*", "", recursive: true, owned: false).OfType<MissionConsole>().FirstOrDefault();

    private void OpenMain()
    {
        if (ResourceLoader.Load<PackedScene>(MainScene) is not { } packed)
        {
            Finish($"cannot load {MainScene}");
            return;
        }

        var main = packed.Instantiate();
        GetTree().Root.AddChild(main);
        GetTree().CurrentScene = main;
    }

    private void Capture(string name)
    {
        if (_out.Length == 0 || DisplayServer.GetName() == "headless") return;
        var image = GetViewport().GetTexture().GetImage();
        var path = Path.Combine(_out, $"{name}.png");
        Directory.CreateDirectory(_out);
        var error = image.SavePng(path);
        _shot++;
        GD.Print(error == Error.Ok ? $"[Play] captured {path}" : $"[Play] capture {path} failed: {error}");
    }

    private void Fail(string message)
    {
        _failures++;
        GD.PushError($"[Play] FAIL {message}");
    }

    private void Finish(string? failure)
    {
        _done = true;
        Engine.TimeScale = 1;
        if (failure is null) GD.Print($"[Play] PASS ({_shot} screenshots)");
        else GD.PushError($"[Play] FAIL: {failure}");
        DiagnosticsQuit.Quit(this, failure is null ? 0 : 1);
    }

    private static string Met(long ms) => ScenarioPlayer.Met(ms);
}
