using Godot;
using Usa.Game.Bootstrap;
using Usa.Game.Flow;
using Usa.Game.Input;
using Usa.Game.Integration;
using Usa.Game.Integration.Runtime;
using Usa.Game.Services.Saves;
using Usa.Simulation.Commands;
using Usa.Simulation.Data;
using Usa.Simulation.Kernel;

namespace Usa.Game.Diagnostics;

/// <summary>
/// Headless check run by tools/check.ps1 (SCRIPTING 4.3): boot (autoload, services, input actions), then every mission under
/// res://data/missions on every required difficulty, loaded through the game's data source: content and whole-mission data
/// integrity, 10 min MET through <see cref="SessionPortAdapter"/>, and the same state hashes from two runs that advance in
/// different chunk sizes; then the screen-router flow (on <see cref="FlowMissionId"/>): start a mission through
/// <see cref="MissionFlowHost"/> (its runtime context composed), restart it (a new context, the old one disposed), save and
/// resume it (same state hash), abandon it (context disposed, its nodes freed, no audio loop left running).
/// Prints <c>[Smoke]</c> lines and exits 0 only when every probe passes.
/// </summary>
public sealed partial class SmokeTest : Node
{
    /// <summary>The mission the screen-router flow runs; the data probes cover every mission found.</summary>
    public const string FlowMissionId = "out_of_control";
    private const long RunMs = 10 * 60 * 1000;
    private const ulong Seed = 1;
    private const int HostTicksPerCall = 16;
    private const int CoarseTicksPerCall = 997;
    private static readonly string[] RequiredDifficulties = ["easy", "normal"];

    private const int FlowFrameLimit = 300;
    private const int FreeFrames = 3;
    private readonly List<string> _failures = [];
    private GameServices? _services;
    private FlowStage _stage = FlowStage.Idle;
    private int _flowFrames;
    private int _flowChildren;
    private int _contextChanges;
    private MissionRuntimeContext? _firstContext;
    private MissionRuntimeContext? _secondContext;

    private enum FlowStage
    {
        Idle,
        Starting,
        Restarting,
        Freeing,
    }

    public override void _Ready()
    {
        Guard(() => Probe(_failures));
        if (_failures.Count > 0 || _services is null)
        {
            Finish();
            return;
        }
        Guard(StartFlow);
        if (_failures.Count > 0) Finish();
    }

    /// <summary>Waits for the flow host to build each launch the router queued, then checks restart, resume and abandon.</summary>
    public override void _Process(double delta)
    {
        if (_stage == FlowStage.Idle || _services is not { } services) return;
        if (_stage == FlowStage.Freeing)
        {
            if (++_flowFrames < FreeFrames) return;
            _stage = FlowStage.Idle;
            Guard(() => CheckFreed(services));
            Finish();
            return;
        }

        var ready = services.Router.Current == Screen.Mission &&
            (_stage != FlowStage.Restarting || (services.Flow.Context is { } context && !ReferenceEquals(context, _firstContext)));
        if (!ready)
        {
            if (++_flowFrames < FlowFrameLimit) return;
            _failures.Add($"flow: router still on {services.Router.Current} after {FlowFrameLimit} frames ({_stage})");
            _stage = FlowStage.Idle;
            Finish();
            return;
        }

        if (_stage == FlowStage.Starting) Guard(() => Restart(services));
        else Guard(() => CheckRunningFlow(services));
        if (_failures.Count == 0) return;
        _stage = FlowStage.Idle;
        Finish();
    }

    private void StartFlow()
    {
        var services = _services!;
        var router = services.Router;
        _flowChildren = services.Flow.GetChildCount();
        services.Flow.ContextChanged += OnContextChanged;
        var started = router.OpenMissionSelect()
            && router.SelectMission(new MissionSelection(FlowMissionId, "normal", Seed))
            && router.StartMission(guidedFirstRun: false);
        if (!started || router.Current != Screen.Loading)
        {
            _failures.Add($"flow: could not start a mission (router on {router.Current})");
            return;
        }
        _flowFrames = 0;
        _stage = FlowStage.Starting;
    }

    private void OnContextChanged(MissionRuntimeContext? context) => _contextChanges++;

    /// <summary>The first run is composed; restart it through the Esc menu (same seed).</summary>
    private void Restart(GameServices services)
    {
        if (services.Flow.Context is not { } context)
        {
            _failures.Add("flow: no runtime context composed for the started mission");
            return;
        }
        _firstContext = context;
        Print($"flow: runtime composed after {_flowFrames} frame(s): console {(context.LatestConsoleSnapshot is null ? "empty" : "built")}, " +
            $"{context.Planner.Engines.Count} planner engine(s), comms host {(context.CommsHost is null ? "none" : "attached")}");
        var router = services.Router;
        if (router.ConnectionOverlayVisible) router.OnConnectionRestored();
        if (!(router.OpenEscMenu() && router.RequestRestart() && router.ConfirmEscAction()))
        {
            _failures.Add($"flow: restart through the Esc menu was refused (router on {router.Current})");
            return;
        }
        if (!context.IsDisposed) _failures.Add("flow: the first runtime context was not disposed by the restart");
        if (services.Flow.Context is not null) _failures.Add("flow: a context is still exposed while the restart loads");
        _flowFrames = 0;
        _stage = FlowStage.Restarting;
    }

