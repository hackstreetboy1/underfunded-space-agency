using System.Globalization;
using Godot;
using Usa.Game.Input;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.UI.Console.Binding;
using Usa.Game.UI.Console.Components;

namespace Usa.Game.UI.Console.Overlays;

/// <summary>
/// The high-impact confirmation (<c>ConfirmDialog.tscn</c>, Figma G1-02 "Confirm dialog", G2-09 node 32:1921; UI.md 5.2):
/// 520 wide over a 40 % scrim that leaves the header and MET clear, title, IRREVERSIBLE or UNSAFE TO STOP flag, effects,
/// who executes, the 20 s countdown text and bar, Cancel (<c>Esc</c>) and Confirm (<c>Enter</c>, focused). While the game
/// is frozen Confirm is disabled and the countdown holds.
/// </summary>
public sealed partial class ConfirmDialog : Control
{
    private const double CautionSeconds = 6;

    private readonly BindingScope _bindings = new();
    private Label _title = null!;
    private BadgeView _irreversible = null!;
    private BadgeView _unsafe = null!;
    private Control _effectsBox = null!;
    private VBoxContainer _effects = null!;
    private Label _executor = null!;
    private Label _timer = null!;
    private ProgressBar _countdown = null!;
    private Button _cancel = null!;
    private Button _confirm = null!;
    private ConfirmationCoordinator? _coordinator;
    private ConsolePalette _palette = ConsolePalette.Default;
    private bool _canConfirm = true;
    private double _secondsLeft = ConfirmationCoordinator.AutoCancelSeconds;
    private Button? _focusBeforeTalk;
    private ConsoleLabels? _labels;

    [Export]
    public string IrreversibleText { get; set; } = string.Empty;

    [Export]
    public string UnsafeToStopText { get; set; } = string.Empty;

    /// <summary>Countdown line; <c>{0}</c> is the whole seconds left.</summary>
    [Export]
    public string TimerFormat { get; set; } = "{0}";

    /// <summary>Shown instead of the countdown while the game is frozen.</summary>
    [Export]
    public string FrozenText { get; set; } = string.Empty;

    /// <summary>Executor line; <c>{0}</c> is who carries it out.</summary>
    [Export]
    public string ExecutorFormat { get; set; } = "{0}";

    public override void _Ready()
    {
        _title = GetNode<Label>("%Title");
        _irreversible = GetNode<BadgeView>("%Irreversible");
        _unsafe = GetNode<BadgeView>("%Unsafe");
        _effectsBox = GetNode<Control>("%EffectsBox");
        _effects = GetNode<VBoxContainer>("%Effects");
        _executor = GetNode<Label>("%Executor");
        _timer = GetNode<Label>("%Timer");
        _countdown = GetNode<ProgressBar>("%Countdown");
        _cancel = GetNode<Button>("%Cancel");
        _confirm = GetNode<Button>("%Confirm");
        _cancel.Pressed += () => _coordinator?.Cancel();
        _confirm.Pressed += () => _coordinator?.Confirm();
        _countdown.MaxValue = ConfirmationCoordinator.AutoCancelSeconds;
        Visible = false;
    }

    public override void _ExitTree() => Unbind();

