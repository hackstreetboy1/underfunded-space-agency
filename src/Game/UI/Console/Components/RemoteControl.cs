using Godot;
using Usa.Game.Presentation.Console.Systems;
using Usa.Game.UI.Console.Binding;

namespace Usa.Game.UI.Console.Components;

/// <summary>
/// The switch / breaker control of a systems-board row (<c>RemoteControl.tscn</c>, Figma G1-02 "Toggle", "Guarded switch",
/// "Button"; G1-03 nodes 15:624, 15:632, 15:648). Shows exactly one affordance per <see cref="RemoteControlMode"/> and
/// forwards clicks to the view model, which raises execute, confirmation or crew-order intents.
/// </summary>
public sealed partial class RemoteControl : HBoxContainer
{
    private readonly BindingScope _bindings = new();
    private CheckButton _toggle = null!;
    private GuardedSwitch _guarded = null!;
    private Button _action = null!;
    private Button _request = null!;
    private Label _crewState = null!;
    private RemoteControlViewModel? _control;
    private bool _crewOnly;
    private GuardState _guard;

    public override void _Ready()
    {
        _toggle = GetNode<CheckButton>("%Toggle");
        _guarded = GetNode<GuardedSwitch>("%Guarded");
        _action = GetNode<Button>("%Action");
        _request = GetNode<Button>("%Request");
        _crewState = GetNode<Label>("%CrewState");
        _toggle.Pressed += OnTogglePressed;
        _action.Pressed += () => _control?.Toggle();
        _request.Pressed += () => _control?.Request();
    }

    public override void _ExitTree() => Unbind();

    public void Bind(RemoteControlViewModel control)
    {
        ArgumentNullException.ThrowIfNull(control);
        Unbind();
        _control = control;
        _bindings.Observe(control.IsCrewOnly, crewOnly =>
        {
            _crewOnly = crewOnly;
            ApplyMode();
        });
        _bindings.Observe(control.Guard, guard =>
        {
            _guard = guard;
            ApplyMode();
        });
        _bindings.Observe(control.Label, label => _action.Text = label);
        _bindings.Observe(control.StateWord, word =>
        {
            _toggle.Text = word;
            _crewState.Text = word;
            ApplyMode();
        });
        _bindings.Observe(control.IsOn, on => _toggle.SetPressedNoSignal(on == true));
        _bindings.Observe(control.CanOperate, canOperate =>
        {
            _toggle.Disabled = !canOperate;
            _action.Disabled = !canOperate;
        });
        _bindings.Observe(control.DisabledReason, reason => TooltipText = reason ?? string.Empty);
        _guarded.Bind(control);
    }

    public void Unbind()
    {
        _bindings.Release();
        _guarded.Unbind();
        _control = null;
    }

    public void ApplyLabels(ConsoleLabels labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        _request.Text = labels.ControlRequest;
        _guarded.ApplyLabels(labels);
    }

    private void ApplyMode()
    {
        if (_control is null) return;
        var mode = RemoteControlModes.Of(_crewOnly, _guard, _control.Kind);
        _toggle.Visible = mode == RemoteControlMode.Toggle;
        _action.Visible = mode == RemoteControlMode.Action;
        _guarded.Visible = mode == RemoteControlMode.Guarded;
        _request.Visible = mode == RemoteControlMode.CrewRequest;
        _crewState.Visible = mode == RemoteControlMode.CrewRequest && _crewState.Text.Length > 0;
    }

    private void OnTogglePressed()
    {
        if (_control is null) return;
        // The position is the simulation's: undo the local flip, then ask for the change.
        _toggle.SetPressedNoSignal(_control.IsOn.Value == true);
        _control.Toggle();
    }
}
