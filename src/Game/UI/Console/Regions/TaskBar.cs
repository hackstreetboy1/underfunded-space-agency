using Godot;
using Usa.Game.Presentation.Console.Tasks;
using Usa.Game.UI.Console.Binding;

namespace Usa.Game.UI.Console.Regions;

/// <summary>
/// The crew task slot bar (<c>TaskBar.tscn</c>, Figma G1-03 node 14:337; UI.md 3.7): assignee, task, progress, ETA in MET
/// and real time, complexity pips and Stop; "No active crew task" when empty; one flash when a second task is rejected.
/// </summary>
public sealed partial class TaskBar : PanelContainer
{
    private static readonly TimeSpan BusyFlashDuration = TimeSpan.FromSeconds(3);

    private readonly BindingScope _bindings = new();
    private Control _active = null!;
    private Label _empty = null!;
    private Label _assignee = null!;
    private Label _taskName = null!;
    private ProgressBar _progress = null!;
    private Label _progressText = null!;
    private Label _eta = null!;
    private HBoxContainer _pips = null!;
    private Button _stop = null!;
    private Label _busy = null!;
    private TaskSlotViewModel? _slot;
    private ulong _busyUntilMs;
    private Label _complexityCaption = null!;
    private bool _compact;

    public override void _Ready()
    {
        _active = GetNode<Control>("%Active");
        _empty = GetNode<Label>("%Empty");
        _assignee = GetNode<Label>("%Assignee");
        _taskName = GetNode<Label>("%TaskName");
        _progress = GetNode<ProgressBar>("%Progress");
        _progressText = GetNode<Label>("%ProgressText");
        _eta = GetNode<Label>("%Eta");
        _pips = GetNode<HBoxContainer>("%Pips");
        _stop = GetNode<Button>("%Stop");
        _busy = GetNode<Label>("%Busy");
        _complexityCaption = GetNode<Label>("%ComplexityCaption");
        _stop.Pressed += () => _slot?.RequestStop();
        _busy.Visible = false;
        SetProcess(false);
    }

    public override void _ExitTree() => Unbind();

    // Presentation only: hides the busy flash once its time is up.
    public override void _Process(double delta)
    {
        if (Time.GetTicksMsec() < _busyUntilMs) return;
        _busy.Visible = false;
        SetProcess(false);
    }

    public void Bind(TaskSlotViewModel slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        Unbind();
        _slot = slot;
        _empty.Text = slot.EmptyText;
        _bindings.Observe(slot.HasTask, has =>
        {
            _active.Visible = has;
            _empty.Visible = !has;
        });
        _bindings.Observe(slot.AssigneeName, name => _assignee.Text = name);
        _bindings.Observe(slot.TaskName, name => _taskName.Text = name);
        _bindings.Observe(slot.Progress, progress => _progress.Value = progress * _progress.MaxValue);
        _bindings.Observe(slot.ProgressText, text => _progressText.Text = text);
        _bindings.Observe(slot.Eta, eta => _eta.Text = _compact ? ConsoleText.EtaCompact(eta) : ConsoleText.Eta(eta));
        _bindings.Observe(slot.Complexity, ShowPips);
        _bindings.Observe(slot.StopEnabled, enabled => _stop.Disabled = !enabled);
        _bindings.On(slot.BusyFlashed, flash =>
        {
            _busy.Text = flash.Message;
            _busy.Visible = true;
            _busyUntilMs = Time.GetTicksMsec() + (ulong)BusyFlashDuration.TotalMilliseconds;
            SetProcess(true);
        });
    }

    public void Unbind()
    {
        _bindings.Release();
        _slot = null;
    }

    public void ApplyLayout(ConsoleLayoutMetrics layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        CustomMinimumSize = new Vector2(0, layout.TaskBarPx);
        // Compact (Figma 21:807): no percentage or Complexity caption, and the ETA as MET clock time and real seconds only.
        _compact = layout.Mode == ConsoleLayoutMode.Compact;
        _complexityCaption.Visible = !_compact;
        _progressText.Visible = !_compact;
        if (_slot is { } slot) _eta.Text = _compact ? ConsoleText.EtaCompact(slot.Eta.Value) : ConsoleText.Eta(slot.Eta.Value);
    }

    public void ApplyLabels(ConsoleLabels labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        _complexityCaption.Text = labels.TaskComplexity;
        _stop.Text = labels.TaskStop;
    }

    // The scene holds TaskSlotViewModel.MaxComplexity pip panels.
    private void ShowPips(ComplexityPips pips)
    {
        var count = _pips.GetChildCount();
        for (var i = 0; i < count; i++)
        {
            if (_pips.GetChild(i) is not Control pip) continue;
            pip.Visible = i < pips.Total;
            pip.SetVariation(ThemeVariations.Pip(i < pips.Filled));
        }
    }
}
