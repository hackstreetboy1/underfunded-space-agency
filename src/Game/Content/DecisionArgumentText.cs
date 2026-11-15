using System.Globalization;
using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Kernel;

namespace Usa.Game.Content;

/// <summary>
/// The display text of one decision argument, for the placeholder of the same name in a <c>timeline.decision.*</c> text:
/// a component id is named from the vehicle data; a value the action's <c>affects</c> template turns into a component id
/// (ring <c>a</c> into <c>tandem/rcs/ring_a</c>) likewise; any other value comes from
/// <see cref="DebriefDefinition.DecisionValues"/>; numbers are formatted (0 dp when whole, else 1 dp).
/// </summary>
public static class DecisionArgumentText
{
    public static string ValueKey(ActionId action, string parameter, string value) => $"{action.Value}.{parameter}.{value}";

    /// <param name="mission">Component names and the action's parameters; without it only the authored values and numbers resolve.</param>
    /// <returns>Null when nothing names the value.</returns>
    public static string? Resolve(DebriefDefinition debrief, DebriefMissionReferences? mission, ActionId action, string parameter, string value)
    {
        ArgumentNullException.ThrowIfNull(debrief);
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(value);

        ActionDefinition? definition = null;
        if (mission is not null)
        {
            if (mission.ComponentName(value) is { } component) return component;
            if (mission.TryGetDecisionAction(action, out var found))
            {
                definition = found;
                if (AffectedComponent(mission, found, parameter, value) is { } affected) return affected;
            }
        }

        if (debrief.DecisionValues.TryGetValue(ValueKey(action, parameter, value), out var text) && !string.IsNullOrWhiteSpace(text)) return text;

        var type = definition?.Parameters?.FirstOrDefault(p => p.Name == parameter)?.Type;
        if ((type is null or ParameterType.Number or ParameterType.Integer) &&
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number))
            return number == Math.Round(number) ? ContentFormat.WholeNumber(number) : ContentFormat.OneDecimal(number);
        return null;
    }

    /// <summary>Every value of <paramref name="parameter"/> that nothing can name; empty for numbers. Null for a free-text parameter.</summary>
    public static IReadOnlyList<string>? Unnamed(DebriefDefinition debrief, DebriefMissionReferences mission, ActionDefinition action, ActionParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(parameter);
        IReadOnlyList<string> values;
        switch (parameter.Type)
        {
            case ParameterType.Enum:
                values = parameter.Values ?? [];
                break;
            case ParameterType.Boolean:
                values = ["true", "false"];
                break;
            case ParameterType.Number or ParameterType.Integer:
                return [];
            default:
                return null;
        }
        return [.. values.Where(v => Resolve(debrief, mission, action.Id, parameter.Name, v) is null)];
    }

    private static string? AffectedComponent(DebriefMissionReferences mission, ActionDefinition action, string parameter, string value)
    {
        var placeholder = "{" + parameter + "}";
        foreach (var template in action.Affects ?? [])
        {
            if (!template.Contains(placeholder, StringComparison.Ordinal)) continue;
            var id = template.Replace(placeholder, value, StringComparison.Ordinal);
            if (!id.Contains('{', StringComparison.Ordinal) && mission.ComponentName(id) is { } name) return name;
        }
        return null;
    }
}
