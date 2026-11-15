using System.Text.Json;
using System.Text.Json.Serialization;
using Usa.Simulation.Commands;
using Usa.Simulation.Data;
using Usa.Simulation.Kernel;
using Usa.Simulation.Session;

namespace Usa.Game.Integration;

/// <summary>
/// The opaque payload of a resume save (SCRIPTING 11.9): the run's seed, its command log (workspace designs travel in it
/// as <c>register_repair_design</c> entries) and the MET to replay to. Variant picks follow from the seed.
/// </summary>
public sealed record ResumePayload(int Schema, string MissionId, string DifficultyId, ulong Seed, long MetMs, string CommandLog)
{
    public const int CurrentSchema = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static ResumePayload Capture(MissionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new ResumePayload(CurrentSchema, session.MissionId, session.DifficultyId, session.Seed, session.Now.Milliseconds, session.CommandLog.ToJson());
    }

    public string Serialize() => JsonSerializer.Serialize(this, Options);

    /// <exception cref="DefinitionException">The payload is unreadable or from another schema.</exception>
    public static ResumePayload Parse(string json)
    {
        ResumePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ResumePayload>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new DefinitionException("resume", "$", $"Unreadable resume payload: {ex.Message}");
        }
        if (payload is null || payload.Schema != CurrentSchema)
            throw new DefinitionException("resume", "$.schema", $"Unsupported resume payload schema {payload?.Schema}.");
        return payload;
    }

    /// <exception cref="DefinitionException">The command log is unreadable.</exception>
    public CommandLog ReadCommandLog() => Usa.Simulation.Commands.CommandLog.FromJson(CommandLog, "resume.commandLog");

    public MissionTime Met => new(MetMs);
}