    // Keys go to the dialog first while it is up: Esc closes only the dialog, never the Esc menu (UI.md 5.2); Enter always
    // confirms and Esc always cancels, whatever has focus; push-to-talk passes through to the microphone and never confirms (ConfirmDialogKeys).
    public override void _Input(InputEvent @event)
    {
        if (!Visible || _coordinator is null || InputBlocked?.Invoke() == true) return;
        if (InputMap.HasAction(InputBindings.PushToTalk) && @event.IsAction(InputBindings.PushToTalk) && !@event.IsEcho())
        {
            OnPushToTalk(@event.IsPressed());
            return;
        }
        var key = KeyOf(@event);
        if (key == DialogKey.Other)
        {
            if (@event is InputEventKey or InputEventMouseButton) _coordinator.Touch();
            return;
        }
        switch (ConfirmDialogKeys.Decide(key, FocusOf(GetViewport().GuiGetFocusOwner()), _canConfirm))
        {
            case DialogResponse.Confirm:
                _coordinator.Confirm();
                break;
            case DialogResponse.Cancel:
                _coordinator.Cancel();
                break;
            case DialogResponse.FocusCancel:
                _cancel.GrabFocus();
                _coordinator.Touch();
                break;
            case DialogResponse.FocusConfirm:
                _confirm.GrabFocus();
                _coordinator.Touch();
                break;
            case DialogResponse.Swallow:
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    /// <summary>
    /// True while something covers the dialog (the Esc menu or the outage overlay): it then takes no keys at all, so Esc never
    /// cancels it unseen (review R3-05).
    /// </summary>
    public Func<bool>? InputBlocked { get; set; }

    public void ApplyLabels(ConsoleLabels labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        _labels = labels;
        IrreversibleText = labels.ConfirmIrreversible;
        UnsafeToStopText = labels.ConfirmUnsafeToStop;
        FrozenText = labels.ConfirmFrozen;
        _cancel.Text = labels.ConfirmCancel;
        _confirm.Text = labels.ConfirmConfirm;
        ShowTimer();
    }

    public void Bind(ConfirmationCoordinator coordinator, ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(palette);
        Unbind();
        _coordinator = coordinator;
        _palette = palette;
        _bindings.Observe(coordinator.Prompt, ShowPrompt);
        _bindings.Observe(coordinator.CanConfirm, can =>
        {
            _canConfirm = can;
            _confirm.Disabled = !can;
            ShowTimer();
        });
        _bindings.Observe(coordinator.SecondsLeft, seconds =>
        {
            _secondsLeft = seconds;
            ShowTimer();
        });
    }

    public void Unbind()
    {
        _bindings.Release();
        _coordinator = null;
        Visible = false;
    }

    /// <summary>Keeps the scrim off the header so the MET clock stays readable (Gate 2 G2-09).</summary>
    public void ApplyLayout(ConsoleLayoutMetrics layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        OffsetTop = layout.HeaderPx;
    }

    private void ShowPrompt(ConfirmPrompt? prompt)
    {
        Visible = prompt is not null;
        if (prompt is null) return;
        _title.Text = prompt.Title;
        _irreversible.Display(prompt.Irreversible ? IrreversibleText : string.Empty, ColourTokens.Critical);
        _unsafe.Display(prompt.Kind == ConfirmPromptKind.StopTask ? UnsafeToStopText : string.Empty, ColourTokens.Caution);
        foreach (var child in _effects.GetChildren())
        {
            _effects.RemoveChild(child);
            child.QueueFree();
        }
        foreach (var effect in prompt.Effects) _effects.AddChild(new Label { Text = effect, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        _effectsBox.Visible = prompt.Effects.Count > 0;
        _executor.ShowText(prompt.ExecutorName is { } who ? Executor(who) : null);
        _focusBeforeTalk = null;
        _confirm.GrabFocus();
    }

    private void OnPushToTalk(bool pressed)
    {
        var focus = FocusOf(GetViewport().GuiGetFocusOwner());
        if (pressed && ConfirmDialogKeys.Decide(DialogKey.PushToTalk, focus, _canConfirm) == DialogResponse.ReleaseFocusForTalk)
        {
            _focusBeforeTalk = focus == DialogFocus.Cancel ? _cancel : _confirm;
            GetViewport().GuiReleaseFocus();
        }
        else if (!pressed && _focusBeforeTalk is { } button)
        {
            _focusBeforeTalk = null;
            button.GrabFocus();
        }
    }

    private DialogFocus FocusOf(Control? owner) =>
        owner == _cancel ? DialogFocus.Cancel : owner == _confirm ? DialogFocus.Confirm : DialogFocus.None;

    private static DialogKey KeyOf(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.KpEnter }) return DialogKey.Accept;
        if (input.IsActionPressed("ui_cancel")) return DialogKey.Cancel;
        if (input.IsActionPressed("ui_focus_prev")) return DialogKey.FocusPrevious;
        if (input.IsActionPressed("ui_focus_next")) return DialogKey.FocusNext;
        return DialogKey.Other;
    }

    private string Executor(string who) => _labels?.ConfirmExecutor(who) ?? string.Format(CultureInfo.InvariantCulture, ExecutorFormat, who);

    private void ShowTimer()
    {
        _countdown.Value = _secondsLeft;
        _timer.Text = _canConfirm
            ? _labels?.ConfirmTimer(_secondsLeft) ?? string.Format(CultureInfo.InvariantCulture, TimerFormat, _secondsLeft)
            : FrozenText;
        // The countdown text turns caution in its last seconds (Gate 2 G2-09 "6 s left").
        if (_canConfirm && _secondsLeft <= CautionSeconds) _timer.SetFontColour(_palette.Of(ColourTokens.Caution));
        else _timer.ClearFontColour();
    }
}
