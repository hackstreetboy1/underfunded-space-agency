using Godot;
using Usa.Game.Presentation.Spacecraft;
using Usa.Game.Presentation.Views;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.UI.Console.Binding;
using Usa.Game.UI.Console.Components;
using Usa.Game.UI.Entry;

namespace Usa.Game.UI.Console.Views;

/// <summary>
/// The External view chrome (<c>res://ui/views/ExternalView.tscn</c>, UI.md 4.4 and 9): the 3D view
/// (<c>scenes/views/ExternalView.tscn</c>) filling the tab, the camera buttons (Reset, Focus vehicle, Focus selected part,
/// Focus last revealed), the legend bottom-left, the first-use legend callout, part tags placed with
/// <see cref="ExternalView.TryGetTagAnchor"/>, and the entry HUD that overlays the view during entry (UI.md 4.2).
/// The camera never moves by itself: only these buttons and the player's drag move it.
/// </summary>
public sealed partial class ExternalViewChrome : Control
{
    private readonly List<Label> _tagLabels = [];
    private IReadOnlyList<ExternalViewTag> _tags = [];
    private ExternalView _viewport = null!;
    private Button _reset = null!;
    private Button _focusVehicle = null!;
    private Button _focusComponent = null!;
    private Button _focusLast = null!;
    private Label _legendNominal = null!;
    private Label _legendDamage = null!;
    private Label _legendUnconfirmed = null!;
    private Label _legendOlder = null!;
    private Control _firstUse = null!;
    private Control _legend = null!;
    private Control _cameraRow = null!;
    private bool _overlaysForHud;
    private bool _overlaysSuppressed;
    private bool _firstUseWanted;
    private Label _firstUseText = null!;
    private Label _firstUseDetail = null!;
    private Button _firstUseDismiss = null!;
    private Control _tagLayer = null!;
    private string? _picked;
    private StatusIconView[] _legendIcons = [];

    /// <summary>The first-use legend callout was dismissed (the host can remember it in the profile).</summary>
    public event Action? FirstUseLegendDismissed;

    /// <summary>The 3D view scene inside the chrome.</summary>
    public ExternalView Viewport => _viewport;

    public SpacecraftView View => _viewport.View;

    public EarthBackdrop Backdrop => _viewport.Backdrop;

    /// <summary>The entry and landing HUD over the view (UI.md 4.2); it shows itself during entry.</summary>
    public EntryHudView EntryHud { get; private set; } = null!;

    /// <summary>The part the player picked last; null when none is.</summary>
    public string? PickedComponentId => _picked;

    public override void _Ready()
    {
        _viewport = GetNode<ExternalView>("%Viewport3D");
        EntryHud = GetNode<EntryHudView>("%EntryHud");
        _reset = GetNode<Button>("%ResetButton");
        _focusVehicle = GetNode<Button>("%FocusVehicleButton");
        _focusComponent = GetNode<Button>("%FocusComponentButton");
        _focusLast = GetNode<Button>("%FocusLastButton");
        _legendNominal = GetNode<Label>("%LegendNominal");
        _legendDamage = GetNode<Label>("%LegendDamage");
        _legendUnconfirmed = GetNode<Label>("%LegendUnconfirmed");
        _legendOlder = GetNode<Label>("%LegendOlder");
        _firstUse = GetNode<Control>("%FirstUse");
        _firstUseText = GetNode<Label>("%FirstUseText");
        _firstUseDetail = GetNode<Label>("%FirstUseDetail");
        _firstUseDismiss = GetNode<Button>("%FirstUseDismiss");
        _tagLayer = GetNode<Control>("%Tags");
        _legendIcons = [Icon(_legendNominal), Icon(_legendDamage), Icon(_legendUnconfirmed), Icon(_legendOlder)];
        ShowLegendIcons(ConsolePalette.Default);

        _reset.Pressed += ResetCamera;
        _focusVehicle.Pressed += () => FocusVehicle();
        _focusComponent.Pressed += () => FocusPicked();
        _focusLast.Pressed += () => FocusLastRevealed();
        _firstUseDismiss.Pressed += DismissFirstUseLegend;
        _legend = GetNode<Control>("Legend");
        _cameraRow = GetNode<Control>("Camera");
        View.ComponentPicked += OnComponentPicked;
        View.PickCleared += OnPickCleared;
        View.ComponentRevealed += OnComponentRevealed;
        _focusComponent.Disabled = true;
        _focusLast.Disabled = true;
        SetProcess(false);
    }

    public override void _ExitTree()
    {
        View.ComponentPicked -= OnComponentPicked;
        View.PickCleared -= OnPickCleared;
        View.ComponentRevealed -= OnComponentRevealed;
    }

    // Presentation only: tags follow their parts as the camera moves.
    public override void _Process(double delta)
    {
        PlaceTags();
        // Polled rather than signal-connected: a C# handler on a child's signal broke re-instancing the console
        // ("Handle is not initialized" in SwapGCHandleForType) on the next mission start.
        if (_overlaysForHud != EntryHud.Visible) UpdateOverlays();
    }

