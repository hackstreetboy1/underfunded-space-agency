using System.Globalization;
using Usa.Simulation.Commands.Catalog;

namespace Usa.Simulation.Commands;

/// <summary>Outcome of parsing arguments: <see cref="Arguments"/> on success, otherwise a reason code naming the parameter in <see cref="Detail"/>.</summary>
public readonly record struct ArgumentParseResult(ParsedArguments? Arguments, string? ReasonCode, string? Detail)
{
    public bool Succeeded => ReasonCode is null;
}

/// <summary>Parses raw arguments against an action's typed parameter schema.</summary>
public static class ActionArguments
{
    public static ArgumentParseResult Parse(CatalogAction action, IReadOnlyDictionary<string, CommandArg> args)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(args);

        foreach (var name in args.Keys.Order(StringComparer.Ordinal))
        {
            if (action.FindParameter(name) is null) return Fail(CommandReasons.UnknownParameter, name);
        }

        var values = new SortedDictionary<string, CommandArg>(StringComparer.Ordinal);
        foreach (var parameter in action.Parameters)
        {
            if (!args.TryGetValue(parameter.Name, out var raw)) return Fail(CommandReasons.MissingParameter, parameter.Name);
            var reason = TryParse(parameter, raw, out var value);
            if (reason is not null) return Fail(reason, parameter.Name);
            values[parameter.Name] = value;
        }
        return new ArgumentParseResult(new ParsedArguments(values), null, null);
    }

    /// <returns>Null on success, otherwise the reason code.</returns>
    private static string? TryParse(ActionParameter parameter, CommandArg raw, out CommandArg value)
    {
        value = raw;
        switch (parameter.Type)
        {
            case ParameterType.String:
                if (raw.Kind != CommandArgKind.Text) return CommandReasons.InvalidParameterType;
                return string.IsNullOrWhiteSpace(raw.AsText()) ? CommandReasons.InvalidParameterValue : null;

            case ParameterType.Enum:
                if (raw.Kind != CommandArgKind.Text) return CommandReasons.InvalidParameterType;
                var match = MatchEnum(parameter.Values ?? [], raw.AsText());
                if (match is null) return CommandReasons.InvalidParameterValue;
                value = CommandArg.Of(match);
                return null;

            case ParameterType.Boolean:
                if (raw.Kind == CommandArgKind.Boolean) return null;
                if (raw.Kind != CommandArgKind.Text) return CommandReasons.InvalidParameterType;
                if (string.Equals(raw.AsText(), "true", StringComparison.OrdinalIgnoreCase)) value = CommandArg.Of(true);
                else if (string.Equals(raw.AsText(), "false", StringComparison.OrdinalIgnoreCase)) value = CommandArg.Of(false);
                else return CommandReasons.InvalidParameterType;
                return null;

            default:
                return TryParseNumber(parameter, raw, out value);
        }
    }

    private static string? TryParseNumber(ActionParameter parameter, CommandArg raw, out CommandArg value)
    {
        value = raw;
        double number;
        if (raw.Kind == CommandArgKind.Number) number = raw.AsNumber();
        else if (raw.Kind != CommandArgKind.Text ||
                 !double.TryParse(raw.AsText(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            return CommandReasons.InvalidParameterType;

        if (!double.IsFinite(number)) return CommandReasons.InvalidParameterValue;
        if (parameter.Type == ParameterType.Integer && Math.Floor(number) != number) return CommandReasons.InvalidParameterValue;
        if (number < parameter.Min || number > parameter.Max) return CommandReasons.ParameterOutOfRange;
        value = CommandArg.Of(number);
        return null;
    }

    /// <summary>Exact match first; a case-insensitive match is accepted and normalised to the declared value.</summary>
    private static string? MatchEnum(IReadOnlyList<string> values, string text)
    {
        foreach (var candidate in values)
        {
            if (string.Equals(candidate, text, StringComparison.Ordinal)) return candidate;
        }
        foreach (var candidate in values)
        {
            if (string.Equals(candidate, text, StringComparison.OrdinalIgnoreCase)) return candidate;
        }
        return null;
    }

    private static ArgumentParseResult Fail(string reason, string parameter) => new(null, reason, parameter);
}
