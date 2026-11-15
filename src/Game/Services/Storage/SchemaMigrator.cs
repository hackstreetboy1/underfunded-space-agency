using System.Text.Json;
using System.Text.Json.Nodes;

namespace Usa.Game.Services.Storage;

public enum MigrationOutcome
{
    /// <summary>Already at the current version.</summary>
    Current,

    /// <summary>One or more steps ran; the caller should re-save.</summary>
    Migrated,

    /// <summary>Written by a newer build. Readable fields are kept; the caller should not overwrite blindly.</summary>
    NewerVersion,

    /// <summary>No step exists for the stored version; treat the file as unreadable.</summary>
    Unsupported,
}

public readonly record struct MigrationResult(MigrationOutcome Outcome, int FromVersion, JsonObject Root);

/// <summary>
/// Upgrades a JSON document one version at a time through registered steps (<c>v → v+1</c>).
/// The version lives in the root property <see cref="VersionProperty"/>; a missing property means version 0.
/// </summary>
public sealed class SchemaMigrator(int currentVersion, IReadOnlyDictionary<int, Func<JsonObject, JsonObject>> steps)
{
    public const string VersionProperty = "schemaVersion";

    // JsonObject throws ArgumentException when it materialises a duplicate property, so duplicates must be
    // rejected while parsing, where they surface as JsonException like any other malformed document.
    private static readonly JsonDocumentOptions StrictDocument = new() { AllowDuplicateProperties = false };

    public int CurrentVersion { get; } = currentVersion;

    /// <summary>
    /// Parses a stored document for <see cref="Migrate"/>. Returns null when the root is not an object.
    /// Throws <see cref="JsonException"/> for malformed JSON, including a duplicate property name at any depth.
    /// </summary>
    public static JsonObject? ParseRoot(string json) => JsonNode.Parse(json, documentOptions: StrictDocument) as JsonObject;

    public static int ReadVersion(JsonObject root) =>
        root.TryGetPropertyValue(VersionProperty, out var node) && node is JsonValue value && value.TryGetValue<int>(out var version)
            ? version
            : 0;

    public MigrationResult Migrate(JsonObject root)
    {
        var from = ReadVersion(root);
        if (from == CurrentVersion) return new MigrationResult(MigrationOutcome.Current, from, root);
        if (from > CurrentVersion) return new MigrationResult(MigrationOutcome.NewerVersion, from, root);

        var current = root;
        for (var version = from; version < CurrentVersion; version++)
        {
            if (!steps.TryGetValue(version, out var step)) return new MigrationResult(MigrationOutcome.Unsupported, from, root);
            current = step(current);
            current[VersionProperty] = version + 1;
        }
        return new MigrationResult(MigrationOutcome.Migrated, from, current);
    }
}
