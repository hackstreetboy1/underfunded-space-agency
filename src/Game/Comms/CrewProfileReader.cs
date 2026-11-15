using System.Text.Json;
using Usa.Game.Services.Ai.Contracts;
using Usa.Simulation.Data;
using Usa.Simulation.Kernel;

namespace Usa.Game.Comms;

/// <summary>
/// Reads only the comms-relevant fields of <c>crew/crew.json</c>; the full file belongs to the Crew area and may
/// grow fields comms does not care about.
/// </summary>
public static class CrewProfileReader
{
    public const string RelativePath = "crew/crew.json";
    private const int MaxSampleLines = 5;

    private static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Reads the crew of <paramref name="source"/>. The shared <c>crew/crew.json</c> holds no mission's crew: pass a
    /// <see cref="MissionDataSource"/> (or use the overload with a mission id) so the mission's own crew file is merged in.
    /// </summary>
    /// <exception cref="DefinitionException">Missing or malformed member fields.</exception>
    public static IReadOnlyList<CrewProfile> Read(IDataSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Parse(source.ReadText(RelativePath), RelativePath);
    }

    /// <summary>The crew of mission <paramref name="missionId"/> (shared plus mission crew files).</summary>
    /// <exception cref="DefinitionException">Missing or malformed member fields.</exception>
    public static IReadOnlyList<CrewProfile> Read(IDataSource source, string missionId) => Read(MissionDataSource.For(source, missionId));

    /// <returns>Profiles in id order.</returns>
    public static IReadOnlyList<CrewProfile> Parse(string json, string fileLabel = RelativePath)
    {
        try
        {
            using var document = JsonDocument.Parse(json, Options);
            var profiles = new List<CrewProfile>();
            foreach (var member in document.RootElement.GetProperty("members").EnumerateArray())
            {
                var name = Text(member, "name");
                var role = Text(member, "role");
                var persona = member.GetProperty("persona");
                profiles.Add(new CrewProfile(new CrewId(Text(member, "id")), name, role, Optional(member, "voiceId"), new CrewPersona(name)
                {
                    Role = role,
                    Personality = Optional(persona, "personality"),
                    Cadence = Optional(persona, "cadence"),
                    Warmth = Optional(persona, "warmth"),
                    Humor = Optional(persona, "humor"),
                    StressExpression = Optional(persona, "stressExpression"),
                    SampleLines = SampleLines(persona),
                }));
            }
            profiles.Sort((a, b) => a.Id.CompareTo(b.Id));
            return profiles;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            throw new DefinitionException(fileLabel, "$.members", ex.Message, ex);
        }
    }

    private static List<string> SampleLines(JsonElement persona)
    {
        var samples = new List<string>();
        if (!persona.TryGetProperty("sampleLines", out var lines)) return samples;
        foreach (var line in lines.EnumerateArray())
        {
            if (samples.Count < MaxSampleLines && line.GetString() is { Length: > 0 } text) samples.Add(text);
        }
        return samples;
    }

    private static string Text(JsonElement element, string property) =>
        element.GetProperty(property).GetString() is { Length: > 0 } text
            ? text
            : throw new InvalidOperationException($"'{property}' is empty.");

    private static string Optional(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
