using Usa.Simulation.Data;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Session;

/// <summary>
/// A difficulty profile, <c>data/difficulty/&lt;id&gt;.json</c> (schema <c>difficulty/1</c>). Its values are served as
/// <c>difficulty.&lt;key&gt;</c>; each area documents the keys it reads (contract section 9).
/// </summary>
public sealed record DifficultyProfile(string Schema, string Id, string DisplayName, IReadOnlyDictionary<string, StateValue> Values)
    : IVersionedDefinition
{
    public const string SchemaTag = "difficulty/1";

    /// <summary>X8: multiplies the ground's navigation uncertainty (Easy 0.5, Normal 1); readers default to 1 when absent.</summary>
    public const string NavUncertaintyScale = "nav_uncertainty_scale";

    public static string PathFor(string difficultyId) => $"difficulty/{difficultyId}.json";

    /// <exception cref="DefinitionException">Malformed file, wrong schema, id mismatch or a null value.</exception>
    public static DifficultyProfile Load(DefinitionLoader loader, string difficultyId)
    {
        ArgumentNullException.ThrowIfNull(loader);
        var path = PathFor(difficultyId);
        var profile = loader.Load<DifficultyProfile>(path, SchemaTag);
        var errors = new DefinitionErrors();
        if (profile.Id != difficultyId) errors.Add(path, "$.id", $"Id '{profile.Id}' does not match the file name '{difficultyId}'.");
        foreach (var (key, value) in profile.Values)
        {
            if (value.Kind == StateValueKind.None) errors.Add(path, $"$.values.{key}", "Value is null.");
        }
        if (profile.Values.TryGetValue(NavUncertaintyScale, out var scale) &&
            !(scale.Kind == StateValueKind.Number && scale.AsNumber() > 0 && double.IsFinite(scale.AsNumber())))
            errors.Add(path, $"$.values.{NavUncertaintyScale}", "Must be a finite number above 0 (1 = nominal tracking uncertainty).");
        errors.ThrowIfAny();
        return profile;
    }

    public bool GetBool(string key, bool fallback) =>
        Values.TryGetValue(key, out var value) && value.Kind is StateValueKind.Bool or StateValueKind.Number ? value.AsBool() : fallback;

    public double GetNumber(string key, double fallback) =>
        Values.TryGetValue(key, out var value) && value.Kind is StateValueKind.Number or StateValueKind.Bool ? value.AsNumber() : fallback;
}
