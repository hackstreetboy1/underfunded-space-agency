using System.Globalization;
using Godot;
using Usa.Game.Bootstrap;
using Usa.Game.Flow;
using Usa.Game.UI.Console;
using Usa.Game.UI.Frontend;

namespace Usa.Game.Diagnostics;

/// <summary>
/// Boot-to-console smoke (diagnostics/boot/BootToConsole.tscn): switches to <c>ui/screens/Main.tscn</c>, binds its
/// <see cref="FrontendShell"/> to the composed services the way GameRoot does at boot, starts the first mission through the
/// screen router (main menu, mission select, briefing, loading), waits for the mission console, lets the mission run, then
/// quits through the audio release gate. User arguments after <c>--</c>: <c>--difficulty=easy</c>, <c>--seed=1</c>,
/// <c>--run=60</c> seconds in the console, <c>--timeout=60</c> seconds allowed to reach it. Prints <c>[Boot]</c> lines;
/// exits non-zero when the console never appears, the router refuses a step or the mission clock does not move.
/// Error and leak lines are judged from the output by the caller (tools/check.ps1 style).
/// </summary>
public sealed partial class BootToConsoleSmoke : Node
{
    private const string MainScene = "res://ui/screens/Main.tscn";

    private readonly Queue<(string Name, Func<bool> Step)> _steps = new();
    private GameRoot _root = null!;
    private string _difficulty = "easy";
    private ulong _seed = 1;
    private double _runS = 60;
    private double _timeoutS = 60;
    private double _elapsedS;
    private double _inConsoleS = -1;
    private long _metAtConsoleMs = -1;
    private bool _started;
    private bool _done;
    private double _nextReportS;

    public override void _Ready()
    {
        if (GetNodeOrNull<GameRoot>("/root/GameRoot") is not { IsComposed: true } root)
        {
            Finish("GameRoot autoload missing or not composed", 1);
            return;
        }

        _root = root;
        var args = OS.GetCmdlineUserArgs()
            .Select(a => a.TrimStart('-').Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal);
        if (args.TryGetValue("difficulty", out var difficulty)) _difficulty = difficulty;
        if (args.TryGetValue("seed", out var seed) && ulong.TryParse(seed, CultureInfo.InvariantCulture, out var parsedSeed)) _seed = parsedSeed;
        if (args.TryGetValue("run", out var run)) _runS = Seconds(run, _runS);
        if (args.TryGetValue("timeout", out var timeout)) _timeoutS = Seconds(timeout, _timeoutS);

        // Main becomes the current scene next to this driver (a scene change would free the driver with the old scene).
        // Deferred: the root is still adding this scene.
        Callable.From(OpenMain).CallDeferred();
    }

    private void OpenMain()
    {
        if (ResourceLoader.Load<PackedScene>(MainScene) is not { } packed)
        {
            Finish($"cannot load {MainScene}", 1);
            return;
        }

        var main = packed.Instantiate();
        GetTree().Root.AddChild(main);
        GetTree().CurrentScene = main;
    }

    public override void _Process(double delta)
    {
        if (_done || _root is null) return;
        _elapsedS += delta;
        if (!_started)
        {
            if (GetTree().CurrentScene is not FrontendShell shell) return;
            StartMission(shell);
            return;
        }

        if (RunNextStep()) return;

        var router = _root.Services.Router;
        var met = _root.Services.Flow.Host.LastFrame.MetAfter;
        if (_inConsoleS < 0)
        {
            if (router.Current == Screen.Mission && FindConsole() is not null && _root.Services.Flow.Host.IsRunning)
            {
                _inConsoleS = 0;
                _metAtConsoleMs = met.Milliseconds;
                GD.Print($"[Boot] mission console up after {_elapsedS:0.0} s, MET {met}");
            }
            else if (_elapsedS > _timeoutS)
            {
                Finish($"no mission console after {_timeoutS:0} s (router on {router.Current}, mission in progress {router.MissionInProgress})", 1);
            }
            return;
        }

        _inConsoleS += delta;
        if (_inConsoleS >= _nextReportS)
        {
            GD.Print($"[Boot] {_inConsoleS:0} s in console: MET {met}, rate x{_root.Services.Flow.Host.EffectiveRate:0.#}, router {router.Current}");
            _nextReportS += 15;
        }
        if (_inConsoleS < _runS) return;

        if (router.Current != Screen.Mission) Finish($"left the mission screen (router on {router.Current})", 1);
        else if (met.Milliseconds <= _metAtConsoleMs) Finish($"mission clock did not move in {_runS:0} s (MET {met})", 1);
        else Finish($"ran {_runS:0} s in the console; MET {met}", 0);
    }

    private void StartMission(FrontendShell shell)
    {
        _started = true;
        var services = _root.Services;
        // GameRoot binds the shell only when Main.tscn is the boot scene; here it arrived by a scene switch.
        if (shell.Context is null) shell.Initialize(services);
        var missions = services.Missions.MissionIds;
        if (missions.Count == 0)
        {
            Finish("no mission to start", 1);
            return;
        }

        var missionId = missions[0];
        var router = services.Router;
        var selection = new MissionSelection(missionId, _difficulty, _seed);
        GD.Print($"[Boot] main menu bound; starting {missionId}/{_difficulty} seed {_seed}");
        // One screen per frame, as a player would: each screen's deferred focus grab runs while it is still shown.
        _steps.Enqueue(("open mission select", router.OpenMissionSelect));
        _steps.Enqueue(("select the mission", () => router.SelectMission(selection)));
        _steps.Enqueue(("begin the mission", () => router.StartMission(guidedFirstRun: false)));
    }

    private bool RunNextStep()
    {
        if (_steps.Count == 0) return false;
        var (name, step) = _steps.Dequeue();
        if (!step()) Finish($"the router refused to {name} (on {_root.Services.Router.Current})", 1);
        return true;
    }

    private MissionConsole? FindConsole() =>
        GetTree().CurrentScene?.FindChildren("*", "", recursive: true, owned: false).OfType<MissionConsole>().FirstOrDefault();

    private static double Seconds(string text, double fallback) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value >= 0 ? value : fallback;

    private void Finish(string message, int exitCode)
    {
        _done = true;
        if (exitCode == 0) GD.Print($"[Boot] PASS: {message}");
        else GD.PushError($"[Boot] FAIL: {message}");
        DiagnosticsQuit.Quit(this, exitCode);
    }
}