    public void ApplyLabels(ConsoleLabels labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        _reset.Text = labels.ExternalReset;
        _focusVehicle.Text = labels.ExternalFocusVehicle;
        _focusComponent.Text = labels.ExternalFocusComponent;
        _focusLast.Text = labels.ExternalFocusLastRevealed;
        _legendNominal.Text = labels.LegendConfirmedNominal;
        _legendDamage.Text = labels.LegendConfirmedDamage;
        _legendUnconfirmed.Text = labels.LegendUnconfirmed;
        _legendOlder.Text = labels.LegendOlder;
        _firstUseText.Text = labels.FirstUseLegend;
        _firstUseDetail.Text = labels.FirstUseLegendDetail;
        _firstUseDismiss.Text = labels.FirstUseDismiss;
    }

    public void SetPalette(ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        EntryHud.SetPalette(palette);
        ShowLegendIcons(palette);
    }

    // The legend's shapes are approved icons beside each word (review R3-11), coloured like the 3D looks they explain.
    private void ShowLegendIcons(ConsolePalette palette)
    {
        _legendIcons[0].Show(StatusShape.FilledCircle, palette.Of(ColourTokens.Ok));
        _legendIcons[1].Show(StatusShape.Octagon, palette.Of(ColourTokens.Critical));
        _legendIcons[2].Show(StatusShape.DashedCircle, palette.Of(ColourTokens.Unknown));
        _legendIcons[3].Show(StatusShape.Clock, palette.Of(ColourTokens.TextMid));
    }

    /// <summary>Shows or hides the first-use legend callout ("Grey means not yet confirmed, not undamaged.").</summary>
    /// <summary>The first-use tip is asked for (not yet dismissed), shown or waiting for the entry HUD to step aside.</summary>
    public bool IsFirstUseLegendWanted => _firstUseWanted;

    /// <summary>The first-use tip is on screen now.</summary>
    public bool IsFirstUseLegendShowing => _firstUse.IsVisibleInTree();

    public void SetFirstUseLegendVisible(bool visible)
    {
        _firstUseWanted = visible;
        UpdateOverlays();
    }

    // The entry HUD fills the view during the burn and entry: the legends step aside instead of covering its readouts.
    private void UpdateOverlays()
    {
        var hud = EntryHud is { } entryHud && entryHud.Visible;
        _overlaysForHud = hud;
        var clear = !hud && !_overlaysSuppressed;
        _firstUse.Visible = _firstUseWanted && clear;
        _legend.Visible = clear;
        _cameraRow.Visible = clear;
    }

    /// <summary>Hides the legends and camera buttons while the player watches the pod without the entry HUD (splashdown).</summary>
    public void SetOverlaysSuppressed(bool suppressed)
    {
        if (_overlaysSuppressed == suppressed) return;
        _overlaysSuppressed = suppressed;
        UpdateOverlays();
    }

    /// <summary>"Reset".</summary>
    public void ResetCamera() => _viewport.ResetCamera();

    /// <summary>"Focus vehicle" (<c>F</c>): the crewed vehicle.</summary>
    public bool FocusVehicle() => _viewport.FocusVehicle(View.PrimaryVehicleId);

    /// <summary>Frames one part, e.g. from the event log or a fact chip.</summary>
    public bool FocusComponent(string componentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        return _viewport.FocusComponent(componentId);
    }

    /// <summary>"Focus last revealed"; false when nothing has been revealed yet.</summary>
    public bool FocusLastRevealed() => _viewport.FocusLastRevealed();

    /// <summary>Replaces the part tags; each follows its part while it is on screen and in front of the camera.</summary>
    public void SetTags(IReadOnlyList<ExternalViewTag> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        _tags = tags;
        while (_tagLabels.Count < tags.Count)
        {
            var label = new Label { ThemeTypeVariation = ThemeVariations.BadgeLabel, MouseFilter = MouseFilterEnum.Ignore };
            _tagLayer.AddChild(label);
            _tagLabels.Add(label);
        }
        for (var i = 0; i < _tagLabels.Count; i++)
        {
            _tagLabels[i].Visible = i < tags.Count;
            if (i < tags.Count) _tagLabels[i].Text = tags[i].Text;
        }
        SetProcess(tags.Count > 0);
        PlaceTags();
    }

    private static StatusIconView Icon(Label label)
    {
        var icon = StatusIconView.Create(12);
        var row = label.GetParent();
        row.AddChild(icon);
        row.MoveChild(icon, 0);
        return icon;
    }

    private bool FocusPicked() => _picked is { } id && _viewport.FocusComponent(id);

    private void PlaceTags()
    {
        for (var i = 0; i < _tags.Count; i++)
        {
            var label = _tagLabels[i];
            if (!_viewport.TryGetTagAnchor(_tags[i].ComponentId, out var anchor))
            {
                label.Visible = false;
                continue;
            }
            label.Visible = true;
            var size = label.GetCombinedMinimumSize();
            label.Position = (anchor - new Vector2(size.X / 2, size.Y + 4)).Round();
        }
    }

    private void OnComponentPicked(string componentId)
    {
        _picked = componentId;
        _focusComponent.Disabled = false;
    }

    private void OnPickCleared()
    {
        _picked = null;
        _focusComponent.Disabled = true;
    }

    private void OnComponentRevealed(string componentId) => _focusLast.Disabled = false;

    /// <summary>Closes the first-use tip as its button does (it counts as seen).</summary>
    public void DismissFirstUseLegend()
    {
        _firstUseWanted = false;
        _firstUse.Visible = false;
        FirstUseLegendDismissed?.Invoke();
    }
}
