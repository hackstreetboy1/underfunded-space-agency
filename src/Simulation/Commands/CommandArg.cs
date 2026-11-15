using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Commands;

public enum CommandArgKind
{
    Text,
    Number,
    Boolean,
}

/// <summary>
/// A command argument as supplied by the UI (typed) or the AI (text). The validator parses it against
/// the action's parameter schema; serialized as a plain JSON string, number or boolean.
/// </summary>
[JsonConverter(typeof(CommandArgJsonConverter))]
public readonly record struct CommandArg
{
    private readonly string? _text;
    private readonly double _number;

    private CommandArg(CommandArgKind kind, string? text, double number)
    {
        Kind = kind;
        _text = text;
        _number = number;
    }

    public CommandArgKind Kind { get; }

    public static CommandArg Of(string text) => new(CommandArgKind.Text, text ?? throw new ArgumentNullException(nameof(text)), 0);

    public static CommandArg Of(double number) => new(CommandArgKind.Number, null, number);

    public static CommandArg Of(bool value) => new(CommandArgKind.Boolean, null, value ? 1 : 0);

    public string AsText() => Kind == CommandArgKind.Text
        ? _text!
        : throw new InvalidOperationException($"Argument {this} is not text.");

    public double AsNumber() => Kind == CommandArgKind.Number
        ? _number
        : throw new InvalidOperationException($"Argument {this} is not a number.");

    public bool AsBool() => Kind == CommandArgKind.Boolean
        ? _number != 0
        : throw new InvalidOperationException($"Argument {this} is not a boolean.");

    public StateValue ToStateValue() => Kind switch
    {
        CommandArgKind.Text => StateValue.Of(_text!),
        CommandArgKind.Number => StateValue.Of(_number),
        _ => StateValue.Of(_number != 0),
    };

    /// <summary>Culture-invariant text used for templates, impact rules and hashing.</summary>
    public string ToCanonicalString() => Kind switch
    {
        CommandArgKind.Text => _text!,
        CommandArgKind.Number => _number.ToString("R", CultureInfo.InvariantCulture),
        _ => _number != 0 ? "true" : "false",
    };

    public override string ToString() => ToCanonicalString();
}

internal sealed class CommandArgJsonConverter : JsonConverter<CommandArg>
{
    public override CommandArg Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => CommandArg.Of(reader.GetString()!),
            JsonTokenType.Number => CommandArg.Of(reader.GetDouble()),
            JsonTokenType.True => CommandArg.Of(true),
            JsonTokenType.False => CommandArg.Of(false),
            _ => throw new JsonException("A command argument is a string, number or boolean."),
        };

    public override void Write(Utf8JsonWriter writer, CommandArg value, JsonSerializerOptions options)
    {
        switch (value.Kind)
        {
            case CommandArgKind.Text: writer.WriteStringValue(value.AsText()); break;
            case CommandArgKind.Number: writer.WriteNumberValue(value.AsNumber()); break;
            default: writer.WriteBooleanValue(value.AsBool()); break;
        }
    }
}
