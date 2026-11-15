using Godot;
using Usa.Game.Presentation.Console.Header;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.UI.Console.Binding;
using Usa.Game.UI.Console.Components;

namespace Usa.Game.UI.Console.Regions;

/// <summary>
/// The console header (<c>Header.tscn</c>, Figma G1-03 node 12:243; UI.md 3.1): agency mark, mission and vehicle, phase
/// chip, MET clock, time compression with its reason and Advance, objectives, caution count, link and the master alarm.
/// </summary>
public sealed partial class ConsoleHeader : MarginContainer
{
    private readonly BindingScope _bindings = new();
    private Label _agencyMark = null!;
    private Label _missionTitle = null!;
    private Label _vehicleName = null!;
    private BadgeView _phase = null!;
    private Label _metClock = null!;
    private Label _compressionRate = null!;
    private Label _compressionReason = null!;
    private Button _advance = null!;
    private HBoxContainer _objectiveList = null!;
    private Label _objectivesSummary = null!;
    private BadgeView _alarmCount = null!;
    private BadgeView _link = null!;
    private Button _masterAlarm = null!;
    private Button _menu = null!;
    private Label _objectivesCaption = null!;
    private ConsoleLabels? _labels;
    private bool _compact;
    private HeaderViewModel? _header;
    private MasterAlarmState _alarmState;
    private ulong _alarmStateSinceMs;

    [Export]
    public PackedScene? ObjectiveChipScene { get; set; }

    /// <summary>Holds a flashing master alarm steadily lit (reduced-motion setting).</summary>
    public bool ReducedMotion { get; set; }

    /// <summary>The menu button; the screen flow opens the Esc menu.</summary>
    public event Action? MenuRequested;

    public override void _Ready()
    {
        _agencyMark = GetNode<Label>("%AgencyMark");
        _missionTitle = GetNode<Label>("%MissionTitle");
        _vehicleName = GetNode<Label>("%VehicleName");
        _phase = GetNode<BadgeView>("%Phase");
        _metClock = GetNode<Label>("%MetClock");
        _compressionRate = GetNode<Label>("%CompressionRate");
        _compressionReason = GetNode<Label>("%CompressionReason");
        _advance = GetNode<Button>("%Advance");
        _objectiveList = GetNode<HBoxContainer>("%ObjectiveList");
        _objectivesSummary = GetNode<Label>("%ObjectivesSummary");
        _alarmCount = GetNode<BadgeView>("%AlarmCount");
        _link = GetNode<BadgeView>("%Link");
        _masterAlarm = GetNode<Button>("%MasterAlarm");
        _advance.Pressed += () => _header?.RequestAdvanceToNextEvent();
        _masterAlarm.Pressed += OnMasterAlarmPressed;
        _menu = GetNode<Button>("%Menu");
        _objectivesCaption = GetNode<Label>("%ObjectivesCaption");
        _menu.Pressed += () => MenuRequested?.Invoke();
        SetProcess(false);
    }

    public override void _ExitTree() => Unbind();

    // Presentation only: flashes the annunciator while an unacknowledged master alarm is up.
    public override void _Process(double delta) => ShowAnnunciator();

    public void Bind(HeaderViewModel header, StatusLanguage status, ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(palette);
        Unbind();
        _header = header;
        _agencyMark.Text = header.AgencyMark;
        _bindings.Observe(header.MissionTitle, title => _missionTitle.Text = title);
        _bindings.Observe(header.VehicleName, _vehicleName.ShowText);
        _bindings.Observe(header.Phase, phase => _phase.Display(phase.Word, phase.ColourToken));
        _bindings.Observe(header.MetText, met => _metClock.Text = met);
        _bindings.Observe(header.Compression, compression =>
        {
            _compressionRate.Text = compression.RateText;
            _compressionReason.ShowText(compression.ReasonText);
            _advance.Disabled = !compression.CanAdvance;
        });
        _bindings.Observe(header.ObjectivesSummary, _objectivesSummary.ShowText);
        _bindings.Observe(header.Link, _link.Display);
        _bindings.Observe(header.MasterAlarm, indicator =>
        {
            _alarmCount.Display(
                indicator.CautionCount > 0 ? indicator.CautionCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + status.Caution.Word : string.Empty,
                status.Caution.ColourToken, status.Caution.Shape);
            if (indicator.State != _alarmState) _alarmStateSinceMs = Time.GetTicksMsec();
            _alarmState = indicator.State;
            SetProcess(indicator.State == MasterAlarmState.Flashing && !ReducedMotion);
            ShowAnnunciator();
        });
        if (ObjectiveChipScene is { } chipScene)
        {
            _bindings.Mirror(header.Objectives, new ListBinder<ObjectiveChip, ObjectiveChipView>(_objectiveList, chipScene, (view, chip) => view.Show(chip, palette)));
        }
    }

    public void Unbind()
    {
        _bindings.Release();
        _header = null;
        _alarmState = MasterAlarmState.Off;
        SetProcess(false);
    }

    public void ApplyLabels(ConsoleLabels labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        _labels = labels;
        _objectivesCaption.Text = labels.HeaderObjectives;
        _menu.Text = labels.HeaderMenu;
        _masterAlarm.Text = labels.MasterAlarm + "\n" + labels.MasterAlarmKey;
        _masterAlarm.TooltipText = labels.AlarmAckTooltip;
        ShowAdvanceLabel();
    }

    /// <summary>Compact layout (Figma 21:807): the short Advance label and no Objectives caption.</summary>
    public void ApplyLayout(ConsoleLayoutMetrics layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _compact = layout.Mode == ConsoleLayoutMode.Compact;
        _objectivesCaption.Visible = !_compact;
        ShowAdvanceLabel();
    }

    private void ShowAdvanceLabel()
    {
        if (_labels is not { } labels) return;
        _advance.Text = (_compact ? labels.AdvanceShort : labels.AdvanceNextEvent) + "   " + labels.AdvanceKey;
        _advance.TooltipText = labels.AdvanceNextEvent;
    }

    /// <summary>The menu button is off while a confirmation is up, so the Esc menu never opens over a live prompt.</summary>
    public void SetMenuEnabled(bool enabled) => _menu.Disabled = !enabled;

    /// <summary>Master alarm click and <c>Ctrl+A</c>.</summary>
    public void AcknowledgeAlarms() => _header?.AcknowledgeAlarms();

    /// <summary>Advance button and <c>N</c>.</summary>
    public void RequestAdvance() => _header?.RequestAdvanceToNextEvent();

    private void OnMasterAlarmPressed()
    {
        AcknowledgeAlarms();
        ShowAnnunciator();
    }

    private void ShowAnnunciator()
    {
        var since = TimeSpan.FromMilliseconds(Time.GetTicksMsec() - _alarmStateSinceMs);
        var lit = AnnunciatorBlink.IsLit(_alarmState, since, ReducedMotion);
        _masterAlarm.SetVariation(_alarmState == MasterAlarmState.Off ? ThemeVariations.Default : ThemeVariations.ButtonDanger);
        // The pressed style is the danger fill: the annunciator is a toggle button only so it can show "lit".
        _masterAlarm.SetPressedNoSignal(lit);
    }
}
