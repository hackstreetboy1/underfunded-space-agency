using Godot;
using Usa.Game.Content;
using Usa.Game.Presentation.Map;
using Usa.Game.Presentation.Map.Planning;
using Usa.Game.UI.Console;
using Usa.Game.UI.Entry;
using Usa.Game.UI.Map.Geometry;
using Usa.Simulation.Flight;
using Usa.Simulation.Guidance;
using Usa.Simulation.Kernel;

namespace Usa.Game.UI.Map;

/// <summary>
/// The burn planner in the orbit view (<c>BurnPlanner.tscn</c>, 300 wide; Figma G4-02 node 30:3133, variants Normal ·
/// Retro, Normal · OAMS drift warning, Easy · Proposal): docked right at 1080p, a drawer in the compact view
/// (<see cref="OrbitMapLayout"/>). Its body scrolls between the fixed header and the Execute row, so Execute is never
/// clipped (R3-04). A thin view over <see cref="BurnPlannerModel"/>: every edit goes to the model and the panel redraws on
/// <see cref="BurnPlannerModel.Changed"/>. Execute only hands the plan to the host (<see cref="ExecuteRequested"/>), which
/// opens the high-impact confirmation and orders the crew. Words come from the <c>planner.*</c> strings.
/// </summary>
public sealed partial class BurnPlannerPanel : PanelContainer
{
    private const string Content = "Column/Scroll/Body/Content/";
    private const string Preview = Content + "Preview/Pad/Rows/";

    private static readonly BurnDirection[] ChipDirections =
        [BurnDirection.Prograde, BurnDirection.Retrograde, BurnDirection.RadialIn, BurnDirection.RadialOut];

    private static readonly string[] ChipNameKeys =
        ["planner.directionPrograde", "planner.directionRetrograde", "planner.directionRadialIn", "planner.directionRadialOut"];

    private static readonly TextSlot[] Slots =
    [
        new("Column/Header/Row/Title", "planner.title"),
        new("%CloseButton", "planner.close"),
        new("%CloseButton", "planner.closeTooltip", Tooltip: true),
        new(Content + "ProposeBlock/ProposeRow/Title", "planner.proposeTitle"),
        new("%ProposeAside", "planner.proposeAside"),
        new("%ProposeButton", "planner.propose"),
        new(Content + "EngineTitle", "planner.engineTitle"),
        new(Content + "DirectionRow/Title", "planner.directionTitle"),
        new("%CustomChip", "planner.directionCustomChip"),
        new("%AngleSpin", "planner.angleTooltip", Tooltip: true),
        new(Content + "DurationRow/Title", "planner.durationTitle"),
        new(Content + "StartRow/Title", "planner.startTitle"),
        new("%AtNodeChip", "planner.startAtNode"),
        new("%AtNodeChip", "planner.startAtNodeTooltip", Tooltip: true),
        new("%ZonePassChip", "planner.startZonePass"),
        new("%ZonePassChip", "planner.startZonePassTooltip", Tooltip: true),
        new("%MetField", "planner.startMetTooltip", Tooltip: true),
        new("%StartHint", "planner.startHint"),
        new(Preview + "Head/Title", "planner.previewTitle"),
        new(Preview + "DeltaVRow/Title", "planner.previewDeltaV"),
        new("%PropellantRow/Title", "planner.previewPropellant"),
        new(Preview + "ApsidesRow/Title", "planner.previewApsides"),
        new(Preview + "LandingRow/Title", "planner.previewLanding"),
        new(Preview + "RecoveryRow/Title", "planner.previewRecoverySplash"),
        new(Preview + "EntryRow/Title", "planner.previewEntry"),
        new(Content + "Note", "planner.executeNote"),
        new("%ExecuteButton", "planner.execute"),
        new("%ClearButton", "planner.clear"),
    ];

    private readonly VariationSetter _variations = new();

