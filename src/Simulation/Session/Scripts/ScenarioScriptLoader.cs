using System.Text.Json;
using System.Text.Json.Serialization;

namespace Usa.Simulation.Session.Scripts;

/// <summary>Reads <c>*.scenario.json</c> files (comments and trailing commas allowed; unknown fields are errors).</summary>
public static class ScenarioScriptLoader
{
    public const string FilePattern = "*.scenario.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
    };

    /// <exception cref="InvalidDataException">The file is not a valid scenario/1 script.</exception>
    public static ScenarioScript Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(File.ReadAllText(path), Path.GetFileName(path));
    }

    /// <exception cref="InvalidDataException">The text is not a valid scenario/1 script.</exception>
    public static ScenarioScript Parse(string json, string label)
    {
        ScenarioScript? script;
        try
        {
            script = JsonSerializer.Deserialize<ScenarioScript>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{label}: {ex.Message}", ex);
        }
        if (script is null) throw new InvalidDataException($"{label}: empty file.");

        var problems = Validate(script);
        if (problems.Count > 0) throw new InvalidDataException($"{label}: {string.Join("; ", problems)}");
        return string.IsNullOrEmpty(script.Name) ? script with { Name = StripSuffix(label) } : script;
    }

    private static List<string> Validate(ScenarioScript script)
    {
        var problems = new List<string>();
        if (script.Schema != ScenarioScript.SchemaId) problems.Add($"schema must be '{ScenarioScript.SchemaId}'");
        if (string.IsNullOrWhiteSpace(script.Mission)) problems.Add("mission is required");
        if (string.IsNullOrWhiteSpace(script.Difficulty)) problems.Add("difficulty is required");

        var labels = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < script.Commands.Count; i++)
        {
            var step = script.Commands[i];
            var at = $"commands[{i}]";
            if (step.Label is { } label && !labels.Add(label)) problems.Add($"{at}: duplicate label '{label}'");
            if (step.AtMetS is null && step.AtEvent is null && step.When is null) problems.Add($"{at}: needs atMetS, atEvent or when");
            if (step.AtMetS is not null && (step.AtEvent is not null || step.When is not null)) problems.Add($"{at}: atMetS excludes atEvent and when");
            if ((step.Action is null) == (step.Design is null)) problems.Add($"{at}: exactly one of action and design");
            if (step.DelayS < 0) problems.Add($"{at}: delayS must not be negative");
            if (step.AtEvent is { } key && !ScenarioEventKeys.IsWellFormed(key)) problems.Add($"{at}: unknown event key '{key}'");
            if (step.Zone is not null && step.Action is null) problems.Add($"{at}: zone needs an action");
        }
        foreach (var key in script.Expect.Events.Concat(script.Expect.NotEvents))
        {
            if (!ScenarioEventKeys.IsWellFormed(key)) problems.Add($"expect: unknown event key '{key}'");
        }
        return problems;
    }

    private static string StripSuffix(string fileName) =>
        fileName.EndsWith(".scenario.json", StringComparison.OrdinalIgnoreCase) ? fileName[..^".scenario.json".Length] : fileName;
}