    /// <summary>A few frames after the abandon, the flow host holds only what it held before the first run (comms controllers and tickers freed).</summary>
    private void CheckFreed(GameServices services)
    {
        services.Flow.ContextChanged -= OnContextChanged;
        var children = services.Flow.GetChildCount();
        if (children != _flowChildren) _failures.Add($"flow: {children} flow-host children after abandon, expected {_flowChildren}");
        if (_contextChanges != 4) _failures.Add($"flow: {_contextChanges} context changes, expected 4 (compose, dispose, compose, dispose)");
        Print($"flow: composed and disposed twice; {children} flow-host child(ren) left, {_contextChanges} context changes");

        var loops = services.Audio.RunningLoops();
        if (loops.Count > 0) _failures.Add($"flow: audio loops still running after abandon: {string.Join(", ", loops)}");
        else Print("flow: no audio loop left running after abandon");
    }

    private void CheckRunningFlow(GameServices services)
    {
        _secondContext = services.Flow.Context;
        if (_secondContext is null || _secondContext.IsDisposed) _failures.Add("flow: no live runtime context after the restart");
        else Print($"flow: restart composed a new runtime context ({_secondContext.Session.SessionId})");
        if (services.Flow.Runtime is not { } runtime)
        {
            _failures.Add("flow: router reached Mission but no runtime is running");
            return;
        }
        if (!services.Flow.Host.IsRunning) _failures.Add("flow: simulation host is not driving the session");
        Print($"flow: {runtime.Launch.MissionId}/{runtime.Launch.Difficulty} running after {_flowFrames} frame(s), freeze {services.Freeze.Reasons.Value}");

        // Advance the live session directly (an outage freeze may already apply without a backend), then save and resume it.
        var session = runtime.Mission.Session;
        var wasFrozen = session.FreezeReasons;
        foreach (var reason in new[] { FreezeReason.Menu, FreezeReason.ServiceOutage, FreezeReason.Loading })
        {
            if ((wasFrozen & reason) != 0) session.Unfreeze(reason);
        }
        session.Submit(new CommandRequest(session.NextCommandId(), CommandSource.Text, new ActionId("read_fact"), null,
            new Dictionary<string, CommandArg> { ["fact"] = CommandArg.Of("ls.ppco2_mmhg") }, Confirmed: false));
        session.Advance(session.Now + 90_000, int.MaxValue);
        if (services.Flow.Launcher.SaveResume() != SaveStatus.Ok) _failures.Add("flow: resume save failed");

        var saved = services.Saves.LoadResume(FlowMissionId);
        if (saved.Value is not { } resume)
        {
            _failures.Add($"flow: resume save not readable ({saved.Status})");
            return;
        }
        var replayed = services.Missions.Resume(ResumePayload.Parse(resume.Payload));
        if (replayed.Mission is not { } copy) _failures.Add($"flow: resume failed: {string.Join("; ", replayed.Errors)}");
        else if (copy.Session.Now != session.Now || copy.Session.StateHash() != session.StateHash())
            _failures.Add($"flow: resumed run differs (MET {copy.Session.Now} vs {session.Now})");
        else Print($"flow: resume save at MET {session.Now} replays to the same state hash {Short(session.StateHash())}");
        foreach (var reason in new[] { FreezeReason.Menu, FreezeReason.ServiceOutage, FreezeReason.Loading })
        {
            if ((wasFrozen & reason) != 0) session.Freeze(reason);
        }

        var router = services.Router;
        if (router.ConnectionOverlayVisible)
        {
            Print("flow: connection overlay up (no backend); abandon via the Esc menu not checked");
            services.Flow.Launcher.Abandon();
        }
        else if (!(router.OpenEscMenu() && router.RequestAbandon() && router.ConfirmEscAction()))
        {
            _failures.Add($"flow: abandon through the Esc menu was refused (router on {router.Current})");
        }
        if (services.Flow.Runtime is not null) _failures.Add("flow: runtime still running after abandon");
        if (services.Saves.LoadResume(FlowMissionId).Status != LoadStatus.Missing) _failures.Add("flow: resume file kept after abandon");
        if (services.Freeze.IsFrozen && services.Freeze.Reasons.Value != FreezeReason.ServiceOutage)
            _failures.Add($"flow: freeze reasons left behind: {services.Freeze.Reasons.Value}");
        if (services.Flow.Context is not null) _failures.Add("flow: a runtime context is still exposed after abandon");
        if (_secondContext is { IsDisposed: false }) _failures.Add("flow: the restarted runtime context was not disposed by the abandon");
        Print($"flow: abandoned; router on {router.Current}, resume file removed");
        _flowFrames = 0;
        _stage = FlowStage.Freeing;
    }

