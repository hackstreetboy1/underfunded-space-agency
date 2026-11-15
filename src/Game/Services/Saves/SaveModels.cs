namespace Usa.Game.Services.Saves;

/// <summary><c>user://saves/profile.json</c>: per mission and difficulty records (GDD 13 slice: best grade, S+, objectives).</summary>
public sealed record PlayerProfile
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Sorted by mission id, then difficulty (ordinal).</summary>
    public IReadOnlyList<MissionBestRecord> Records { get; init; } = [];

    public MissionBestRecord? Find(string missionId, string difficulty)
    {
        foreach (var record in Records)
        {
            if (record.MissionId == missionId && record.Difficulty == difficulty) return record;
        }
        return null;
    }
}

/// <param name="BestGrade">Grade id from <c>config/scoring.json</c> (e.g. "b_plus") of the best run.</param>
/// <param name="BestScorePoints">Final points 0-100 of the best run; decides what "best" means.</param>
/// <param name="ObjectivesCompleted">Union of objective ids completed in any run, sorted.</param>
public sealed record MissionBestRecord(
    string MissionId,
    string Difficulty,
    string BestGrade,
    double BestScorePoints,
    bool SPlusAchieved,
    IReadOnlyList<string> ObjectivesCompleted,
    DateTimeOffset LastPlayedUtc);

/// <summary>One finished mission, as reported by the debrief.</summary>
public sealed record CompletedRun(
    string MissionId,
    string Difficulty,
    string Grade,
    double ScorePoints,
    bool SPlusAchieved,
    IReadOnlyList<string> ObjectivesCompleted);

/// <summary>What the menu needs to offer "Continue" without reading the payload.</summary>
/// <param name="MetMs">Mission elapsed time at the save, for display.</param>
public sealed record ResumeMetadata(string MissionId, string Difficulty, long MetMs, DateTimeOffset SavedUtc, string GameVersion);

/// <summary>
/// <c>user://saves/resume_&lt;mission&gt;.json</c>. <see cref="Payload"/> is opaque to this service (the session's
/// seed, variant picks, command log and transcript, SCRIPTING.md 11.9); <see cref="PayloadSha256"/> detects truncation.
/// </summary>
public sealed record ResumeSave(int SchemaVersion, ResumeMetadata Metadata, string Payload, string PayloadSha256)
{
    public const int CurrentSchemaVersion = 1;
}
