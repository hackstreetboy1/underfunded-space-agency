using System.Text.Json;
using Usa.Simulation.Data;

namespace Usa.Game.Content;

/// <summary>Validation helpers shared by the content loaders; every problem is added, never thrown.</summary>
internal static class ContentChecks
{
    public static void Text(DefinitionErrors errors, string file, string path, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) errors.Add(file, path, "Text is missing or empty.");
    }

    public static void OptionalText(DefinitionErrors errors, string file, string path, string? value)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value)) errors.Add(file, path, "Must be null or non-empty text.");
    }

    public static void Texts(DefinitionErrors errors, string file, string path, IReadOnlyList<string> values, bool requireAny = true)
    {
        if (requireAny && values.Count == 0) errors.Add(file, path, "At least one entry is required.");
        for (var i = 0; i < values.Count; i++) Text(errors, file, $"{path}[{i}]", values[i]);
    }

    public static void TextMap(DefinitionErrors errors, string file, string path, IReadOnlyDictionary<string, string> map)
    {
        if (map.Count == 0) errors.Add(file, path, "At least one entry is required.");
        foreach (var (key, value) in map)
        {
            if (string.IsNullOrWhiteSpace(key)) errors.Add(file, path, "A key is empty.");
            Text(errors, file, KeyPath(path, key), value);
        }
    }

    /// <summary>Adds an error for each key in <paramref name="required"/> that the map lacks.</summary>
    public static void RequiredKeys(
        DefinitionErrors errors, string file, string path, IReadOnlyDictionary<string, string> map, IEnumerable<string> required, string what)
    {
        foreach (var key in required)
        {
            if (!map.ContainsKey(key)) errors.Add(file, KeyPath(path, key), $"Missing {what} '{key}'.");
        }
    }

    /// <summary>Adds an error for each key in the map that is not in <paramref name="allowed"/>.</summary>
    public static void KnownKeys(
        DefinitionErrors errors, string file, string path, IReadOnlyDictionary<string, string> map, IReadOnlyCollection<string> allowed, string what)
    {
        foreach (var key in map.Keys)
        {
            if (!allowed.Contains(key)) errors.Add(file, KeyPath(path, key), $"Unknown {what} '{key}'. Expected one of: {string.Join(", ", allowed)}.");
        }
    }

    /// <summary>Records <paramref name="id"/> and reports it when it was already seen.</summary>
    public static void UniqueId(DefinitionErrors errors, string file, string path, HashSet<string> seen, string id, string what)
    {
        if (string.IsNullOrWhiteSpace(id)) errors.Add(file, path, $"The {what} id is empty.");
        else if (!seen.Add(id)) errors.Add(file, path, $"Duplicate {what} id '{id}'.");
    }

    public static string KeyPath(string path, string key) => $"{path}['{key}']";

    public static string SnakeName<T>(T value) where T : struct, Enum => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    public static string[] SnakeNames<T>() where T : struct, Enum => Enum.GetValues<T>().Select(SnakeName).ToArray();

    /// <summary>Wraps a load so a broken file adds its errors and the others still load.</summary>
    public static T? TryLoad<T>(DefinitionErrors errors, string file, Func<T> load) where T : class
    {
        try
        {
            return load();
        }
        catch (DefinitionException ex)
        {
            if (ex.Errors.Count > 0)
            {
                foreach (var error in ex.Errors) errors.Add(error.File, error.JsonPath, error.Message);
            }
            else
            {
                errors.Add(string.IsNullOrEmpty(ex.File) ? file : ex.File, ex.JsonPath, ex.InnerException?.Message ?? ex.Message);
            }
            return null;
        }
    }
}
