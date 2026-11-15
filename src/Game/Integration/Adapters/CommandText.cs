using Usa.Game.Content;
using Usa.Simulation.Commands;
using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Kernel;
using Usa.Simulation.Session;

namespace Usa.Game.Integration.Adapters;

/// <summary>
/// Player-facing words for an order and the target its arguments name, never a raw id: "Cycle OAMS thruster breakers
/// T1–T4", "Arm RCS ring A", "Close OAMS T1 breaker" (the crew's "go ahead" offer, <see cref="Comms.ICommsSessionPort.DescribeCommand"/>).
/// <para>An action with a <c>console.commandPhrase.&lt;action&gt;</c> string is worded by it, its <c>{parameter}</c>
/// placeholders filled with the arguments' text; any other is its task title (else its description) followed by the
/// arguments' text in catalog order (<c>console.commandWithTarget</c>). An argument's text is, in order: the
/// <c>console.commandArg.&lt;action&gt;.&lt;parameter&gt;.&lt;value&gt;</c> string; the label of the fact or the name of
/// the component it names; a number, through <c>console.commandArg.&lt;action&gt;.&lt;parameter&gt;</c> when that
/// gives it a unit. An argument nothing names is left out rather than shown as an id.</para>
/// </summary>
public sealed class CommandText
{
    public const string PhrasePrefix = "console.commandPhrase.";
    public const string ArgumentPrefix = "console.commandArg.";
    private const string WithTargetKey = "console.commandWithTarget";

    private readonly MissionData _data;
    private readonly AdapterText _text;
    private readonly Dictionary<string, string> _componentNames = new(StringComparer.Ordinal);

    public CommandText(MissionData data, AdapterText? text = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
        _text = text ?? AdapterText.Default;
        foreach (var vehicle in data.Vehicles)
        {
            foreach (var component in vehicle.Components) _componentNames[component.Id.Value] = component.Name;
        }
    }

    public string Describe(CommandRequest command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!_data.Actions.TryGet(command.Action, out var action)) return AdapterText.Words(command.Action.Value);

        if (_text.Find(PhrasePrefix + action.Id.Value) is { } phrase)
        {
            var complete = true;
            var worded = TextTemplate.Fill(phrase, name => ArgumentText(action.Definition, command, name), _ => complete = false);
            if (complete) return worded;
        }

        var targets = new List<string>();
        foreach (var parameter in action.Definition.Parameters ?? [])
        {
            if (ArgumentText(action.Definition, command, parameter.Name) is { } target) targets.Add(target);
        }
        var title = Title(action.Definition);
        return targets.Count == 0
            ? title
            : TextTemplate.Fill(_text.Get(WithTargetKey), name => name switch
            {
                "action" => title,
                "target" => string.Join(", ", targets),
                _ => null,
            });
    }

    /// <summary>The display text of one argument value; null when nothing names it.</summary>
    public string? DescribeValue(ActionDefinition action, string parameter, CommandArg value)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(parameter);
        var canonical = value.ToCanonicalString();
        var key = $"{ArgumentPrefix}{action.Id.Value}.{parameter}";
        if (_text.Find($"{key}.{canonical}") is { } authored) return authored;

        if (value.Kind == CommandArgKind.Number)
        {
            var number = value.AsNumber();
            var formatted = number == Math.Round(number) ? ContentFormat.WholeNumber(number) : ContentFormat.OneDecimal(number);
            return _text.Find(key) is { } unit ? TextTemplate.Fill(unit, name => name == "value" ? formatted : null) : formatted;
        }
        if (value.Kind != CommandArgKind.Text) return null;
        if (_data.Facts.TryResolve(new FactId(canonical), out var fact)) return fact.Label;
        return _componentNames.GetValueOrDefault(canonical) ?? AffectedComponent(action, parameter, canonical);
    }

    private string? ArgumentText(ActionDefinition action, CommandRequest command, string parameter) =>
        command.Args.TryGetValue(parameter, out var value) ? DescribeValue(action, parameter, value) : null;

    /// <summary>A value the action's <c>affects</c> template turns into a component id (ring <c>a</c> into <c>tandem/rcs/ring_a</c>).</summary>
    private string? AffectedComponent(ActionDefinition action, string parameter, string value)
    {
        var placeholder = "{" + parameter + "}";
        foreach (var template in action.Affects ?? [])
        {
            if (!template.Contains(placeholder, StringComparison.Ordinal)) continue;
            var id = template.Replace(placeholder, value, StringComparison.Ordinal);
            if (_componentNames.TryGetValue(id, out var name)) return name;
        }
        return null;
    }

    private string Title(ActionDefinition action) =>
        action.TaskId is { } task && _data.Tasks.TryGet(task, out var definition) ? definition.Name : action.Description.TrimEnd('.');
}
