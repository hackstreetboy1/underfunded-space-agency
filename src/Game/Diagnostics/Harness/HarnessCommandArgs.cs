using System.Globalization;
using Usa.Simulation.Commands;
using Usa.Simulation.Commands.Catalog;

namespace Usa.Game.Diagnostics.Harness;

/// <summary>
/// Parses the debug command box: <c>name=value</c> pairs separated by commas or spaces. A value is typed by the action's
/// parameter of that name (number, integer, boolean, else text); unknown names fall back to number, then boolean, then text.
/// </summary>
public static class HarnessCommandArgs
{
    private static readonly char[] Separators = [',', ' ', '\t', ';'];

    public static bool TryParse(string text, CatalogAction? action, out Dictionary<string, CommandArg> args, out string? error)
    {
        args = new Dictionary<string, CommandArg>(StringComparer.Ordinal);
        error = null;
        foreach (var token in (text ?? "").Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var equals = token.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0 || equals == token.Length - 1)
            {
                error = $"'{token}' is not name=value";
                return false;
            }

            var name = token[..equals];
            var value = token[(equals + 1)..].Trim('\'', '"');
            if (!TryConvert(value, action?.FindParameter(name)?.Type, out var arg))
            {
                error = $"'{value}' is not a valid {action?.FindParameter(name)?.Type} for '{name}'";
                return false;
            }
            args[name] = arg;
        }
        return true;
    }

    private static bool TryConvert(string value, ParameterType? type, out CommandArg arg)
    {
        var isNumber = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number);
        var isBool = bool.TryParse(value, out var flag);
        switch (type)
        {
            case ParameterType.Number or ParameterType.Integer:
                arg = CommandArg.Of(number);
                return isNumber;
            case ParameterType.Boolean:
                arg = CommandArg.Of(flag);
                return isBool;
            case ParameterType.String or ParameterType.Enum:
                arg = CommandArg.Of(value);
                return true;
            default:
                arg = isNumber ? CommandArg.Of(number) : isBool ? CommandArg.Of(flag) : CommandArg.Of(value);
                return true;
        }
    }
}