    private Label _modeAside = null!;
    private Button _closeButton = null!;
    private ScrollContainer _scroll = null!;
    private Control _proposeBlock = null!;
    private OptionButton _goalSelect = null!;
    private Button _proposeButton = null!;
    private Label _proposalNote = null!;
    private OptionButton _engineSelect = null!;
    private Label _directionAside = null!;
    private Button[] _directionChips = [];
    private Button _customChip = null!;
    private SpinBox _angleSpin = null!;
    private Label _durationAside = null!;
    private HSlider _durationSlider = null!;
    private SpinBox _durationSpin = null!;
    private Label _fixedDetail = null!;
    private Label _startAside = null!;
    private Button _atNodeChip = null!;
    private Button _zonePassChip = null!;
    private PopupMenu _zonePassMenu = null!;
    private LineEdit _metField = null!;
    private Label _startHint = null!;
    private Label _massRow = null!;
    private Label _previewAside = null!;
    private Label _deltaV = null!;
    private Control _propellantRow = null!;
    private Label _propellant = null!;
    private PanelContainer _propellantBadge = null!;
    private Label _apsides = null!;
    private Label _landingZone = null!;
    private PanelContainer _landingBadge = null!;
    private Label _recovery = null!;
    private Label _entry = null!;
    private PanelContainer _corridorBadge = null!;
    private VBoxContainer _warnings = null!;
    private Button _executeButton = null!;
    private Button _clearButton = null!;
    private Control _heldRow = null!;
    private Label _heldOrder = null!;
    private MissionTime? _heldAt;

    private BurnPlannerModel? _planner;
    private ITutorialRails? _rails;
    private TutorialRailsStep? _railsStep;
    private MapText? _text;
    private ConsolePalette _palette = ConsolePalette.Default;
    private IReadOnlyList<PlannerGoalOption> _goals = [];
    private IReadOnlyList<RecoveryZoneArc> _zones = [];
    private MissionTime _now;
    private bool _refreshing;

    /// <summary>Execute with a valid, previewed plan: confirm with the player, then order the crew or send the command.</summary>
    public event Action<ManeuverPlan>? ExecuteRequested;

    /// <summary>The drawer's Close was pressed (compact view only).</summary>
    public event Action? CloseRequested;

    /// <summary>
    /// Clear was pressed: the draft is already cleared in the model; the host also drops a confirmed burn order it is
    /// holding for its mark (<c>MissionRuntimeContext.CancelBurnOrder()</c>), so clearing the plan never leaves a burn armed.
    /// </summary>
    public event Action? ClearRequested;

