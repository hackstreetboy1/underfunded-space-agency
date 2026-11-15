using System.Text.Json;

namespace Usa.Game.Diagnostics.Repro;

/// <summary>
/// One playtest repro (schema <see cref="SchemaTag"/>): enough to rebuild the run headless (mission, difficulty, seed, command
/// log) and to tell where a replay departs from it (the 60 s state hash samples and the hash at capture). Everything else is
/// context for the person reading it. This folder is Godot-free: <c>tools/repro</c> compiles it into the headless replayer.
/// </summary>
/// <param name="Reason"><see cref="ReproReasons"/>: why it was written.</param>
/// <param name="Mission">Null when no mission was running.</param>
/// <param name="CommandLog">The session's <c>command_log/1</c> document, embedded as-is; null without a mission.</param>
public sealed record ReproDocument(
    string Schema,
    DateTimeOffset CreatedUtc,
    string Reason,
    ReproApp App,
    ReproMission? Mission,
    ReproSettings? Settings,
    ReproHost? Host,
    IReadOnlyList<ReproHash> Hashes,
    JsonElement? CommandLog,
    ReproFailure? Failure = null,
    string? Note = null)
{
    public const string SchemaTag = "usa_repro/1";
}

/// <summary>Values of <see cref="ReproDocument.Reason"/>.</summary>
public static class ReproReasons
{
    public const string Manual = "manual";
    public const string Exception = "exception";
}

/// <param name="Editor">Running inside the Godot editor (F5) rather than an exported build.</param>
public sealed record ReproApp(string Name, string Version, string BuildPhase, string Godot, bool DebugBuild, bool Editor, string Os, string Runtime);

/// <param name="RunTag">The launch's command-id tag (<c>MissionSession.CommandIdPrefix</c> minus the session id); null when untagged.</param>
/// <param name="MetMs">MET when the repro was captured; the replay runs to exactly this time.</param>
/// <param name="FinalHash">State hash at <paramref name="MetMs"/>.</param>
/// <param name="Resumed">The run was resumed from a save (its log already holds the earlier commands).</param>
public sealed record ReproMission(
    string MissionId,
    string Difficulty,
    ulong Seed,
    string? RunTag,
    string SessionId,
    long MetMs,
    string FinalHash,
    int CommandCount,
    bool GuidedFirstRun,
    bool Resumed);

/// <summary>Settings that change what the player sees or can do (none of them feed the simulation).</summary>
public sealed record ReproSettings(bool TutorialCallouts, bool MicEnabled, bool ReducedMotion, bool HighContrast, float UiScale, bool DefaultBackendUrl);

/// <summary>Host state at capture: pacing, freeze reasons, router screen and backend health.</summary>
public sealed record ReproHost(
    string Screen,
    double CompressionRate,
    string CompressionReason,
    double EffectiveRate,
    string SessionFreeze,
    string AppFreeze,
    string Backend);

public sealed record ReproHash(long MetMs, string Hash);

/// <param name="Source">Where it was caught: <c>godot-error</c>, <c>appdomain</c>, <c>task</c>.</param>
public sealed record ReproFailure(string Type, string Message, string? Detail, string Source);
