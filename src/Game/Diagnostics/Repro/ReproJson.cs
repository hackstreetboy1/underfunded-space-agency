using System.Text.Json;
using Usa.Simulation.Commands;
using Usa.Simulation.Data;

namespace Usa.Game.Diagnostics.Repro;

/// <summary>A parsed repro: the document and its command log (null when it was captured without a mission).</summary>
public sealed record ReproFile(ReproDocument Document, CommandLog? CommandLog);

/// <summary>Reads and writes <see cref="ReproDocument"/> JSON.</summary>
public static class ReproJson
{
    public static string Serialize(ReproDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, ReproJsonContext.Default.ReproDocument);
    }

    /// <exception cref="InvalidDataException">Not JSON, the wrong schema, or a malformed command log.</exception>
    public static ReproFile Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        ReproDocument? document;
        try
        {
            document = JsonSerializer.Deserialize(json, ReproJsonContext.Default.ReproDocument);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Not a repro file: {ex.Message}", ex);
        }
        if (document is null) throw new InvalidDataException("Not a repro file: empty document.");
        if (document.Schema != ReproDocument.SchemaTag)
            throw new InvalidDataException($"Expected schema '{ReproDocument.SchemaTag}' but found '{document.Schema}'.");
        if (document.Mission is not null && document.CommandLog is null)
            throw new InvalidDataException("The repro names a mission but has no command log.");

        CommandLog? log = null;
        if (document.CommandLog is { } element)
        {
            try
            {
                log = CommandLog.FromJson(element.GetRawText(), "repro.commandLog");
            }
            catch (DefinitionException ex)
            {
                throw new InvalidDataException($"The repro's command log is malformed: {ex.Message}", ex);
            }
        }
        return new ReproFile(document with { Hashes = document.Hashes ?? [] }, log);
    }
}
