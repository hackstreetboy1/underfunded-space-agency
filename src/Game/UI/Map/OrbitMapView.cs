using Godot;
using Usa.Game.Content;
using Usa.Game.Input;
using Usa.Game.Presentation.Map;
using Usa.Game.Presentation.Map.Planning;
using Usa.Game.UI.Console;
using Usa.Game.UI.Entry;
using Usa.Game.UI.Map.Geometry;
using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;

namespace Usa.Game.UI.Map;

/// <summary>
/// The Orbit main view (<c>res://ui/map/OrbitMapView.tscn</c>; Figma G4-01 node 32:2944): the map canvas with its chips,
/// controls and legend, and the <see cref="BurnPlannerPanel"/>. It lays itself out for whatever main-view region it is
/// given (<see cref="OrbitMapLayout"/>): at 1000 × 560 the planner docks right (300 wide), in the compact 600 × 406 view it
/// becomes a drawer opened from the map (R3-04). The host binds the models once and calls <see cref="Show"/> at the UI
/// cadence (10 Hz) with the adapter's input; zoom, pan and node drags rebuild the frame at once. Everything shown is the
/// ground's estimate (X8), never truth.
/// </summary>
public sealed partial class OrbitMapView : Control
{
    private static readonly TextSlot[] Slots =
    [
        new("%OrbitTitle", "map.chipOrbit"),
        new("%NavTitle", "map.chipNav"),
        new("%NodeTitle", "map.chipNode"),
        new("%AltitudeNote", "map.altitudeNote"),
        new("%ZoomInButton", "map.zoomIn", Tooltip: true),
        new("%ZoomOutButton", "map.zoomOut", Tooltip: true),
        new("%LabelsButton", "map.labels", Tooltip: true),
        new("%FocusButton", "map.focusVehicle"),
        new("%PlannerToggle", "planner.open"),
        new("%PlannerToggle", "planner.openTooltip", Tooltip: true),
        new("%Items/CurrentOrbit/Text", "map.legendCurrentOrbit"),
        new("%Items/Planned/Text", "map.legendPlanned"),
        new("%Items/NavBand/Text", "map.legendNavBand"),
        new("%Items/Corridor/Text", "map.legendCorridor"),
        new("%Items/Zone/Text", "map.legendZone"),
        new("%Items/Landing/Text", "map.legendLanding"),
        new("%Items/ZoneAtSplash/Text", "map.legendZoneAtSplash"),
        new("%Items/Station/Text", "map.legendStation"),
        new("%Items/Gestures", "map.legendGestures"),
    ];

    private readonly VariationSetter _variations = new();

    private OrbitMapCanvas _canvas = null!;
    private BurnPlannerPanel _plannerPanel = null!;
    private Usa.Simulation.Guidance.ITutorialRails? _rails;
    private Button _plannerToggle = null!;
    private Control _rightChips = null!;
    private Control _legend = null!;
    private Label _orbitText = null!;
    private PanelContainer _orbitBadge = null!;
    private Label _orbitBadgeText = null!;
    private Label _orbitMeta = null!;
    private Label _navText = null!;
    private Label _navMeta = null!;
    private Control _nodeChip = null!;
    private Label _nodeText = null!;
    private Control _nextFixChip = null!;
    private Label _nextFixText = null!;
    private Button _zoomInButton = null!;
    private Button _zoomOutButton = null!;
    private Button _labelsButton = null!;
    private Button _focusButton = null!;
    private LegendSwatch[] _swatches = [];

    private BurnPlannerModel? _planner;
    private MapText? _text;
    private OrbitMapInput? _input;
    private MapTrackingOverlay _tracking = MapTrackingOverlay.None;
    private IReadOnlyList<PlannerGoalOption> _extraGoals = [];
    private OrbitMapLayout? _layout;
    private bool _drawerOpen;
    private float _plannerMinWidth;

    /// <summary>The player pressed Execute on a valid, previewed plan.</summary>
    public event Action<ManeuverPlan>? ExecuteRequested;

    /// <summary>
    /// The player pressed the planner's Clear (the draft is already cleared). The host cancels any burn order it holds for
    /// its mark: <c>MissionRuntimeContext.CancelBurnOrder()</c>.
    /// </summary>
    public event Action? ClearRequested;

    /// <summary>The layout this view chose for its current size.</summary>
    public OrbitMapLayout? Layout => _layout;

