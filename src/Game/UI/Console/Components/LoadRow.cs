using Godot;
using Usa.Game.Presentation.Console.Systems;
using Usa.Game.UI.Console.Binding;

namespace Usa.Game.UI.Console.Components;

/// <summary>
/// A load-list row (<c>LoadRow.tscn</c>, UI.md 3.9): name, priority group chip, W, heat W, condition, position or switch,
/// and the "what depends on this" tooltip. No Gate 1 frame shows it; the layout follows the readout row.
/// </summary>
public sealed partial class LoadRow : HBoxContainer
{
    private readonly BindingScope _bindings = new();
    private Label _name = null!;
    private BadgeView _priority = null!;
    private Label _power = null!;
    private Label _heat = null!;
    private BadgeView _condition = null!;
    private Label _state = null!;
    private RemoteControl _switch = null!;

    public override void _Ready()
    {
        _name = GetNode<Label>("%Name");
        _priority = GetNode<BadgeView>("%Priority");
        _power = GetNode<Label>("%Power");
        _heat = GetNode<Label>("%Heat");
        _condition = GetNode<BadgeView>("%Condition");
        _state = GetNode<Label>("%State");
        _switch = GetNode<RemoteControl>("%Switch");
    }

    public override void _ExitTree() => Unbind();

    public void ApplyLabels(ConsoleLabels labels) => _switch.ApplyLabels(labels);

    public void Bind(LoadRowViewModel load)
    {
        ArgumentNullException.ThrowIfNull(load);
        Unbind();
        _bindings.Observe(load.Name, name => _name.Text = name);
        _bindings.Observe(load.PriorityWord, word => _priority.Display(word, null));
        _bindings.Observe(load.PowerText, text => _power.Text = text);
        _bindings.Observe(load.HeatText, text => _heat.Text = text);
        _bindings.Observe(load.Condition, _condition.Display);
        _bindings.Observe(load.StateWord, word => _state.Text = word);
        _bindings.Observe(load.DependentsText, text => TooltipText = text);
        _state.Visible = load.Switch is null;
        _switch.Visible = load.Switch is not null;
        if (load.Switch is { } control) _switch.Bind(control);
    }

    public void Unbind()
    {
        _bindings.Release();
        _switch.Unbind();
    }
}
