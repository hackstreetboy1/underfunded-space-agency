using System.Text.Json.Serialization;
using Usa.Simulation.Data;
using Usa.Simulation.Ports;

namespace Usa.Game.Flow.Data;

/// <summary>
/// The front end's view of <c>data/difficulty/&lt;id&gt;.json</c> (schema <c>difficulty/1</c>): name and the few values
/// the menus read. Mirrors the simulation's <c>DifficultyProfile</c> so the front end builds without the Session area.
/// </summary>
public sealed record DifficultyInfo(string Schema, string Id, string DisplayName, IReadOnlyDictionary<string, StateValue> Values)
    : IVersionedDefinition
{
    public const string SchemaTag = "difficulty/1";
    public const string Directory = "difficulty";
    public const string Easy = "easy";
    public const string Normal = "normal";
    public const string Hard = "hard";

    /// <summary>Order of the difficulty selector (GDD 9.3).</summary>
    public static IReadOnlyList<string> SelectorOrder { get; } = [Easy, Normal, Hard];

    /// <summary>Whether the guided first run is offered on by default (GDD 9.3, 9.4).</summary>
    public bool TutorialDefault => GetBool("tutorial_default", false);

    /// <summary>
    /// Whether the guided first run stays on by default after the player has finished the mission (GDD 9.3: Easy "on by
    /// default", Normal "on for first play").
    /// </summary>
    public bool TutorialEveryPlay => GetBool("tutorial_every_play", false);

    /// <summary>Offered on mission select; from <see cref="DemoScope"/>, never from the profile file. It still loads for scenarios.</summary>
    [JsonIgnore]
    public bool Selectable { get; init; } = true;

    /// <summary>
    /// The debrief offers Retry with a new incident variant; from <see cref="DemoScope"/>. Off in the demo build, so players
    /// only fly the verified first variant. Replay with the same seed stays.
    /// </summary>
    [JsonIgnore]
    public bool RetryNewVariant { get; init; } = true;

    public static string PathFor(string difficultyId) => $"{Directory}/{difficultyId}.json";

    /// <exception cref="DefinitionException">Missing or malformed file, or an id that does not match the file name.</exception>
    public static DifficultyInfo Load(IDataSource source, string difficultyId)
    {
        ArgumentNullException.ThrowIfNull(source);
        var path = PathFor(difficultyId);
        var info = new DefinitionLoader(source).Load<DifficultyInfo>(path, SchemaTag);
        if (info.Id != difficultyId) throw new DefinitionException(path, "$.id", $"Id '{info.Id}' does not match the file name '{difficultyId}'.");
        return info;
    }

    /// <summary>Every shipped difficulty in selector order (Hard is absent in the slice), with the demo cuts applied.</summary>
    public static IReadOnlyList<DifficultyInfo> LoadAll(IDataSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var demo = DemoScope.Load(source);
        var result = new List<DifficultyInfo>();
        foreach (var id in SelectorOrder)
        {
            if (source.Exists(PathFor(id)))
                result.Add(Load(source, id) with { Selectable = demo.Offers(id), RetryNewVariant = demo.RetryNewVariant });
        }
        return result;
    }

    public bool GetBool(string key, bool fallback) =>
        Values.TryGetValue(key, out var value) && value.Kind is StateValueKind.Bool or StateValueKind.Number ? value.AsBool() : fallback;
}