    public override void _Ready()
    {
        _modeAside = GetNode<Label>("%ModeAside");
        _closeButton = GetNode<Button>("%CloseButton");
        _scroll = GetNode<ScrollContainer>("%Scroll");
        _proposeBlock = GetNode<Control>("%ProposeBlock");
        _goalSelect = GetNode<OptionButton>("%GoalSelect");
        _proposeButton = GetNode<Button>("%ProposeButton");
        _proposalNote = GetNode<Label>("%ProposalNote");
        _engineSelect = GetNode<OptionButton>("%EngineSelect");
        _directionAside = GetNode<Label>("%DirectionAside");
        _directionChips =
        [
            GetNode<Button>("%ProgradeChip"), GetNode<Button>("%RetrogradeChip"),
            GetNode<Button>("%RadialInChip"), GetNode<Button>("%RadialOutChip"),
        ];
        _customChip = GetNode<Button>("%CustomChip");
        _angleSpin = GetNode<SpinBox>("%AngleSpin");
        _durationAside = GetNode<Label>("%DurationAside");
        _durationSlider = GetNode<HSlider>("%DurationSlider");
        _durationSpin = GetNode<SpinBox>("%DurationSpin");
        _fixedDetail = GetNode<Label>("%FixedDetail");
        _startAside = GetNode<Label>("%StartAside");
        _atNodeChip = GetNode<Button>("%AtNodeChip");
        _zonePassChip = GetNode<Button>("%ZonePassChip");
        _zonePassMenu = GetNode<PopupMenu>("%ZonePassMenu");
        _metField = GetNode<LineEdit>("%MetField");
        _startHint = GetNode<Label>("%StartHint");
        _massRow = GetNode<Label>("%MassRow");
        _previewAside = GetNode<Label>("%PreviewAside");
        _deltaV = GetNode<Label>("%DeltaV");
        _propellantRow = GetNode<Control>("%PropellantRow");
        _propellant = GetNode<Label>("%Propellant");
        _propellantBadge = GetNode<PanelContainer>("%PropellantBadge");
        _apsides = GetNode<Label>("%Apsides");
        _landingZone = GetNode<Label>("%LandingZone");
        _landingBadge = GetNode<PanelContainer>("%LandingBadge");
        _recovery = GetNode<Label>("%Recovery");
        _entry = GetNode<Label>("%Entry");
        _corridorBadge = GetNode<PanelContainer>("%CorridorBadge");
        _warnings = GetNode<VBoxContainer>("%Warnings");
        _executeButton = GetNode<Button>("%ExecuteButton");
        _clearButton = GetNode<Button>("%ClearButton");
        _heldRow = GetNode<Control>("%HeldRow");
        _heldOrder = GetNode<Label>("%HeldOrder");

        for (var i = 0; i < _directionChips.Length; i++)
        {
            var direction = ChipDirections[i];
            _directionChips[i].Pressed += () => _planner?.SetDirection(direction);
        }
        _closeButton.Pressed += () => CloseRequested?.Invoke();
        _customChip.Pressed += () => _planner?.SetCustomAngle(_angleSpin.Value);
        _angleSpin.ValueChanged += value => _planner?.SetCustomAngle(value);
        _engineSelect.ItemSelected += OnEngineSelected;
        _durationSlider.ValueChanged += value => _planner?.SetDuration(value);
        _durationSpin.ValueChanged += value => _planner?.SetDuration(value);
        _atNodeChip.Pressed += () => _planner?.SetStartTime(_planner.Draft.StartTime);
        _zonePassChip.Pressed += OpenZonePasses;
        _zonePassMenu.IdPressed += OnZonePassPicked;
        _metField.TextSubmitted += OnMetSubmitted;
        _metField.FocusExited += () => OnMetSubmitted(_metField.Text);
        _proposeButton.Pressed += OnProposePressed;
        _executeButton.Pressed += OnExecutePressed;
        _clearButton.Pressed += OnClearPressed;
        if (_text is not null) ApplyText(_text.Source);
        Refresh();
    }

    public override void _ExitTree() => Unbind();

    /// <param name="text">The <c>map.*</c> / <c>planner.*</c> strings (labels, corridor words, relative times).</param>
    public void Bind(BurnPlannerModel planner, ITextSource text)
    {
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(text);
        Unbind();
        _planner = planner;
        _text = new MapText(text);
        _planner.Changed += OnPlannerChanged;
        if (IsNodeReady()) ApplyText(text);
        Refresh();
    }

    public void Unbind()
    {
        if (_planner is not null) _planner.Changed -= OnPlannerChanged;
        _planner = null;
    }

    /// <summary>
    /// The confirmed burn order the host holds until its mark (<c>BurnOrder.OrderAt</c>); null when none is held. Shown above
    /// Execute as "held until MET …" so the player knows Clear cancels it.
    /// </summary>
    public void ShowHeldOrder(MissionTime? orderAt)
    {
        _heldAt = orderAt;
        RefreshHeldOrder();
    }

    /// <summary>Drawer mode (compact view): the header shows Close.</summary>
    public void SetDrawerMode(bool drawer)
    {
        if (IsNodeReady()) _closeButton.Visible = drawer;
    }

    public void SetPalette(ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        Refresh();
    }

    /// <summary>The Easy goal picker's entries; the orbit view fills zone targets from the map, the host may add others.</summary>
    public void SetGoals(IReadOnlyList<PlannerGoalOption> goals)
    {
        ArgumentNullException.ThrowIfNull(goals);
        if (goals.SequenceEqual(_goals)) return;
        _goals = goals;
        if (!IsNodeReady()) return;
        var selected = _goalSelect.Selected;
        _goalSelect.Clear();
        foreach (var goal in goals)
        {
            _goalSelect.AddItem(goal.Aside.Length == 0 || _text is null
                ? goal.Label
                : _text.Format("planner.goalOption", ("goal", goal.Label), ("aside", goal.Aside)));
        }
        if (goals.Count > 0) _goalSelect.Select(Math.Clamp(selected, 0, goals.Count - 1));
        Refresh();
    }

