using Usa.Game.Services.Saves;
using Usa.Simulation.Scoring;

namespace Usa.Game.Flow.Profile;

public enum ProfileRecordStatus
{
    Recorded,

    /// <summary>This run was already recorded (or attempted); nothing was written again.</summary>
    AlreadyRecorded,

    /// <summary>The save could not be written; the run is not retried so it can never count twice.</summary>
    Failed,
}

/// <summary>
/// Records each finished run into the profile exactly once, keyed by <see cref="MissionLaunch.RunNumber"/>, and
/// removes the mission's resume file (a finished flight cannot be continued). One instance lives as long as the game; the
/// debrief asks <see cref="StatusOf"/> whether its run was saved.
/// </summary>
public sealed class ProfileRecorder(IProfileStore store)
{
    private readonly Dictionary<long, ProfileRecordStatus> _outcomes = [];

    public ProfileRecordStatus Record(CompletedMission mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        var run = mission.Launch.RunNumber;
        // Marked failed first: a store that throws leaves the run attempted and reported as not saved.
        if (!_outcomes.TryAdd(run, ProfileRecordStatus.Failed)) return ProfileRecordStatus.AlreadyRecorded;

        var status = store.RecordRun(ToRun(mission)) ? ProfileRecordStatus.Recorded : ProfileRecordStatus.Failed;
        _outcomes[run] = status;
        store.DeleteResume(mission.Launch.MissionId);
        return status;
    }

    /// <summary>
    /// What the first <see cref="Record"/> of run <paramref name="runNumber"/> did (<see cref="ProfileRecordStatus.Recorded"/> or
    /// <see cref="ProfileRecordStatus.Failed"/>); null when that run was never recorded.
    /// </summary>
    public ProfileRecordStatus? StatusOf(long runNumber) => _outcomes.TryGetValue(runNumber, out var status) ? status : null;

    public static CompletedRun ToRun(CompletedMission mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        var view = mission.View;
        var completed = view.Objectives
            .Where(o => o.State == ObjectiveFinalState.Completed)
            .Select(o => o.Id.Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new CompletedRun(
            mission.Launch.MissionId,
            mission.Launch.Difficulty,
            GradeIds.IdOf(view.Grade),
            Math.Clamp(view.FinalPoints, 0, 100),
            view.SPlus.Achieved,
            completed);
    }
}