    private void Guard(Action probe)
    {
        try
        {
            probe();
        }
        catch (Exception ex)
        {
            _failures.Add($"unexpected {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Finish()
    {
        foreach (var failure in _failures) GD.PushError($"[Smoke] {failure}");
        GD.Print(_failures.Count == 0 ? "[Smoke] PASS" : $"[Smoke] FAIL ({_failures.Count})");
        DiagnosticsQuit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private void Probe(List<string> failures)
    {
        if (!InputMap.HasAction(InputBindings.PushToTalk)) failures.Add("input actions not registered");
        if (MissionTime.FromSeconds(22_800).ToString() != "06:20:00") failures.Add("simulation assembly not usable");
        if (GetNodeOrNull<GameRoot>("/root/GameRoot") is not { IsComposed: true } root)
        {
            failures.Add("GameRoot autoload missing or not composed");
            return;
        }

        var services = root.Services;
        _services = services;
        Print($"services composed; missions [{string.Join(", ", services.Missions.MissionIds)}], difficulties [{string.Join(", ", services.Missions.DifficultyIds)}]");
        if (!services.Missions.MissionIds.Contains(FlowMissionId)) failures.Add($"mission '{FlowMissionId}' not found under res://data/missions");
        foreach (var difficulty in RequiredDifficulties)
        {
            if (!services.Missions.DifficultyIds.Contains(difficulty)) failures.Add($"difficulty '{difficulty}' not found");
        }
        if (failures.Count > 0) return;

        foreach (var missionId in services.Missions.MissionIds)
        {
            try
            {
                var content = services.Content.Get(missionId);
                Print($"content library loaded for {content.MissionId}");
            }
            catch (DefinitionException ex)
            {
                failures.Add($"{missionId} content: {ex.Message}");
            }

            foreach (var difficulty in RequiredDifficulties) ProbeDifficulty(services.Missions, missionId, difficulty, failures);
        }
    }

    private static void ProbeDifficulty(MissionLoader missions, string missionId, string difficulty, List<string> failures)
    {
        var label = $"{missionId}/{difficulty}";
        var integrity = missions.CheckIntegrity(missionId, difficulty);
        foreach (var problem in integrity) failures.Add($"{label} data integrity: {problem}");
        Print($"{label}: data integrity {(integrity.Count == 0 ? "clean" : $"{integrity.Count} problem(s)")}");

        var request = new MissionLaunchRequest(missionId, difficulty, Seed);
        var host = Run(missions, request, HostTicksPerCall, failures);
        var coarse = Run(missions, request, CoarseTicksPerCall, failures);
        if (host is null || coarse is null) return;

        var expectedSamples = (int)(RunMs / 60_000);
        if (host.Hashes.Count != expectedSamples) failures.Add($"{label}: {host.Hashes.Count} hash samples, expected {expectedSamples}");
        if (!host.Hashes.SequenceEqual(coarse.Hashes)) failures.Add($"{label}: hash sequence differs between {HostTicksPerCall}- and {CoarseTicksPerCall}-tick runs");
        if (host.FinalHash != coarse.FinalHash) failures.Add($"{label}: final state hash differs between runs");
        Print($"{label}: deterministic over {host.Hashes.Count} samples, final {Short(host.FinalHash)} = {Short(coarse.FinalHash)}");
    }

    private static MissionSmokeResult? Run(MissionLoader missions, MissionLaunchRequest request, int ticksPerCall, List<string> failures)
    {
        var loaded = missions.Load(request);
        if (loaded.Mission is not { } mission)
        {
            foreach (var error in loaded.Errors) failures.Add($"{request.DifficultyId} load: {error}");
            return null;
        }

        var result = MissionSmokeRun.Run(mission, RunMs, ticksPerCall);
        foreach (var failure in result.Failures) failures.Add($"{request.DifficultyId} run: {failure}");
        if (result.End != result.Start + RunMs) failures.Add($"{request.DifficultyId}: stopped at MET {result.End}, expected {result.Start + RunMs}");
        Print($"{request}: MET {result.Start} -> {result.End} in {result.Elapsed.TotalSeconds:F2} s real ({ticksPerCall} ticks/call, {result.EventCount} events)");
        return result;
    }

    private static string Short(string hash) => hash.Length > 12 ? hash[..12] : hash;

    private static void Print(string message) => GD.Print($"[Smoke] {message}");
}
