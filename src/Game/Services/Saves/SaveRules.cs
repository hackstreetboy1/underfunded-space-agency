using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Usa.Game.Services.Storage;

namespace Usa.Game.Services.Saves;

/// <summary>Validation, merging, hashing and migration hooks for save files. Pure.</summary>
public static partial class SaveRules
{
    public const int MaxGradeLength = 16;
    public const int MaxObjectives = 64;
    public const int MaxPayloadChars = 8 * 1024 * 1024;

    /// <summary>Mission and difficulty ids are snake_case; this also keeps them safe inside file names.</summary>
    public static bool IsValidId(string? id) => id is not null && IdPattern().IsMatch(id);

    /// <summary>Migration hook for profile.json; add <c>n → n+1</c> steps when the schema grows.</summary>
    public static SchemaMigrator CreateProfileMigrator() => new(PlayerProfile.CurrentSchemaVersion, new Dictionary<int, Func<JsonObject, JsonObject>>());

    /// <summary>Migration hook for resume files. Old resume files are usually discarded rather than migrated.</summary>
    public static SchemaMigrator CreateResumeMigrator() => new(ResumeSave.CurrentSchemaVersion, new Dictionary<int, Func<JsonObject, JsonObject>>());

    public static string Hash(string payload) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    public static IReadOnlyList<string> Validate(PlayerProfile? profile)
    {
        var problems = new List<string>();
        if (profile is null) return ["profile is null"];
        if (profile.Records is null) return ["records missing"];

        var seen = new HashSet<(string, string)>();
        for (var i = 0; i < profile.Records.Count; i++)
        {
            var record = profile.Records[i];
            if (record is null)
            {
                problems.Add($"records[{i}] is null");
                continue;
            }
            if (!IsValidId(record.MissionId)) problems.Add($"records[{i}].missionId invalid");
            if (!IsValidId(record.Difficulty)) problems.Add($"records[{i}].difficulty invalid");
            if (string.IsNullOrWhiteSpace(record.BestGrade) || record.BestGrade.Length > MaxGradeLength) problems.Add($"records[{i}].bestGrade invalid");
            if (!double.IsFinite(record.BestScorePoints) || record.BestScorePoints is < 0 or > 100) problems.Add($"records[{i}].bestScorePoints out of range");
            if (record.ObjectivesCompleted is null || record.ObjectivesCompleted.Count > MaxObjectives) problems.Add($"records[{i}].objectivesCompleted invalid");
            else if (!AllValidIds(record.ObjectivesCompleted)) problems.Add($"records[{i}].objectivesCompleted has an invalid id");
            if (!seen.Add((record.MissionId ?? "", record.Difficulty ?? ""))) problems.Add($"records[{i}] duplicates an earlier record");
        }
        return problems;
    }

    public static IReadOnlyList<string> Validate(ResumeSave? save, string expectedMissionId)
    {
        if (save is null) return ["resume is null"];
        var problems = new List<string>();
        var metadata = save.Metadata;
        if (metadata is null) return ["metadata missing"];
        if (metadata.MissionId != expectedMissionId) problems.Add("metadata.missionId does not match the file name");
        if (!IsValidId(metadata.Difficulty)) problems.Add("metadata.difficulty invalid");
        if (metadata.MetMs < 0) problems.Add("metadata.metMs negative");
        if (save.Payload is null || save.Payload.Length > MaxPayloadChars) problems.Add("payload missing or too large");
        else if (!string.Equals(save.PayloadSha256, Hash(save.Payload), StringComparison.Ordinal)) problems.Add("payload checksum mismatch");
        return problems;
    }

    /// <summary>
    /// Folds a finished run into the profile: higher points replace the best grade (ties keep the earlier run),
    /// S+ is sticky, completed objectives accumulate.
    /// </summary>
    public static PlayerProfile Merge(PlayerProfile profile, CompletedRun run, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(run);

        var existing = profile.Find(run.MissionId, run.Difficulty);
        var objectives = new SortedSet<string>(StringComparer.Ordinal);
        if (existing is not null) objectives.UnionWith(existing.ObjectivesCompleted);
        objectives.UnionWith(run.ObjectivesCompleted);

        var improved = existing is null || run.ScorePoints > existing.BestScorePoints;
        var merged = new MissionBestRecord(
            run.MissionId,
            run.Difficulty,
            improved ? run.Grade : existing!.BestGrade,
            improved ? run.ScorePoints : existing!.BestScorePoints,
            run.SPlusAchieved || (existing?.SPlusAchieved ?? false),
            [.. objectives],
            nowUtc);

        var records = new List<MissionBestRecord>(profile.Records.Count + 1);
        foreach (var record in profile.Records)
        {
            if (record.MissionId != run.MissionId || record.Difficulty != run.Difficulty) records.Add(record);
        }
        records.Add(merged);
        records.Sort(static (a, b) =>
        {
            var byMission = string.CompareOrdinal(a.MissionId, b.MissionId);
            return byMission != 0 ? byMission : string.CompareOrdinal(a.Difficulty, b.Difficulty);
        });
        return profile with { SchemaVersion = PlayerProfile.CurrentSchemaVersion, Records = records };
    }

    private static bool AllValidIds(IReadOnlyList<string> ids)
    {
        foreach (var id in ids)
        {
            if (!IsValidId(id)) return false;
        }
        return true;
    }

    // \z, not $: '$' also matches before a trailing newline, which would let "m1\n" into a file name.
    [GeneratedRegex(@"\A[a-z0-9_]{1,64}\z")]
    private static partial Regex IdPattern();
}
