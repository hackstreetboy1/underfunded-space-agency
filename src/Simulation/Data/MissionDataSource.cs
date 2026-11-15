using System.Text.Json;
using System.Text.Json.Nodes;

namespace Usa.Simulation.Data;

/// <summary>
/// One mission's view of the data (GDD D1: a mission is data). The global catalogs hold only entries every mission can use;
/// each mission adds its own in optional files that this view merges into the global path, so every loader keeps reading
/// one file:
/// <list type="table">
/// <item><term>catalog/tasks.json</term><description>+ <c>missions/&lt;id&gt;/catalog/tasks.json</c> (<c>tasks</c>)</description></item>
/// <item><term>catalog/actions.json</term><description>+ <c>missions/&lt;id&gt;/catalog/actions.json</c> (<c>actions</c>)</description></item>
/// <item><term>catalog/facts.json</term><description>+ <c>missions/&lt;id&gt;/catalog/facts.json</c> (<c>facts</c>; phrases and methods stay global)</description></item>
/// <item><term>crew/crew.json</term><description>+ <c>missions/&lt;id&gt;/crew.json</c> (<c>members</c>; performance stays global)</description></item>
/// </list>
/// A mission file has the global file's schema and only the scoped array; the merged array lists the mission's entries first
/// (so an error at <c>$.&lt;array&gt;[i]</c> with i below the mission's count points into the mission file), then the shared ones.
/// An id defined in both files is an error, so a mission never silently replaces a shared entry. Every other path is read
/// through unchanged. Shared actions say <c>"phases": ["*"]</c> (<see cref="LivePhases"/>) or <c>["**"]</c> (<see cref="AllPhases"/>),
/// expanded to the mission's phases.
/// Wrapping is idempotent for the same mission (<see cref="For"/>).
/// </summary>
public sealed class MissionDataSource : IDataSource
{
    private static readonly JsonDocumentOptions DocumentOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private static readonly ScopedFile[] ScopedFiles =
    [
        new("catalog/tasks.json", "catalog/tasks.json", "tasks", "tasks/1"),
        new("catalog/actions.json", "catalog/actions.json", ActionsKey, "actions/1"),
        new("catalog/facts.json", "catalog/facts.json", "facts", "facts/1"),
        new("crew/crew.json", "crew.json", "members", "crew/1"),
    ];

    /// <summary>In <c>catalog/actions.json</c>, <c>"phases": ["*"]</c> means every phase of the mission except its ended phase.</summary>
    public const string LivePhases = "*";

    /// <summary>In <c>catalog/actions.json</c>, <c>"phases": ["**"]</c> means every phase of the mission, its ended phase included.</summary>
    public const string AllPhases = "**";

    private const string ActionsKey = "actions";

    private readonly Dictionary<string, string> _merged = new(StringComparer.Ordinal);

