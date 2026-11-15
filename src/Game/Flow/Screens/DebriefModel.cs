using Usa.Game.Content;
using Usa.Game.Flow.Profile;

namespace Usa.Game.Flow.Screens;

/// <summary>
/// Debrief (UI.md 5, GDD 10.4): the composed <see cref="DebriefView"/> plus Replay (same seed), Retry (new variant)
/// and Mission select. The run is recorded into the profile when the mission ends (by the host, through
/// <see cref="ProfileRecorder"/>); the model only shows the outcome, so rebuilding it has no side effects.
/// </summary>
public sealed class DebriefModel
{
    private readonly ScreenRouter _router;

    public DebriefModel(ScreenRouter router, CompletedMission mission, ProfileRecordStatus recordStatus)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(mission);
        _router = router;
        Mission = mission;
        RecordStatus = recordStatus;
    }

    public CompletedMission Mission { get; }

    public DebriefView View => Mission.View;

    public string MissionId => Mission.Launch.MissionId;

    public string Difficulty => Mission.Launch.Difficulty;

    public ulong Seed => Mission.Seed;

    public ulong ReplaySeed => SeedPlanner.Replay(Seed);

    public ulong RetrySeed => SeedPlanner.Retry(Seed);

    /// <summary>Pass <c>ProfileRecorder.StatusOf(run)</c>, so the debrief says honestly whether this run was saved.</summary>
    public ProfileRecordStatus RecordStatus { get; }

    /// <summary>The run reached the profile; <see cref="ProfileRecordStatus.AlreadyRecorded"/> says nothing about the first write, so it does not count.</summary>
    public bool Saved => RecordStatus == ProfileRecordStatus.Recorded;

    public string ReplayLabel => View.Labels.Replay;

    public string RetryLabel => View.Labels.Retry;

    public string MissionSelectLabel => View.Labels.MissionSelect;

    public bool Replay() => _router.Replay();

    public bool Retry() => _router.Retry();

    public bool MissionSelect() => _router.OpenMissionSelect();
}
