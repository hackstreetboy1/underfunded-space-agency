using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Usa.Game.Services.Logging;
using Usa.Game.Services.Storage;

namespace Usa.Game.Services.Saves;

public enum LoadStatus
{
    Ok,
    Missing,

    /// <summary>Unparseable or invalid; the file was moved aside (see <see cref="LoadResult{T}.MovedAsideTo"/>).</summary>
    Corrupt,

    /// <summary>The file exists but could not be read; it was left alone.</summary>
    IoError,

    /// <summary>Written by a newer build; left alone so downgrading never destroys it.</summary>
    NewerVersion,
}

public enum SaveStatus
{
    Ok,

    /// <summary>The data failed validation and was not written.</summary>
    Invalid,

    /// <summary>Reading the existing file or writing the new one failed; the existing file was left alone.</summary>
    IoError,

    /// <summary>The existing file is from a newer build; it was left alone.</summary>
    NewerVersion,
}

public sealed record LoadResult<T>(LoadStatus Status, T? Value, string? MovedAsideTo = null)
    where T : class;

/// <summary>
/// Profile records and resume files under <c>user://saves/</c> (SCRIPTING.md 11.9). Writes are atomic, loads are
/// validated, damaged files are moved aside, and IO problems come back as statuses, never exceptions.
/// Godot-free: the store root is the globalized <c>user://</c> path in the game and a temp folder in tests.
/// </summary>
public sealed class SaveService(UserFileStore store, Log log, string gameVersion, TimeProvider? time = null)
{
    public const string Directory = "saves";
    public const string ProfilePath = Directory + "/profile.json";
    private const string ResumePrefix = "resume_";
    private const string Category = "Saves";

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    // Local files, never HTML: the default encoder escaped every quote of the opaque resume payload as six characters.
    private readonly JsonSerializerOptions _fileJson = new(SaveJsonContext.Default.Options) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private bool _pruned;

    public static string ResumePath(string missionId) => $"{Directory}/{ResumePrefix}{missionId}.json";

    /// <summary>Always yields a profile: an empty one when the file is missing, corrupt or unreadable.</summary>
    public LoadResult<PlayerProfile> LoadProfile()
    {
        PruneOnce();
        var result = Load(ProfilePath, SaveRules.CreateProfileMigrator(), SaveJsonContext.Default.PlayerProfile, profile => SaveRules.Validate(profile));
        return result.Value is null ? result with { Value = new PlayerProfile() } : result;
    }

    /// <summary>
    /// Replaces the stored profile. Refuses (<see cref="SaveStatus.IoError"/>, <see cref="SaveStatus.NewerVersion"/>) when
    /// the existing file cannot be read or is from a newer build, so it is never overwritten blindly. Prefer
    /// <see cref="RecordRun"/>, which merges into what is stored.
    /// </summary>
    public SaveStatus SaveProfile(PlayerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (Validate(profile) is { } invalid) return invalid;
        return Protects(LoadProfile().Status) ?? Write(ProfilePath, profile);
    }

    /// <summary>
    /// Merges a finished run into the stored profile and saves it. If the file could not be read or is from a newer
    /// build nothing is written, so the real file survives.
    /// </summary>
    /// <returns><see cref="SaveStatus.Ok"/> only when the merged profile was written.</returns>
    public SaveStatus RecordRun(CompletedRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var loaded = LoadProfile();
        if (Protects(loaded.Status) is { } refused) return refused;

        var merged = SaveRules.Merge(loaded.Value!, run, _time.GetUtcNow());
        return Validate(merged) ?? Write(ProfilePath, merged);
    }

    public SaveStatus SaveResume(string missionId, string difficulty, long metMs, string payload)
    {
        if (!SaveRules.IsValidId(missionId))
        {
            log.Error(Category, "resume not saved: invalid mission id");
            return SaveStatus.Invalid;
        }

        ArgumentNullException.ThrowIfNull(payload);
        var save = new ResumeSave(
            ResumeSave.CurrentSchemaVersion,
            new ResumeMetadata(missionId, difficulty, metMs, _time.GetUtcNow(), gameVersion),
            payload,
            SaveRules.Hash(payload));

        var problems = SaveRules.Validate(save, missionId);
        if (problems.Count > 0)
        {
            log.Error(Category, $"resume not saved, invalid: {problems[0]}");
            return SaveStatus.Invalid;
        }
        return Write(ResumePath(missionId), save);
    }