    public MissionDataSource(IDataSource inner, string missionId)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        Inner = inner is MissionDataSource view ? view.Inner : inner;
        MissionId = missionId;
    }

    /// <summary>The unmerged data.</summary>
    public IDataSource Inner { get; }

    public string MissionId { get; }

    /// <summary><paramref name="source"/> itself when it already is this mission's view, else a new view of its unmerged data.</summary>
    public static MissionDataSource For(IDataSource source, string missionId) =>
        source is MissionDataSource view && view.MissionId == missionId ? view : new MissionDataSource(source, missionId);

    /// <summary>The mission-scoped file merged into <paramref name="globalPath"/>, or null when that path is not scoped.</summary>
    public static string? ScopedPathFor(string missionId, string globalPath)
    {
        foreach (var file in ScopedFiles)
        {
            if (file.GlobalPath == globalPath) return $"missions/{missionId}/{file.MissionPath}";
        }
        return null;
    }

    public bool Exists(string relativePath) => Inner.Exists(relativePath);

    public IReadOnlyList<string> List(string relativeDirectory, string searchPattern = "*.json") => Inner.List(relativeDirectory, searchPattern);

    /// <exception cref="DefinitionException">A mission file is malformed, has another schema or redefines a global id.</exception>
    public string ReadText(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        foreach (var file in ScopedFiles)
        {
            if (file.GlobalPath != relativePath) continue;
            if (_merged.TryGetValue(relativePath, out var cached)) return cached;
            var merged = Merge(file, Inner.ReadText(relativePath));
            _merged[relativePath] = merged;
            return merged;
        }
        return Inner.ReadText(relativePath);
    }

    private string Merge(ScopedFile file, string globalText)
    {
        var global = ParseObject(file.GlobalPath, globalText);
        if (global[file.ArrayKey] is not JsonArray globalItems)
            throw new DefinitionException(file.GlobalPath, $"$.{file.ArrayKey}", "Array expected.");
        var missionPath = ScopedPathFor(MissionId, file.GlobalPath)!;
        if (Inner.Exists(missionPath)) MergeMissionItems(file, globalItems, missionPath, ParseObject(missionPath, Inner.ReadText(missionPath)));
        if (file.ArrayKey == ActionsKey) ExpandAllPhases(globalItems);
        return global.ToJsonString();
    }

    /// <summary>
    /// A shared action cannot list one mission's phases, so <c>"phases": ["*"]</c> stands for every phase of the mission but its
    /// <c>endedPhase</c> and <c>["**"]</c> for every phase (<c>mission.json</c> <c>phases</c>, in that order).
    /// </summary>
    private void ExpandAllPhases(JsonArray actions)
    {
        foreach (var action in actions)
        {
            if (action is not JsonObject entry || entry["phases"] is not JsonArray { Count: 1 } phases ||
                phases[0] is not JsonValue only || !only.TryGetValue<string>(out var text) || text is not (LivePhases or AllPhases))
                continue;
            entry["phases"] = MissionPhases(includeEnded: text == AllPhases);
        }
    }

    private JsonArray MissionPhases(bool includeEnded)
    {
        var path = $"missions/{MissionId}/mission.json";
        var mission = ParseObject(path, Inner.ReadText(path));
        if (mission["phases"] is not JsonArray phases) throw new DefinitionException(path, "$.phases", "Array expected.");
        var ended = mission["endedPhase"] is JsonValue value && value.TryGetValue<string>(out var id) ? id : null;
        var selected = new JsonArray();
        foreach (var phase in phases)
        {
            if (phase is JsonValue name && name.TryGetValue<string>(out var text) && (includeEnded || text != ended)) selected.Add(text);
        }
        return selected;
    }

    private static void MergeMissionItems(ScopedFile file, JsonArray globalItems, string missionPath, JsonObject mission)
    {
        var errors = new DefinitionErrors();

        var globalSchema = file.Schema;
        var missionSchema = mission["schema"]?.GetValue<string>();
        if (missionSchema != globalSchema)
            errors.Add(missionPath, "$.schema", $"Expected schema '{globalSchema}' (as {file.GlobalPath}) but found '{missionSchema}'.");
        foreach (var property in mission)
        {
            if (property.Key is not ("schema" or "description") && property.Key != file.ArrayKey)
                errors.Add(missionPath, $"$.{property.Key}", $"Only '{file.ArrayKey}' can be scoped to a mission; '{property.Key}' belongs in {file.GlobalPath}.");
        }
        if (mission[file.ArrayKey] is not JsonArray missionItems)
        {
            errors.Add(missionPath, $"$.{file.ArrayKey}", "Array expected.");
            throw new DefinitionException(errors.All);
        }

        var globalIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in globalItems)
        {
            if (IdOf(item) is { } id) globalIds.Add(id);
        }
        for (var i = 0; i < missionItems.Count; i++)
        {
            if (IdOf(missionItems[i]) is { } id && globalIds.Contains(id))
                errors.Add(missionPath, $"$.{file.ArrayKey}[{i}].id", $"'{id}' is already defined in {file.GlobalPath}; mission entries cannot replace shared ones.");
        }
        errors.ThrowIfAny();

        var moved = missionItems.ToArray();
        missionItems.Clear();
        for (var i = 0; i < moved.Length; i++) globalItems.Insert(i, moved[i]);
    }

    private static JsonObject ParseObject(string file, string text)
    {
        try
        {
            return JsonNode.Parse(text, documentOptions: DocumentOptions) as JsonObject
                   ?? throw new DefinitionException(file, "$", "A JSON object is expected.");
        }
        catch (JsonException ex)
        {
            throw new DefinitionException(file, "$", ex.Message, ex);
        }
    }

    private static string? IdOf(JsonNode? item) =>
        item is JsonObject entry && entry["id"] is JsonValue value && value.TryGetValue<string>(out var id) ? id : null;

    private sealed record ScopedFile(string GlobalPath, string MissionPath, string ArrayKey, string Schema);
}