    /// <summary>The clock and the zones' next passes, from the map frame (for <c>in 31 min</c> and the Zone pass menu).</summary>
    public void SetClock(MissionTime now, IReadOnlyList<RecoveryZoneArc> zones)
    {
        ArgumentNullException.ThrowIfNull(zones);
        _zones = zones;
        if (now == _now) return;
        _now = now;
        RefreshStart();
    }

    /// <summary>
    /// The ground ledger's mass line (X8: <c>≈ 2330–2350 kg EST · ground ledger</c>, from <c>OrbitMapInputAdapter.MassText</c>);
    /// null hides the row.
    /// </summary>
    public void SetMassText(string? text)
    {
        if (IsNodeReady()) _massRow.ShowText(text);
    }

    private void OnPlannerChanged(object? sender, EventArgs e) => Refresh();

    /// <summary>
    /// The tutorial rails (<see cref="ITutorialRails"/>) for the guided first run, called from the console pump: during the planner
    /// steps the engine and goal pickers allow only the step's choice (Retro sequence, the recommended zone), Propose and Execute
    /// open with their steps and Execute waits for a proposal; the manual fields and Clear stay locked. Null lifts the lock.
    /// </summary>
    public void ApplyRails(ITutorialRails? rails)
    {
        var engaged = rails is { IsEngaged: true } ? rails : null;
        if (engaged is null && _rails is null) return;
        if (engaged is not null && ReferenceEquals(engaged, _rails) && engaged.Step == _railsStep) return;
        var lifted = engaged is null;
        _rails = engaged;
        _railsStep = engaged?.Step;
        if (!IsNodeReady()) return;
        if (lifted)
        {
            foreach (var control in new BaseButton[] { _engineSelect, _goalSelect, _atNodeChip, _zonePassChip, _clearButton }) control.Disabled = false;
            _metField.Editable = true;
            for (var i = 0; i < _goalSelect.ItemCount; i++) _goalSelect.SetItemDisabled(i, false);
        }
        Refresh();
    }

    /// <summary>The goal the Easy picker shows selected; null when none.</summary>
    public BurnGoal? SelectedGoal => IsNodeReady() && _goalSelect.Selected >= 0 && _goalSelect.Selected < _goals.Count ? _goals[_goalSelect.Selected].Goal : null;

    private void ApplyGate()
    {
        if (_rails is not { } rails || _planner is not { } planner) return;
        var tooltip = rails.BlockedTooltip;
        for (var i = 0; i < planner.Engines.Count && i < _engineSelect.ItemCount; i++)
        {
            if (rails.IsAllowed(TutorialControls.Item(TutorialControls.PlannerEngine, planner.Engines[i].Id))) continue;
            _engineSelect.SetItemDisabled(i, true);
            _engineSelect.SetItemTooltip(i, tooltip);
        }
        for (var i = 0; i < _goals.Count && i < _goalSelect.ItemCount; i++)
        {
            var zone = _goals[i].Goal is TargetZone target ? target.ZoneId : _goals[i].Label;
            var allowed = rails.IsAllowed(TutorialControls.Item(TutorialControls.PlannerGoal, zone));
            _goalSelect.SetItemDisabled(i, !allowed);
            _goalSelect.SetItemTooltip(i, allowed ? string.Empty : tooltip);
        }
        _engineSelect.Disabled = !rails.IsAllowed(TutorialControls.PlannerEngine);
        _goalSelect.Disabled = !rails.IsAllowed(TutorialControls.PlannerGoal);
        _proposeButton.Disabled |= !rails.IsAllowed(TutorialControls.PlannerPropose);
        _executeButton.Disabled |= !rails.IsAllowed(TutorialControls.PlannerExecute) || planner.LastProposal is not { Applied: true };
        var edit = rails.IsAllowed(TutorialControls.PlannerEdit);
        foreach (var chip in _directionChips) chip.Disabled |= !edit;
        _customChip.Disabled |= !edit;
        _angleSpin.Editable &= edit;
        _durationSlider.Editable &= edit;
        _durationSpin.Editable &= edit;
        _atNodeChip.Disabled = !edit;
        _zonePassChip.Disabled = !edit;
        _metField.Editable = edit;
        _clearButton.Disabled = !rails.IsAllowed(TutorialControls.PlannerClear);
    }