    public LoadResult<ResumeSave> LoadResume(string missionId)
    {
        if (!SaveRules.IsValidId(missionId)) return new LoadResult<ResumeSave>(LoadStatus.Missing, null);
        return Load(ResumePath(missionId), SaveRules.CreateResumeMigrator(), SaveJsonContext.Default.ResumeSave, save => SaveRules.Validate(save, missionId));
    }

    /// <summary>Metadata of every valid resume file, sorted by mission id. Damaged files are moved aside on the way.</summary>
    public IReadOnlyList<ResumeMetadata> ListResumes()
    {
        PruneOnce();
        var result = new List<ResumeMetadata>();
        foreach (var path in store.List(Directory, $"{ResumePrefix}*.json"))
        {
            var fileName = path[(path.LastIndexOf('/') + 1)..];
            var missionId = fileName[ResumePrefix.Length..^".json".Length];
            if (LoadResume(missionId) is { Status: LoadStatus.Ok, Value: { } save }) result.Add(save.Metadata);
        }
        return result;
    }

    public bool DeleteResume(string missionId) => SaveRules.IsValidId(missionId) && store.Delete(ResumePath(missionId));

    private static SaveStatus? Protects(LoadStatus existing) => existing switch
    {
        LoadStatus.IoError => SaveStatus.IoError,
        LoadStatus.NewerVersion => SaveStatus.NewerVersion,
        _ => null,
    };

    private SaveStatus? Validate(PlayerProfile profile)
    {
        var problems = SaveRules.Validate(profile);
        if (problems.Count == 0) return null;
        log.Error(Category, $"profile not saved, invalid: {problems[0]}");
        return SaveStatus.Invalid;
    }

    // Crash-left temp files and old moved-aside copies; once per session is enough.
    private void PruneOnce()
    {
        if (_pruned) return;
        _pruned = true;
        store.PruneLeftovers(Directory);
    }

    private LoadResult<T> Load<T>(string path, SchemaMigrator migrator, JsonTypeInfo<T> type, Func<T, IReadOnlyList<string>> validate)
        where T : class
    {
        var read = store.ReadText(path);
        if (read.Status == StoreReadStatus.Missing) return new LoadResult<T>(LoadStatus.Missing, null);
        if (read.Status == StoreReadStatus.IoError) return new LoadResult<T>(LoadStatus.IoError, null);

        string problem;
        try
        {
            if (SchemaMigrator.ParseRoot(read.Text!) is { } root)
            {
                var migration = migrator.Migrate(root);
                if (migration.Outcome == MigrationOutcome.NewerVersion)
                {
                    log.Warn(Category, $"{path} is from a newer build (schemaVersion {migration.FromVersion}); left untouched");
                    return new LoadResult<T>(LoadStatus.NewerVersion, null);
                }

                if (migration.Outcome == MigrationOutcome.Unsupported)
                {
                    problem = $"unsupported schemaVersion {migration.FromVersion}";
                }
                else
                {
                    var value = migration.Root.Deserialize(type);
                    var problems = value is null ? ["empty document"] : validate(value);
                    if (problems.Count == 0)
                    {
                        if (migration.Outcome == MigrationOutcome.Migrated) Write(path, value!);
                        return new LoadResult<T>(LoadStatus.Ok, value);
                    }
                    problem = problems[0];
                }
            }
            else
            {
                problem = "not a JSON object";
            }
        }
        catch (JsonException)
        {
            problem = "malformed JSON";
        }

        log.Warn(Category, $"{path} rejected: {problem}");
        return new LoadResult<T>(LoadStatus.Corrupt, null, store.MoveAside(path));
    }

    private SaveStatus Write<T>(string path, T value)
    {
        var json = JsonSerializer.Serialize(value, _fileJson.GetTypeInfo<T>());
        return store.WriteTextAtomic(path, json) ? SaveStatus.Ok : SaveStatus.IoError;
    }
}
