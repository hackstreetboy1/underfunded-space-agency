using Usa.Game.Comms;
using Usa.Game.Integration.Runtime;
using Usa.Game.Services.Logging;
using Usa.Simulation.Data;
using Usa.Simulation.Session;

namespace Usa.Game.Integration;

/// <summary>Which mission run to build: the same request always builds the same run.</summary>
public sealed record MissionLaunchRequest(string MissionId, string DifficultyId, ulong Seed)
{
    public override string ToString() => $"{MissionId}/{DifficultyId}/seed {Seed}";
}

/// <summary>A composed session and the hosting port over it.</summary>
/// <param name="History">
/// What a resumed session raised while it was replayed, already taken off the session (<see cref="MissionHistory.None"/> for a
/// fresh load). Only the debrief recorder and the runtime's state restore read it, so nothing from before the resume is shown again.
/// </param>
public sealed record LoadedMission(MissionLaunchRequest Request, MissionSession Session, SessionPortAdapter Port, MissionHistory History)
{
    /// <summary>
    /// Comms ids for this run: command ids from the session's single source (<see cref="MissionSession.NextCommandId"/>,
    /// shared with console and workspace), auxiliary request ids under the same run-tagged prefix.
    /// </summary>
    public CommandIdAllocator CreateCommsIds() => new(Session.CommandIdPrefix, Session.NextCommandId);
}

/// <summary>Either a loaded mission or every data problem that stopped it loading.</summary>
public sealed record MissionLoadResult(LoadedMission? Mission, IReadOnlyList<DefinitionError> Errors)
{
    public bool Succeeded => Mission is not null;
}

/// <summary>
/// Builds mission sessions from the game's data source (<c>res://data</c> through <c>GodotDataSource</c> in the game,
/// any <see cref="IDataSource"/> in tools). Never throws for data problems: they come back as <see cref="MissionLoadResult.Errors"/>.
/// </summary>
public sealed class MissionLoader
{
    public const string DifficultyDirectory = "difficulty";
    private const string Category = "Missions";

    private readonly IDataSource _data;
    private readonly Log _log;
    private readonly Func<string> _newRunTag;

    /// <param name="missionIds">Mission directories under <c>missions/</c> that hold a <c>mission.json</c>.</param>
    /// <param name="newRunTag">
    /// A fresh tag per built run (<see cref="MissionSession.CommandIdPrefix"/>); defaults to 8 random hex digits, so a
    /// restart with the same seed or a resume after relaunch never reuses request ids the AI proxy still caches.
    /// </param>
    public MissionLoader(IDataSource data, Log log, IReadOnlyList<string> missionIds, Func<string>? newRunTag = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(missionIds);
        _data = data;
        _log = log;
        _newRunTag = newRunTag ?? NewRandomRunTag;
        MissionIds = missionIds;
        DifficultyIds = ListDifficulties(data);
    }

    public IReadOnlyList<string> MissionIds { get; }

    /// <summary>Difficulty ids from <c>difficulty/*.json</c>, ordinal order.</summary>
    public IReadOnlyList<string> DifficultyIds { get; }

    public MissionLoadResult Load(MissionLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Build(request, () => MissionSession.Load(_data, request.MissionId, request.DifficultyId, request.Seed, _newRunTag()), resumed: false);
    }

    /// <summary>
    /// Rebuilds a saved run by replaying its command log up to the saved MET. The replay's events are taken off the session
    /// into <see cref="LoadedMission.History"/>, so the live runtime starts at the resume MET.
    /// </summary>
    public MissionLoadResult Resume(ResumePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var request = new MissionLaunchRequest(payload.MissionId, payload.DifficultyId, payload.Seed);
        return Build(request, () => MissionSession.Replay(
            _data, payload.MissionId, payload.DifficultyId, payload.Seed, payload.ReadCommandLog(), payload.Met, runTag: _newRunTag()), resumed: true);
    }

    private MissionLoadResult Build(MissionLaunchRequest request, Func<MissionSession> create, bool resumed)
    {
        try
        {
            var session = create();
            var history = resumed ? MissionHistory.TakeFrom(session) : MissionHistory.None;
            var replayed = resumed ? $", {history.Events.Count} replayed event(s) kept out of the runtime" : string.Empty;
            _log.Info(Category, $"loaded {request} at MET {session.Now} (command ids {session.CommandIdPrefix}-*{replayed})");
            return new MissionLoadResult(new LoadedMission(request, session, new SessionPortAdapter(session), history), []);
        }
        catch (DefinitionException ex)
        {
            IReadOnlyList<DefinitionError> errors = ex.Errors.Count > 0 ? ex.Errors : [new DefinitionError(ex.File, ex.JsonPath, ex.Message)];
            _log.Error(Category, $"{request} failed to load: {errors.Count} data problem(s); first: {errors[0]}");
            return new MissionLoadResult(null, errors);
        }
    }

    /// <summary>The whole-mission data check (<see cref="MissionDataIntegrity"/>); empty when consistent.</summary>
    public IReadOnlyList<DefinitionError> CheckIntegrity(string missionId, string difficultyId) =>
        MissionDataIntegrity.Check(_data, missionId, difficultyId);

    private static string NewRandomRunTag() => Guid.NewGuid().ToString("N")[..8];

    private static string[] ListDifficulties(IDataSource data)
    {
        var files = data.List(DifficultyDirectory);
        var ids = new string[files.Count];
        for (var i = 0; i < files.Count; i++) ids[i] = Path.GetFileNameWithoutExtension(files[i]);
        return ids;
    }
}