    private void OnClearPressed()
    {
        _planner?.Clear();
        ClearRequested?.Invoke();
    }

    private void RefreshHeldOrder()
    {
        if (!IsNodeReady()) return;
        var held = _heldAt is { } at && _text is { } words ? words.Format("planner.heldOrder", ("met", words.Met(at))) : null;
        _heldRow.Visible = held is not null;
        _heldOrder.Text = held ?? string.Empty;
        _heldOrder.SetFontColour(_palette.Of("caution"));
    }

    private void ApplyText(ITextSource text)
    {
        SceneText.Apply(this, text, Slots);
        for (var i = 0; i < _directionChips.Length; i++) _directionChips[i].TooltipText = text.Get(ChipNameKeys[i]);
    }

    private void Refresh()
    {
        if (!IsNodeReady()) return;
        _refreshing = true;
        try
        {
            // The orbit view shows and hides the panel (docked, drawer, or no planner after splashdown).
            if (_planner is not { } planner || _text is not { } words) return;
            var easy = planner.Settings.ProposalsEnabled;
            _modeAside.Text = words.Get(easy ? "planner.modeEasy" : "planner.modeNormal");
            RefreshProposal(planner, easy);
            RefreshEngines(planner, words);
            RefreshDirection(planner);
            RefreshDuration(planner, words);
            RefreshStart();
            RefreshPreview(planner, easy, words);
            RefreshMessages(planner);
            RefreshHeldOrder();
            _executeButton.Disabled = !planner.CanExecute;
            ApplyGate();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RefreshProposal(BurnPlannerModel planner, bool easy)
    {
        _proposeBlock.Visible = easy;
        if (!easy) return;
        _proposeButton.Disabled = !planner.CanPropose || planner.ProposalPending || _goals.Count == 0;
        var note = planner.ProposalPending
            ? _text?.Get("planner.proposalComputing")
            : planner.LastProposal?.Explanation;
        _proposalNote.GetParent<Control>().Visible = !string.IsNullOrEmpty(note);
        _proposalNote.Text = note ?? string.Empty;
    }

    private void RefreshEngines(BurnPlannerModel planner, MapText words)
    {
        var engines = planner.Engines;
        if (_engineSelect.ItemCount != engines.Count) _engineSelect.Clear();
        for (var i = 0; i < engines.Count; i++)
        {
            var engine = engines[i];
            var label = engine.Available || engine.UnavailableReason is null
                ? engine.Label
                : words.Format("planner.engineOption", ("engine", engine.Label), ("reason", engine.UnavailableReason));
            if (i >= _engineSelect.ItemCount) _engineSelect.AddItem(label, i);
            else _engineSelect.SetItemText(i, label);
            _engineSelect.SetItemDisabled(i, !engine.Available);
            _engineSelect.SetItemTooltip(i, engine.UnavailableReason ?? string.Empty);
        }
        var selected = -1;
        for (var i = 0; i < engines.Count; i++)
        {
            if (engines[i].Id == planner.Draft.EngineId) selected = i;
        }
        _engineSelect.Select(selected);
        // Select(-1) blanks the button, which reads as an empty list; the items are there, so prompt for a pick.
        if (selected < 0 && engines.Count > 0) _engineSelect.Text = words.Get("planner.selectEngine");
    }

    private void RefreshDirection(BurnPlannerModel planner)
    {
        var draft = planner.Draft;
        var locked = planner.DirectionLocked;
        for (var i = 0; i < _directionChips.Length; i++)
        {
            var chip = _directionChips[i];
            var selected = draft.Direction == ChipDirections[i];
            chip.SetPressedNoSignal(selected);
            chip.Disabled = locked && !selected;
            chip.GetNode<DirectionGlyph>("Glyph").SetColour(_palette.Of(chip.Disabled ? "text.off" : selected ? "accent" : "text.mid"));
        }
        var custom = draft.Direction == BurnDirection.Custom;
        _customChip.SetPressedNoSignal(custom);
        _customChip.Disabled = locked;
        _angleSpin.Editable = !locked;
        _angleSpin.SetValueNoSignal(draft.CustomAngleDeg);
        _directionAside.Text = planner.DirectionLabel;
        _directionAside.TooltipText = planner.HoldMeta ?? string.Empty;
    }

    private void RefreshDuration(BurnPlannerModel planner, MapText words)
    {
        var settings = planner.Settings;
        var engine = planner.SelectedEngine;
        var fixedS = engine?.Engine?.FixedDurationS;
        _durationSlider.MaxValue = settings.MaxDurationS;
        _durationSlider.Step = settings.DurationStepS;
        _durationSpin.MaxValue = settings.MaxDurationS;
        _durationSpin.Step = settings.DurationStepS;
        var value = fixedS ?? planner.Draft.DurationS;
        _durationSlider.SetValueNoSignal(Math.Min(value, settings.MaxDurationS));
        _durationSpin.SetValueNoSignal(value);
        var isFixed = fixedS is not null;
        _durationSlider.Editable = !isFixed;
        _durationSpin.Editable = !isFixed;
        _durationAside.Text = words.Get(isFixed ? "planner.durationFixedAside" : "planner.durationManual");
        _fixedDetail.ShowText(planner.FixedDurationDetail);
    }

    private void RefreshStart()
    {
        if (!IsNodeReady() || _planner is not { } planner) return;
        var draft = planner.Draft;
        var zonePass = draft.StartMode == BurnStartMode.ZonePass;
        _atNodeChip.SetPressedNoSignal(!zonePass);
        _zonePassChip.SetPressedNoSignal(zonePass);
        var start = draft.StartTime is { } requested && requested > _now ? requested : _now;
        if (!_metField.HasFocus()) _metField.Text = FlightFormat.Met(start);
        _startAside.Text = _text?.Relative(_now, start) ?? string.Empty;
        _startHint.Visible = !planner.Settings.ProposalsEnabled && planner.SelectedEngine?.HasFixedDuration == true;
    }

    private void RefreshPreview(BurnPlannerModel planner, bool easy, MapText words)
    {
        _previewAside.Text = words.Get(planner.PreviewPending || planner.ProposalPending ? "planner.previewUpdating" : "planner.previewLive");
        _propellantRow.Visible = !easy;
        if (planner.PreviewText is not { } text)
        {
            foreach (var label in new[] { _deltaV, _propellant, _apsides, _landingZone, _recovery, _entry }) label.Text = FlightFormat.Placeholder;
            ShowBadge(_propellantBadge, string.Empty, null);
            ShowBadge(_landingBadge, string.Empty, null);
            ShowBadge(_corridorBadge, string.Empty, null);
            return;
        }
        _deltaV.Text = text.DeltaV;
        _propellant.Text = words.Format("planner.previewPropellantLine", ("used", text.PropellantUsed), ("remaining", text.RemainingDeltaV));
        _propellant.TooltipText = text.Propellant.Meta ?? string.Empty;
        ShowBadge(_propellantBadge, text.Propellant.Badge, text.Propellant.State switch
        {
            PlannerKnowledgeState.Estimated => "info",
            PlannerKnowledgeState.Stale => "caution",
            _ => "unknown",
        });
        _apsides.Text = $"{text.Periapsis} · {text.Apoapsis}";
        _landingZone.Text = text.LandingBand.Length > 0
            ? words.Format("map.landingZoneBand", ("zone", text.LandingZone), ("band", text.LandingBand))
            : text.LandingZone;
        var judged = text.Verdict is LandingZoneVerdict.InZone or LandingZoneVerdict.NearEdge;
        ShowBadge(_landingBadge, judged ? text.VerdictWord : string.Empty, CorridorText.ZoneVerdictToken(text.Verdict));
        _recovery.Text = $"{text.RecoveryWait} · {text.LandingTime}";
        _entry.Text = $"{text.EntryAngle} · {text.PeakGLoad}";
        ShowBadge(_corridorBadge, words.CorridorWord(text.Corridor), CorridorText.ColorToken(text.Corridor));
    }

    private void RefreshMessages(BurnPlannerModel planner)
    {
        var lines = new List<PlannerMessage>();
        foreach (var issue in planner.Issues)
        {
            if (issue.Severity != PlannerSeverity.Info) lines.Add(issue);
        }
        lines.AddRange(planner.Warnings);
        while (_warnings.GetChildCount() > lines.Count) _warnings.GetChild(_warnings.GetChildCount() - 1).Free();
        while (_warnings.GetChildCount() < lines.Count) _warnings.AddChild(WarningRow());
        for (var i = 0; i < lines.Count; i++)
        {
            var row = _warnings.GetChild(i);
            var colour = _palette.Of(lines[i].ColorToken);
            var shape = lines[i].Severity == PlannerSeverity.Critical ? HudShape.Failed : HudShape.Caution;
            HudIcons.Show(row.GetChild<TextureRect>(0), shape, colour);
            var label = row.GetChild<Label>(1);
            label.Text = lines[i].Text;
            label.SetFontColour(colour);
        }
    }

    /// <summary>A warning line: the status shape as an icon (R3-11), then the words.</summary>
    private static HBoxContainer WarningRow()
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 6);
        var icon = HudIcons.Create();
        icon.CustomMinimumSize = new Vector2(14, 18);
        row.AddChild(icon);
        row.AddChild(new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            ThemeTypeVariation = ThemeVariationNames.Small,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(120, 0),
        });
        return row;
    }

    private void ShowBadge(PanelContainer badge, string word, string? token)
    {
        badge.Visible = word.Length > 0;
        var label = badge.GetNode<Label>("Text");
        _variations.SetBadge(badge, label, token);
        label.Text = word;
    }

    private void OnEngineSelected(long index)
    {
        if (_refreshing || _planner is not { } planner || index < 0 || index >= planner.Engines.Count) return;
        if (_rails is { } rails && !rails.IsAllowed(TutorialControls.Item(TutorialControls.PlannerEngine, planner.Engines[(int)index].Id)))
        {
            Refresh();
            return;
        }
        planner.SelectEngine(planner.Engines[(int)index].Id);
    }

    private void OpenZonePasses()
    {
        _zonePassMenu.Clear();
        for (var i = 0; i < _zones.Count; i++)
        {
            var zone = _zones[i];
            var item = _text?.Format("planner.zonePassOption", ("zone", zone.Label), ("eta", zone.EtaText)) ?? zone.Label;
            _zonePassMenu.AddItem(item, i);
            _zonePassMenu.SetItemDisabled(i, zone.NextPass is null);
        }
        var rect = _zonePassChip.GetGlobalRect();
        _zonePassMenu.Popup(new Rect2I((Vector2I)(rect.Position + new Vector2(0, rect.Size.Y)), Vector2I.Zero));
        RefreshStart();
    }

    private void OnZonePassPicked(long id)
    {
        if (_planner is null || id < 0 || id >= _zones.Count || _zones[(int)id].NextPass is not { } pass) return;
        _planner.SetStartAtZonePass(_zones[(int)id].ZoneId, pass);
    }

    private void OnMetSubmitted(string text)
    {
        if (_refreshing || _planner is not { } planner) return;
        if (MetInput.TryParse(text, out var time) && time != planner.Draft.StartTime) planner.SetStartTime(time);
        _metField.ReleaseFocus();
        RefreshStart();
    }

    private void OnProposePressed()
    {
        if (_rails is { } rails && !rails.IsAllowed(TutorialControls.PlannerPropose)) return;
        var index = _goalSelect.Selected;
        if (_planner is null || index < 0 || index >= _goals.Count) return;
        _planner.ProposeBurn(_goals[index].Goal);
    }

    private void OnExecutePressed()
    {
        if (_rails is { } rails && (!rails.IsAllowed(TutorialControls.PlannerExecute) || _planner?.LastProposal is not { Applied: true })) return;
        if (_planner is not { CanExecute: true } planner || planner.BuildPlan() is not { } plan) return;
        ExecuteRequested?.Invoke(plan);
    }
}
