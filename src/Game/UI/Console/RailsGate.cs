using Godot;
using Usa.Game.UI.Console.Binding;
using Usa.Game.UI.Console.Components;
using Usa.Game.UI.Console.Regions;
using Usa.Simulation.Guidance;

namespace Usa.Game.UI.Console;

/// <summary>
/// Applies the tutorial rails (<see cref="ITutorialRails"/>) to the console's actionable controls through one
/// <see cref="RailsControlGate{T}"/>: every control the rails do not allow now is disabled with the tooltip "Follow the guide —
/// step N of 23" and gets its own state back once allowed. Which rails control a node stands for comes from
/// <see cref="ControlOf"/>: the <see cref="ControlMeta"/> set where the control is built, else its name or its component.
/// </summary>
public sealed class RailsGate
{
    /// <summary>Node meta naming the rails control (<see cref="TutorialControls"/>) of a control built in code.</summary>
    public const string ControlMeta = "rails_control";

    private readonly RailsControlGate<Control> _gate = new();

    /// <summary>Walks <paramref name="roots"/> and gates every classified control; called from the console's 10 Hz pump.</summary>
    public void Apply(IReadOnlyList<Node> roots, ITutorialRails? rails)
    {
        ArgumentNullException.ThrowIfNull(roots);
        if (rails is not { IsEngaged: true } && !_gate.HoldsAny) return;
        foreach (var root in roots)
        {
            if (GodotObject.IsInstanceValid(root)) Walk(root, rails);
        }
        _gate.Forget(control => !GodotObject.IsInstanceValid(control));
    }

    /// <summary>The rails control a console node stands for; null for a control the rails never gate.</summary>
    public static string? ControlOf(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (control.HasMeta(ControlMeta)) return control.GetMeta(ControlMeta).AsString();
        if (control is not BaseButton) return null;
        if (control.Name == "Ask") return TutorialControls.ReadoutAsk;
        if (control.Name == "Ack") return TutorialControls.AlarmAck;
        for (var parent = control.GetParent(); parent is not null and not MissionConsole; parent = parent.GetParent())
        {
            switch (parent)
            {
                case RemoteControl or GuardedSwitch:
                    return TutorialControls.SystemsSwitch;
                case TaskBar:
                    return TutorialControls.TaskStop;
                case TipCard:
                    return TutorialControls.TipCard;
            }
        }
        return null;
    }

    private void Walk(Node node, ITutorialRails? rails)
    {
        if (node is BaseButton button && ControlOf(button) is { } id
            && _gate.Gate(button, id, rails, new RailsControlState(button.Disabled, button.TooltipText)) is { } state)
        {
            button.Disabled = state.Disabled;
            button.TooltipText = state.Tooltip;
        }
        for (var i = 0; i < node.GetChildCount(); i++) Walk(node.GetChild(i), rails);
    }
}
