using Usa.Game.Flow;
using Usa.Game.Flow.Tutorial;
using Usa.Game.Hosting;
using Usa.Game.Services.Logging;
using Usa.Game.Services.Saves;
using Usa.Simulation.Data;

namespace Usa.Game.Integration;

/// <summary>The result of building a queued launch.</summary>
/// <param name="Mission">Null when the launch failed; <paramref name="Errors"/> then says why.</param>
public sealed record MissionLaunchOutcome(MissionLaunch Launch, LoadedMission? Mission, IReadOnlyList<DefinitionError> Errors);

/// <summary>
/// <see cref="IMissionLauncher"/> for the screen router. The router calls it before it enters the Loading screen, so
/// requests are queued and built by <see cref="ProcessPending"/> on a later frame (the host then answers the router with
/// <c>LoadingCompleted</c> or <c>LoadingFailed</c>). A resume launch replays the saved payload; a fresh launch or an
/// abandon discards the mission's resume file, since that flight can no longer be continued. Periodic resume saves
/// (<see cref="SaveResumeInBackground"/>) capture the payload on the calling thread and write it on a worker, latest wins;
/// deletes and synchronous saves wait for a write in progress, so a stale write never lands after them.
/// </summary>
public sealed class MissionLauncher(MissionLoader loader, SaveService saves, Log log) : IMissionLauncher
{
    private const string Category = "Launcher";
    private readonly LatestWinsWorker _writer = new(ex => log.Error(Category, $"resume write failed: {ex.GetType().Name}: {ex.Message}"));
    private MissionLaunch? _pending;

    /// <summary>The running session was discarded (restart, abandon, or a new launch); stop driving it.</summary>
    public event Action<LoadedMission>? Discarded;

    public LoadedMission? Current { get; private set; }

    public MissionLaunch? CurrentLaunch { get; private set; }

    public bool HasPending => _pending is not null;

    public void Start(MissionLaunch launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        Discard();
        _pending = launch;
    }

    public void Restart(MissionLaunch launch) => Start(launch);

    public void Abandon()
    {
        _pending = null;
        if (CurrentLaunch is { } launch) DeleteResume(launch.MissionId);
        Discard();
    }

    /// <summary>Builds the queued launch, if any.</summary>
    public MissionLaunchOutcome? ProcessPending()
    {
        if (_pending is not { } launch) return null;
        _pending = null;

        var result = launch.Resume is { } resume ? LoadResume(launch, resume) : LoadFresh(launch);
        if (result.Mission is { } mission)
        {
            MissionLaunchTutorial.Apply(launch, new SessionTutorial(mission.Session));
            Current = mission;
            CurrentLaunch = launch;
        }
        return new MissionLaunchOutcome(launch, result.Mission, result.Errors);
    }

    /// <summary>Writes the running session to <c>user://saves/resume_&lt;mission&gt;.json</c> now (quit, tools), after any background write.</summary>
    public SaveStatus? SaveResume()
    {
        if (Capture() is not { } save) return null;
        _writer.CancelWaitingAndWait();
        return save();
    }

    /// <summary>The periodic save: the payload is captured here (main thread), serialised to disk on a worker.</summary>
    /// <returns>False when no session is running.</returns>
    public bool SaveResumeInBackground()
    {
        if (Capture() is not { } save) return false;
        _writer.Post(() => save());
        return true;
    }

    /// <summary>Blocks until a background resume write finished (shutdown).</summary>
    public void FlushSaves() => _writer.WaitIdle();

    /// <summary>The mission finished (debrief shown); the session is no longer current.</summary>
    public void Complete() => Discard();

    private Func<SaveStatus>? Capture()
    {
        if (Current is not { } mission || CurrentLaunch is not { } launch) return null;
        var payload = ResumePayload.Capture(mission.Session);
        var text = payload.Serialize();
        var met = mission.Session.Now;
        return () =>
        {
            var status = saves.SaveResume(launch.MissionId, launch.Difficulty, payload.MetMs, text);
            log.Debug(Category, $"resume save {launch.MissionId} at MET {met}: {status}");
            return status;
        };
    }

    private void DeleteResume(string missionId)
    {
        _writer.CancelWaitingAndWait();
        saves.DeleteResume(missionId);
    }

    private MissionLoadResult LoadFresh(MissionLaunch launch)
    {
        if (launch.Seed is not { } seed)
            return new MissionLoadResult(null, [new DefinitionError("launch", "$.seed", "A fresh launch needs a seed.")]);
        DeleteResume(launch.MissionId);
        return loader.Load(new MissionLaunchRequest(launch.MissionId, launch.Difficulty, seed));
    }

    private MissionLoadResult LoadResume(MissionLaunch launch, ResumeSave resume)
    {
        ResumePayload payload;
        try
        {
            payload = ResumePayload.Parse(resume.Payload);
        }
        catch (DefinitionException ex)
        {
            log.Warn(Category, $"resume of {launch.MissionId} unreadable: {ex.Message}");
            return new MissionLoadResult(null, [new DefinitionError(ex.File, ex.JsonPath, ex.Message)]);
        }
        if (payload.MissionId != launch.MissionId || payload.DifficultyId != launch.Difficulty)
            return new MissionLoadResult(null, [new DefinitionError("resume", "$", $"Payload is for {payload.MissionId}/{payload.DifficultyId}.")]);
        return loader.Resume(payload);
    }

    private void Discard()
    {
        if (Current is not { } mission) return;
        Current = null;
        CurrentLaunch = null;
        Discarded?.Invoke(mission);
    }
}
