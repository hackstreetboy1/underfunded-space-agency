using Godot;
using Usa.Game.UI.Theme;
using Usa.Game.Presentation.Console.Systems;
using Usa.Game.UI.Console.Binding;

namespace Usa.Game.UI.Console.Components;

/// <summary>
/// A guarded switch for a critical load (<c>GuardedSwitch.tscn</c>, Figma G1-02 "Guarded switch" Guarded / Cover open /
/// Armed / Disabled; G1-03 node 15:632): open the hinged cover, flip the switch, then confirm in the dialog (UI.md 3.9).
/// The switch shows the reported position only; the simulation moves it.
/// </summary>
public sealed partial class GuardedSwitch : VBoxContainer
{
    private readonly BindingScope _bindings = new();
    private Button _cover = null!;
    private Control _armed = null!;
    private CheckButton _switch = null!;
    private Button _close = null!;
    private Label _awaiting = null!;
    private RemoteControlViewModel? _control;

    public override void _Ready()
    {
        _cover = GetNode<Button>("%Cover");
        _armed = GetNode<Control>("%Armed");
        _switch = GetNode<CheckButton>("%Switch");
        _close = GetNode<Button>("%Close");
        StatusIconView.UseIcon(_close, UiIcons.ActionClose);
        _awaiting = GetNode<Label>("%Awaiting");
        _cover.Pressed += () => _control?.OpenGuard();
        _close.Pressed += () => _control?.CloseGuard();
        _switch.Pressed += OnSwitchPressed;
    }

    public override void _ExitTree() => Unbind();

    public void Bind(RemoteControlViewModel control)
    {
        ArgumentNullException.ThrowIfNull(control);
        Unbind();
        _control = control;
        _bindings.Observe(control.Guard, guard =>
        {
            _cover.Visible = guard is GuardState.Closed or GuardState.NotGuarded;
            _armed.Visible = guard is GuardState.Open or GuardState.AwaitingConfirmation;
            _close.Visible = guard == GuardState.Open;
            _awaiting.Visible = guard == GuardState.AwaitingConfirmation;
        });
        _bindings.Observe(control.IsEnabled, enabled => _cover.Disabled = !enabled);
        _bindings.Observe(control.CanOperate, canOperate => _switch.Disabled = !canOperate);
        _bindings.Observe(control.IsOn, on => _switch.SetPressedNoSignal(on == true));
        _bindings.Observe(control.StateWord, word => _switch.Text = word);
        _bindings.Observe(control.DisabledReason, reason => TooltipText = reason ?? string.Empty);
    }

    public void Unbind()
    {
        _bindings.Release();
        _control = null;
    }

    public void ApplyLabels(ConsoleLabels labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        _cover.Text = labels.GuardedCover;
        _cover.TooltipText = labels.GuardedOpen;
        _close.TooltipText = labels.GuardedClose;
        _awaiting.Text = labels.GuardedAwaiting;
    }

    private void OnSwitchPressed()
    {
        if (_control is null) return;
        // The position is the simulation's: undo the local flip, then ask for the change.
        _switch.SetPressedNoSignal(_control.IsOn.Value == true);
        _control.Toggle();
    }
}
