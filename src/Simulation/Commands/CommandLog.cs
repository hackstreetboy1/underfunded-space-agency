using System.Text.Json;
using Usa.Simulation.Data;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Commands;

/// <summary>One submitted request and the MET at which it was submitted.</summary>
public sealed record CommandLogEntry(long MetMs, CommandRequest Request);

/// <summary>
/// Append-only record of every non-duplicate submission, in submission order. Saves and scenario
/// replays store it as JSON (schema <c>command_log/1</c>); resume and replay feed it back through
/// <see cref="CommandProcessor.Replay"/>.
/// </summary>
public sealed class CommandLog : IHashContributor
{
    public const string SchemaTag = "command_log/1";

    private readonly List<CommandLogEntry> _entries = [];

    public IReadOnlyList<CommandLogEntry> Entries => _entries;

    public void Append(MissionTime now, CommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_entries.Count > 0 && now.Milliseconds < _entries[^1].MetMs)
            throw new InvalidOperationException($"Command {request.Id} at {now} is earlier than the last logged command.");
        _entries.Add(new CommandLogEntry(now.Milliseconds, request));
    }

    public string ToJson()
    {
        var records = new CommandLogRecord[_entries.Count];
        for (var i = 0; i < _entries.Count; i++)
        {
            var (metMs, request) = _entries[i];
            var args = new SortedDictionary<string, CommandArg>(StringComparer.Ordinal);
            foreach (var (name, value) in request.Args) args[name] = value;
            records[i] = new CommandLogRecord(
                metMs,
                new CommandRequestRecord(request.Id.Value, request.Source, request.Action, request.Assignee, args, request.Confirmed));
        }
        return JsonSerializer.Serialize(new CommandLogFile(SchemaTag, records), DataJson.Options);
    }

    /// <exception cref="DefinitionException">Malformed JSON, wrong schema, empty ids or out-of-order entries.</exception>
    public static CommandLog FromJson(string json, string fileLabel = "command_log")
    {
        var file = DefinitionLoader.Deserialize<CommandLogFile>(fileLabel, json);
        var errors = new DefinitionErrors();
        if (file.Schema != SchemaTag) errors.Add(fileLabel, "$.schema", $"Expected schema '{SchemaTag}' but found '{file.Schema}'.");

        var log = new CommandLog();
        var previousMs = long.MinValue;
        for (var i = 0; i < file.Entries.Count; i++)
        {
            var (metMs, record) = file.Entries[i];
            var path = $"$.entries[{i}]";
            if (string.IsNullOrWhiteSpace(record.Id)) errors.Add(fileLabel, path + ".request.id", "Command id is empty.");
            if (metMs < previousMs) errors.Add(fileLabel, path + ".metMs", "Entries must be in MET order.");
            previousMs = metMs;

            var request = new CommandRequest(
                new CommandId(record.Id),
                record.Source,
                record.Action,
                record.Assignee,
                record.Args ?? CommandRequest.NoArgs,
                record.Confirmed);
            log._entries.Add(new CommandLogEntry(metMs, request));
        }

        errors.ThrowIfAny();
        return log;
    }

    public static CommandLog Load(DefinitionLoader loader, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return FromJson(loader.Source.ReadText(relativePath), relativePath);
    }

    public void WriteHash(StateHashWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write((long)_entries.Count);
        foreach (var (metMs, request) in _entries)
        {
            writer.Write(metMs);
            writer.Write(request.Id.Value);
            writer.Write((long)request.Source);
            writer.Write(request.Action.Value);
            writer.Write(request.Assignee?.Value ?? "");
            writer.Write((long)request.Args.Count);
            foreach (var name in request.Args.Keys.Order(StringComparer.Ordinal))
            {
                var value = request.Args[name];
                writer.Write(name);
                writer.Write((long)value.Kind);
                writer.Write(value.ToCanonicalString());
            }
            writer.Write(request.Confirmed);
        }
    }

    private sealed record CommandLogFile(string Schema, IReadOnlyList<CommandLogRecord> Entries);

    private sealed record CommandLogRecord(long MetMs, CommandRequestRecord Request);

    private sealed record CommandRequestRecord(
        string Id,
        CommandSource Source,
        ActionId Action,
        CrewId? Assignee = null,
        IReadOnlyDictionary<string, CommandArg>? Args = null,
        bool Confirmed = false);
}
