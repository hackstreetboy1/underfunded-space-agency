using System.Text.Json;
using System.Text.Json.Serialization;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Data;

/// <summary>
/// The one JSON configuration for all data files: camelCase properties, snake_case enum strings,
/// unknown properties rejected, nullable annotations and required constructor parameters enforced.
/// </summary>
public static class DataJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        options.Converters.Add(new StringIdConverter<ComponentId>(v => new ComponentId(v), id => id.Value));
        options.Converters.Add(new StringIdConverter<FactId>(v => new FactId(v), id => id.Value));
        options.Converters.Add(new StringIdConverter<ActionId>(v => new ActionId(v), id => id.Value));
        options.Converters.Add(new StringIdConverter<TaskId>(v => new TaskId(v), id => id.Value));
        options.Converters.Add(new StringIdConverter<CrewId>(v => new CrewId(v), id => id.Value));
        options.Converters.Add(new StringIdConverter<ObjectiveId>(v => new ObjectiveId(v), id => id.Value));
        options.Converters.Add(new StringIdConverter<IncidentId>(v => new IncidentId(v), id => id.Value));
        options.Converters.Add(new StringIdConverter<PartId>(v => new PartId(v), id => id.Value));
        options.Converters.Add(new StateValueJsonConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}

/// <summary>Reads and writes a typed id as a JSON string, including as a dictionary key.</summary>
public sealed class StringIdConverter<T>(Func<string, T> create, Func<T, string> value) : JsonConverter<T>
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException($"Expected a string id for {typeof(T).Name}.");
        return Create(reader.GetString()!);
    }

    public override void Write(Utf8JsonWriter writer, T id, JsonSerializerOptions options) => writer.WriteStringValue(value(id));

    public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Create(reader.GetString()!);

    public override void WriteAsPropertyName(Utf8JsonWriter writer, T id, JsonSerializerOptions options) =>
        writer.WritePropertyName(value(id));

    private T Create(string text)
    {
        try
        {
            return create(text);
        }
        catch (ArgumentException ex)
        {
            throw new JsonException(ex.Message, ex);
        }
    }
}

/// <summary>Maps JSON number/bool/string/null to <see cref="StateValue"/>.</summary>
public sealed class StateValueJsonConverter : JsonConverter<StateValue>
{
    public override StateValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Number => StateValue.Of(reader.GetDouble()),
            JsonTokenType.True => StateValue.Of(true),
            JsonTokenType.False => StateValue.Of(false),
            JsonTokenType.String => StateValue.Of(reader.GetString()!),
            JsonTokenType.Null => StateValue.None,
            _ => throw new JsonException("Expected a number, boolean, string or null."),
        };

    public override void Write(Utf8JsonWriter writer, StateValue value, JsonSerializerOptions options)
    {
        switch (value.Kind)
        {
            case StateValueKind.Number: writer.WriteNumberValue(value.AsNumber()); break;
            case StateValueKind.Bool: writer.WriteBooleanValue(value.AsBool()); break;
            case StateValueKind.Text: writer.WriteStringValue(value.AsText()); break;
            default: writer.WriteNullValue(); break;
        }
    }
}