    public override void _Ready()
    {
        _canvas = GetNode<OrbitMapCanvas>("%MapCanvas");
        _plannerPanel = GetNode<BurnPlannerPanel>("%BurnPlanner");
        _plannerToggle = GetNode<Button>("%PlannerToggle");
        _rightChips = GetNode<Control>("%RightChips");
        _legend = GetNode<Control>("%Legend");
        _orbitText = GetNode<Label>("%OrbitText");
        _orbitBadge = GetNode<PanelContainer>("%OrbitBadge");
        _orbitBadgeText = _orbitBadge.GetNode<Label>("Text");
        _orbitMeta = GetNode<Label>("%OrbitMeta");
        _navText = GetNode<Label>("%NavText");
        _navMeta = GetNode<Label>("%NavMeta");
        _nodeChip = GetNode<Control>("%NodeChip");
        _nodeText = GetNode<Label>("%NodeText");
        _nextFixChip = GetNode<Control>("%NextFixChip");
        _nextFixText = GetNode<Label>("%NextFixText");
        _zoomInButton = GetNode<Button>("%ZoomInButton");
        _zoomOutButton = GetNode<Button>("%ZoomOutButton");
        _labelsButton = GetNode<Button>("%LabelsButton");
        _focusButton = GetNode<Button>("%FocusButton");
        var swatches = new List<LegendSwatch>();
        foreach (var item in GetNode("%Items").GetChildren())
        {
            if (item.GetNodeOrNull<LegendSwatch>("Swatch") is { } swatch) swatches.Add(swatch);
        }
        _swatches = [.. swatches];

        _zoomInButton.Pressed += () => _canvas.ZoomBy(1.25);
        _zoomOutButton.Pressed += () => _canvas.ZoomBy(0.8);
        _labelsButton.Toggled += _canvas.SetLabelsVisible;
        _focusButton.Pressed += FocusVehicle;
        _plannerToggle.Toggled += SetDrawerOpen;
        _plannerPanel.CloseRequested += () => SetDrawerOpen(false);
        _canvas.FrameChanged += OnFrameChanged;
        _canvas.NodeTimeChanged += time =>
        {
            if (MapEditAllowed) _planner?.SetStartTime(time);
        };
        _canvas.DirectionPicked += direction =>
        {
            if (MapEditAllowed) _planner?.SetDirection(direction);
        };
        _canvas.CustomAngleChanged += degrees =>
        {
            if (MapEditAllowed) _planner?.SetCustomAngle(degrees);
        };
        _plannerPanel.ExecuteRequested += plan => ExecuteRequested?.Invoke(plan);
        _plannerPanel.ClearRequested += () => ClearRequested?.Invoke();
        if (_text is not null) ApplyText(_text.Source);
        ShowFocusKey();
        ApplyLayout();
        OnFrameChanged();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized && IsNodeReady()) ApplyLayout();
    }

    /// <param name="planner">Null hides the planner (e.g. after splashdown).</param>
    /// <param name="text">The <c>map.*</c> / <c>planner.*</c> strings, the same source the models were built with.</param>
    public void Bind(OrbitMapModel map, BurnPlannerModel? planner, ITextSource text)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(text);
        _text = new MapText(text);
        _planner = planner;
        _canvas.Bind(map, _text);
        _labelsButton.SetPressedNoSignal(map.LabelsVisible);
        if (planner is null) _plannerPanel.Unbind();
        else _plannerPanel.Bind(planner, text);
        if (IsNodeReady()) ApplyText(text);
        ApplyLayout();
    }

    /// <summary>One frame of the ground's flight picture (<c>OrbitMapInputAdapter.Build(plan, preview)</c>), at 10 Hz.</summary>
    public void Show(OrbitMapInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        _input = input;
        _canvas.Show(input);
        // The planner's minimum width can grow with its content; checked here (10 Hz) instead of through a signal handler.
        if (!Mathf.IsEqualApprox(_plannerPanel.GetCombinedMinimumSize().X, _plannerMinWidth))
        {
            _plannerMinWidth = _plannerPanel.GetCombinedMinimumSize().X;
            ApplyLayout();
        }
    }

    /// <summary>The along-track tick and STALE badge (X8); <see cref="MapTrackingOverlay.None"/> hides them.</summary>
    public void ShowTracking(MapTrackingOverlay tracking)
    {
        ArgumentNullException.ThrowIfNull(tracking);
        _tracking = tracking;
        _canvas.ShowTracking(tracking);
        OnFrameChanged();
    }

    /// <summary>The held burn order's mark (<c>BurnOrder.OrderAt</c>, from <c>PlanExecutor.Pending</c>); null when none is held.</summary>
    public void ShowHeldOrder(MissionTime? orderAt) => _plannerPanel.ShowHeldOrder(orderAt);

    /// <summary>The planner's ledger mass line (<c>OrbitMapInputAdapter.MassText</c>); null hides it.</summary>
    public void SetMassText(string? text) => _plannerPanel.SetMassText(text);

    /// <summary>The guided first run's tutorial rails: the planner locks to the step's choices and the node cannot be dragged.</summary>
    public void ApplyRails(Usa.Simulation.Guidance.ITutorialRails? rails)
    {
        _rails = rails is { IsEngaged: true } ? rails : null;
        _plannerPanel.ApplyRails(rails);
    }

    /// <summary>The goal the planner's Easy picker shows selected.</summary>
    public BurnGoal? PlannerSelectedGoal => _plannerPanel.SelectedGoal;

    private bool MapEditAllowed => _rails?.IsAllowed(Usa.Simulation.Guidance.TutorialControls.MapDrag) ?? true;

    /// <summary>Goals the Easy picker lists after the zone targets (e.g. <c>Lower Pe into corridor</c>, <c>Raise orbit by…</c>).</summary>
    public void SetExtraGoals(IReadOnlyList<PlannerGoalOption> goals)
    {
        ArgumentNullException.ThrowIfNull(goals);
        _extraGoals = goals;
        OnFrameChanged();
    }

    /// <summary>Default or High contrast token colours for the drawn map (the theme covers the controls).</summary>
    public void SetPalette(ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _canvas.SetPalette(palette);
        _plannerPanel.SetPalette(palette);
        foreach (var swatch in _swatches) swatch.SetPalette(palette);
    }

    /// <summary>"Focus vehicle" (the <c>focus_vehicle</c> action, <c>F</c> by default).</summary>
    public void FocusVehicle() => _canvas.FocusVehicle();

    /// <summary>Opens or closes the planner drawer (compact view); the docked planner is always open.</summary>
    public void SetDrawerOpen(bool open)
    {
        _drawerOpen = open;
        ApplyLayout();
    }

    private void ApplyText(ITextSource text)
    {
        SceneText.Apply(this, text, Slots);
        ShowFocusKey();
    }

    /// <summary>The focus key follows the player's binding (UI.md 6.1), not a fixed <c>F</c>.</summary>
    private void ShowFocusKey()
    {
        var key = FocusKeyName();
        if (key.Length == 0 || _text is null) return;
        _focusButton.Text = _text.Format("map.focusVehicleKey", ("key", key));
        _focusButton.TooltipText = _text.Format("map.focusTooltip", ("key", key));
    }

    private static string FocusKeyName()
    {
        if (!InputMap.HasAction(InputBindings.FocusVehicle)) return string.Empty;
        foreach (var binding in InputMap.ActionGetEvents(InputBindings.FocusVehicle))
        {
            if (binding is not InputEventKey key) continue;
            var code = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
            return OS.GetKeycodeString(code);
        }
        return string.Empty;
    }

    private void ApplyLayout()
    {
        if (!IsNodeReady()) return;
        var layout = OrbitMapLayout.For(Size.X, Size.Y);
        _layout = layout;
        var hasPlanner = _planner is not null;
        var drawer = layout.Placement == PlannerPlacement.Drawer;
        // The docked planner can need more than its nominal width (its content's minimum); the map ends where it starts, so
        // the map's own buttons (Focus vehicle, zoom) never sit under it.
        var plannerWidth = Math.Max(layout.PlannerWidthPx, _plannerPanel.GetCombinedMinimumSize().X);
        _canvas.OffsetRight = hasPlanner && !drawer ? -plannerWidth : 0;
        _plannerPanel.OffsetLeft = -plannerWidth;
        _plannerPanel.SetDrawerMode(drawer);
        _plannerPanel.Visible = hasPlanner && (!drawer || _drawerOpen);
        _plannerToggle.Visible = hasPlanner && drawer && !_drawerOpen;
        _plannerToggle.SetPressedNoSignal(_drawerOpen);
        var toggleRoom = _plannerToggle.Visible ? OrbitMapLayout.DrawerToggleHeightPx + 4 : 0;
        _rightChips.OffsetLeft = -OrbitMapLayout.GutterPx - layout.RightChipsWidthPx;
        _rightChips.OffsetTop = OrbitMapLayout.GutterPx + toggleRoom;
        _legend.OffsetRight = _legend.OffsetLeft + layout.LegendWidthPx;
    }

    private void OnFrameChanged()
    {
        if (!IsNodeReady()) return;
        var frame = _canvas.Frame;
        var input = _input;
        ShowOrbitChip(frame, input);

        _nodeChip.Visible = frame?.Node is not null;
        if (frame?.Node is { } node) _nodeText.Text = node.TimeLabel;

        var nextFix = frame?.NextFixText;
        _nextFixChip.Visible = nextFix is not null;
        _nextFixText.Text = nextFix ?? string.Empty;

        if (frame is null || input is null) return;
        _plannerPanel.SetClock(input.Now, frame.Zones);
        var goals = new List<PlannerGoalOption>(frame.Zones.Count + 2 + _extraGoals.Count);
        if (_text is not null)
        {
            // An entry that still needs an amount (Raise orbit by…) waits for a km field in the scene; the host may add one.
            foreach (var option in BurnGoalOptions.Build(_text, frame.Zones, frame.CorridorPeriapsisKm))
            {
                if (option.Goal is { } goal) goals.Add(new PlannerGoalOption(option.Label, option.Aside, goal));
            }
        }
        goals.AddRange(_extraGoals);
        _plannerPanel.SetGoals(goals);
    }

    /// <summary>The STALE badge with its age when the host knows it.</summary>
    private string StaleBadge() => _text is null ? string.Empty
        : _tracking.StaleAge.Length > 0 ? _text.Format("map.badgeStale", ("age", _tracking.StaleAge))
        : _text.Get("map.badgeStaleNoAge");

    /// <summary>The model's chip (apsides, period, fix meta and state); the host's stale overlay still applies without a fix.</summary>
    private void ShowOrbitChip(OrbitChip chip, OrbitMapInput? input)
    {
        _orbitText.Text = chip.Text;
        _orbitMeta.ShowText(chip.Meta);
        var stale = chip.State == OrbitFixState.Stale || (chip.State == OrbitFixState.None && _tracking.Stale);
        var badge = chip.Badge.Length > 0 ? chip.Badge : stale ? StaleBadge() : string.Empty;
        string? token = badge.Length == 0 ? null : stale ? "caution" : "info";
        ShowBadge(badge, token);
        ShowNav(input);
    }

    private void ShowBadge(string badge, string? token)
    {
        _orbitBadge.Visible = badge.Length > 0;
        _variations.SetBadge(_orbitBadge, _orbitBadgeText, token);
        _orbitBadgeText.Text = badge;
    }

    private void ShowNav(OrbitMapInput? input)
    {
        var navKm = input?.NavUncertaintyM / 1000 ?? 0;
        var shown = navKm > 0 && _text is not null;
        _navText.Text = shown ? _text!.Format("map.navValue", ("value", FlightFormat.AltitudeKm(navKm))) : FlightFormat.Placeholder;
        _navMeta.ShowText(shown ? _text!.Get("map.navMeta") : null);
    }

    private void ShowOrbitChip(OrbitMapFrame? frame, OrbitMapInput? input)
    {
        if (frame?.Orbit is { } chip)
        {
            ShowOrbitChip(chip, input);
            return;
        }
        var apsides = new List<string>(2);
        string? meta = null;
        if (frame is not null)
        {
            foreach (var apsis in frame.Apsides)
            {
                apsides.Add(apsis.Label);
                meta ??= apsis.Meta;
            }
        }
        if (meta is null && input?.Orbit is { } orbit && _text is not null) meta = _text.OrbitMeta(orbit);
        _orbitText.Text = apsides.Count > 0 ? string.Join(" · ", apsides) : FlightFormat.Placeholder;
        _orbitMeta.ShowText(meta);

        var badge = string.Empty;
        string? token = null;
        if (_tracking.Stale)
        {
            badge = StaleBadge();
            token = "caution";
        }
        else if (input?.Orbit?.Estimated == true && _text is not null)
        {
            badge = _text.Get("map.badgeEstimated");
            token = "info";
        }
        ShowBadge(badge, token);
        ShowNav(input);
    }
}
