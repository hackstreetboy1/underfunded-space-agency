using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.Console.Systems;
using Usa.Simulation.Commands;
using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Kernel;

namespace Usa.Game.Integration.Adapters;

/// <summary>
/// Which remote action (<c>catalog/actions.json</c>, kind <c>remote</c>) drives a switch or breaker, read from the actions'
/// effects: a literal target <c>&lt;component&gt;.closed</c>, or a template <c>{param}.closed</c> whose enum parameter
/// lists the component. A component no remote action reaches is crew-only on the systems board.
/// </summary>
public sealed class RemoteControlMap
{
    private const string ClosedSuffix = ".closed";

    private readonly Dictionary<ComponentId, Binding> _bindings = [];

    public RemoteControlMap(ActionCatalog actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        foreach (var action in actions.Actions)
        {
            if (action.Kind != ActionKind.Remote || action.Definition.Effects is not { } effects) continue;
            foreach (var effect in effects)
            {
                if (effect.Arg is not { } valueArg || !effect.Target.EndsWith(ClosedSuffix, StringComparison.Ordinal)) continue;
                var target = effect.Target[..^ClosedSuffix.Length];
                if (target.StartsWith('{') && target.EndsWith('}'))
                {
                    if (action.FindParameter(target[1..^1]) is not { Values: { } values } parameter) continue;
                    foreach (var value in values) _bindings.TryAdd(new ComponentId(value), new Binding(action, parameter.Name, valueArg));
                }
                else if (!target.Contains('{', StringComparison.Ordinal))
                {
                    _bindings.TryAdd(new ComponentId(target), new Binding(action, null, valueArg));
                }
            }
        }
    }

    public bool IsRemote(ComponentId component) => _bindings.ContainsKey(component);

    /// <summary>The action's base impact is not low, so the board guards the switch and asks for confirmation.</summary>
    public bool IsGuarded(ComponentId component) =>
        _bindings.TryGetValue(component, out var binding) && binding.Action.BaseImpact != ActionImpact.Low;

    /// <summary>The <c>RemoteControl</c> request for <paramref name="intent"/>, or null when no remote action drives it.</summary>
    public CommandRequest? Request(RemoteControlIntent intent, CommandId id)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (!_bindings.TryGetValue(intent.ComponentId, out var binding)) return null;
        bool closed;
        switch (intent.DesiredState)
        {
            case DesiredControlState.On:
                closed = true;
                break;
            case DesiredControlState.Off:
                closed = false;
                break;
            default:
                return null;
        }
        var args = new Dictionary<string, CommandArg>(StringComparer.Ordinal) { [binding.ValueArg] = CommandArg.Of(closed) };
        if (binding.ComponentParameter is { } parameter) args[parameter] = CommandArg.Of(intent.ComponentId.Value);
        return new CommandRequest(id, CommandSource.RemoteControl, binding.Action.Id, null, args, intent.Confirmed);
    }

    /// <summary>
    /// What switching <paramref name="component"/> on or off does, from the action's data: its <c>impact.*</c> effect keys (the
    /// action's own and those of every matching parameter rule), whether the change is high-impact and whether it is irreversible.
    /// </summary>
    /// <returns><see cref="ControlImpact.None"/> when no remote action drives the component or the arguments do not parse.</returns>
    public ControlImpact ImpactOf(ComponentId component, bool on)
    {
        if (!_bindings.TryGetValue(component, out var binding)) return ControlImpact.None;
        var args = new Dictionary<string, CommandArg>(StringComparer.Ordinal) { [binding.ValueArg] = CommandArg.Of(on) };
        if (binding.ComponentParameter is { } parameter) args[parameter] = CommandArg.Of(component.Value);
        var parsed = ActionArguments.Parse(binding.Action, args);
        if (parsed.Arguments is not { } arguments) return ControlImpact.None;
        var impact = binding.Action.ImpactFor(arguments);
        return new ControlImpact(binding.Action.EffectTextKeysFor(arguments), impact != ActionImpact.Low, impact == ActionImpact.Irreversible);
    }

    private sealed record Binding(CatalogAction Action, string? ComponentParameter, string ValueArg);
}
