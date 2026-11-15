using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Usa.Game.Services.Storage;

namespace Usa.Game.Services.Settings;

public enum SettingsDecodeStatus
{
    Ok,

    /// <summary>Upgraded from an older schema; re-save.</summary>
    Migrated,

    /// <summary>From a newer build; what this build understands was read, the rest is defaults. Never overwrite the file.</summary>
    NewerVersion,

    /// <summary>Not JSON, not an object, or no migration path. The caller moves the file aside.</summary>
    Unreadable,

    /// <summary>No settings file yet; defaults.</summary>
    Missing,

    /// <summary>The file exists but could not be read (locked, access denied); defaults. Its content is unknown, so never overwrite it.</summary>
    ReadFailed,
}

public readonly record struct SettingsDecodeResult(SettingsDecodeStatus Status, GameSettings Settings)
{
    /// <summary>True when the stored file has content this build must not replace (<see cref="SettingsDecodeStatus.NewerVersion"/>, <see cref="SettingsDecodeStatus.ReadFailed"/>).</summary>
    public bool ProtectsFile => Status is SettingsDecodeStatus.NewerVersion or SettingsDecodeStatus.ReadFailed;
}

/// <summary>Text ⇄ <see cref="GameSettings"/> with migration and sanitising. Pure.</summary>
public static class SettingsCodec
{
    public static string Encode(GameSettings settings) =>
        JsonSerializer.Serialize(SettingsRules.Sanitize(settings), SettingsJsonContext.Default.GameSettings);

    /// <summary>Decodes a store read, mapping a missing or unreadable file to defaults with its own status.</summary>
    public static SettingsDecodeResult Decode(StoreReadResult read) => read.Status switch
    {
        StoreReadStatus.Missing => new SettingsDecodeResult(SettingsDecodeStatus.Missing, new GameSettings()),
        StoreReadStatus.IoError => new SettingsDecodeResult(SettingsDecodeStatus.ReadFailed, new GameSettings()),
        _ => Decode(read.Text ?? string.Empty),
    };

    public static SettingsDecodeResult Decode(string json)
    {
        try
        {
            if (SchemaMigrator.ParseRoot(json) is not { } root) return Unreadable();

            var migration = SettingsRules.CreateMigrator().Migrate(root);
            switch (migration.Outcome)
            {
                case MigrationOutcome.Unsupported:
                    return Unreadable();
                case MigrationOutcome.NewerVersion:
                    // Checked before deserialising: a newer build may have changed a property's type or added an
                    // enum value, which must not make its file look corrupt here.
                    return new SettingsDecodeResult(SettingsDecodeStatus.NewerVersion, SettingsRules.Sanitize(DeserializeUnderstood(migration.Root)));
            }

            var settings = SettingsRules.Sanitize(migration.Root.Deserialize(SettingsJsonContext.Default.GameSettings));
            var status = migration.Outcome == MigrationOutcome.Migrated ? SettingsDecodeStatus.Migrated : SettingsDecodeStatus.Ok;
            return new SettingsDecodeResult(status, settings);
        }
        catch (JsonException)
        {
            return Unreadable();
        }
    }

    /// <summary>
    /// Adds to <paramref name="paths"/> the path of every value an edit (<paramref name="before"/> to <paramref name="after"/>)
    /// changed, as JSON property names joined by <c>/</c> (<c>audio/masterLinear</c>). Sections and the binding map recurse;
    /// anything else, a binding list included, is one value. Accumulate it over a session, then <see cref="ReplayPaths"/>.
    /// </summary>
    public static void CollectChangedPaths(GameSettings before, GameSettings after, ISet<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        CollectChanges(ToNode(before), ToNode(after), string.Empty, paths);
    }

    /// <summary>
    /// <paramref name="stored"/> with each of <paramref name="paths"/> taken from <paramref name="edited"/> (removed where
    /// <paramref name="edited"/> has none): only what the player changed overrides the file, even a change back to a default.
    /// </summary>
    public static GameSettings ReplayPaths(GameSettings stored, GameSettings edited, IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var target = ToNode(stored);
        var source = ToNode(edited);
        foreach (var path in paths)
        {
            var names = path.Split('/');
            JsonNode? value = source;
            foreach (var name in names) value = (value as JsonObject)?[name];

            var parent = target;
            for (var i = 0; i < names.Length - 1; i++)
            {
                if (parent[names[i]] is not JsonObject next)
                {
                    next = [];
                    parent[names[i]] = next;
                }
                parent = next;
            }
            if (value is null) parent.Remove(names[^1]);
            else parent[names[^1]] = value.DeepClone();
        }
        return SettingsRules.Sanitize(target.Deserialize(SettingsJsonContext.Default.GameSettings));
    }

    private static void CollectChanges(JsonObject before, JsonObject after, string prefix, ISet<string> paths)
    {
        foreach (var (name, afterValue) in after)
        {
            before.TryGetPropertyValue(name, out var beforeValue);
            if (JsonNode.DeepEquals(beforeValue, afterValue)) continue;
            var path = prefix + name;
            if (afterValue is JsonObject afterObject && beforeValue is JsonObject beforeObject) CollectChanges(beforeObject, afterObject, path + "/", paths);
            else paths.Add(path);
        }

        foreach (var (name, _) in before)
        {
            if (!after.ContainsKey(name)) paths.Add(prefix + name);
        }
    }

    private static JsonObject ToNode(GameSettings settings) =>
        JsonSerializer.SerializeToNode(SettingsRules.Sanitize(settings), SettingsJsonContext.Default.GameSettings)!.AsObject();

    /// <summary>Deserialises, dropping each value this build cannot read (named by the exception path) until the rest fits.</summary>
    private static GameSettings? DeserializeUnderstood(JsonObject root)
    {
        var working = root.DeepClone().AsObject();
        // Every pass removes one node, so the loop ends; the bound only guards against a path that names nothing.
        for (var pass = 0; pass < 256; pass++)
        {
            try
            {
                return working.Deserialize(SettingsJsonContext.Default.GameSettings);
            }
            catch (JsonException ex)
            {
                if (!RemoveAt(working, ex.Path)) return null;
            }
        }
        return null;
    }

    /// <summary>Removes the node at a serializer path such as <c>$.display.windowMode</c> or <c>$.controls.bindingOverrides.ptt[0].device</c>.</summary>
    /// <remarks>Inside an array the whole element goes, so a half-understood binding is not kept.</remarks>
    private static bool RemoveAt(JsonObject root, string? path)
    {
        if (!TrySplitPath(path, out var segments) || segments.Count == 0) return false;

        var lastIndex = segments.FindLastIndex(segment => segment.Index is not null);
        var target = lastIndex >= 0 ? lastIndex : segments.Count - 1;

        JsonNode? parent = root;
        for (var i = 0; i < target; i++)
        {
            parent = segments[i].Index is { } index
                ? parent is JsonArray array && index < array.Count ? array[index] : null
                : parent is JsonObject obj && obj.TryGetPropertyValue(segments[i].Name!, out var child) ? child : null;
            if (parent is null) return false;
        }

        var last = segments[target];
        if (last.Index is { } at)
        {
            if (parent is not JsonArray items || at >= items.Count) return false;
            items.RemoveAt(at);
            return true;
        }
        return parent is JsonObject owner && owner.Remove(last.Name!);
    }

    private static SettingsDecodeResult Unreadable() => new(SettingsDecodeStatus.Unreadable, new GameSettings());

    private readonly record struct PathSegment(string? Name, int? Index);

    private static bool TrySplitPath(string? path, out List<PathSegment> segments)
    {
        segments = [];
        if (path is null || !path.StartsWith('$')) return false;

        var i = 1;
        while (i < path.Length)
        {
            if (path.AsSpan(i).StartsWith("['"))
            {
                var end = path.IndexOf("']", i + 2, StringComparison.Ordinal);
                if (end < 0) return false;
                segments.Add(new PathSegment(path[(i + 2)..end], null));
                i = end + 2;
            }
            else if (path[i] == '.')
            {
                var end = path.IndexOfAny(['.', '['], i + 1);
                if (end < 0) end = path.Length;
                if (end == i + 1) return false;
                segments.Add(new PathSegment(path[(i + 1)..end], null));
                i = end;
            }
            else if (path[i] == '[')
            {
                var end = path.IndexOf(']', i + 1);
                if (end < 0 || !int.TryParse(path.AsSpan(i + 1, end - i - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var index)) return false;
                segments.Add(new PathSegment(null, index));
                i = end + 1;
            }
            else
            {
                return false;
            }
        }
        return true;
    }
}
