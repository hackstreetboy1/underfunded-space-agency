using Godot;
using Usa.Game.Presentation.Diagram;
using Usa.Game.Presentation.ViewModels;
using Usa.Game.UI.Console.Binding;
using Usa.Simulation.Kernel;

namespace Usa.Game.UI.Console.Views;

/// <summary>
/// The internal 2D diagram (<c>res://ui/views/InternalDiagram.tscn</c>; UI.md 4.5, Gate 3): the drawn
/// <see cref="DiagramCanvas"/>, layer toggles (Power / Fluids / Propellant / Data), Fit, the hover tooltip (name, family,
/// known state, dependencies) and the side inspector of the selected part with "Ask crew to inspect" and, for a part whose
/// failure is confirmed, "Open repair workspace". Every state shown is the ground's knowledge (<see cref="DiagramScene"/>).
/// </summary>
public sealed partial class InternalDiagramView : Control
{
    private DiagramCanvas _canvas = null!;
    private (Button Button, DiagramLayers Layer)[] _layers = [];
    private Button _fit = null!;
    private Control _tooltip = null!;
    private Label _tooltipText = null!;
    private Control _inspector = null!;
    private Label _inspectorName = null!;
    private Label _inspectorFamily = null!;
    private Label _inspectorState = null!;
    private Label _inspectorConfirmed = null!;
    private Label _inspectorFeeds = null!;
    private Button _askInspect = null!;
    private Button _openWorkspace = null!;
    private DiagramScene? _scene;
    private ConsolePalette _palette = ConsolePalette.Default;
    private ConsoleLabels? _labels;

    /// <summary>"Open repair workspace" on a part whose failure is confirmed.</summary>
    public event Action<ComponentId>? OpenWorkspaceRequested;

    /// <summary>"Ask crew to inspect": the part's id and name, for a pre-filled crew order.</summary>
    public event Action<ComponentId, string>? InspectRequested;

    public override void _Ready()
    {
        // The canvas is a scripted node inside this scene, not an instance.
        _canvas = (DiagramCanvas)GetNode<Control>("%Canvas");
        _layers =
        [
            (GetNode<Button>("%LayerPower"), DiagramLayers.Power),
            (GetNode<Button>("%LayerFluid"), DiagramLayers.Fluid),
            (GetNode<Button>("%LayerPropellant"), DiagramLayers.Propellant),
            (GetNode<Button>("%LayerData"), DiagramLayers.Data),
        ];
        _fit = GetNode<Button>("%Fit");
        _tooltip = GetNode<Control>("%Tooltip");
        _tooltipText = GetNode<Label>("%TooltipText");
        _inspector = GetNode<Control>("%Inspector");
        _inspectorName = GetNode<Label>("%InspectorName");
        _inspectorFamily = GetNode<Label>("%InspectorFamily");
        _inspectorState = GetNode<Label>("%InspectorState");
        _inspectorConfirmed = GetNode<Label>("%InspectorConfirmed");
        _inspectorFeeds = GetNode<Label>("%InspectorFeeds");
        _askInspect = GetNode<Button>("%AskInspect");
        _openWorkspace = GetNode<Button>("%OpenWorkspace");

        foreach (var (button, layer) in _layers) button.Toggled += _ => ToggleLayer(layer);
        _fit.Pressed += _canvas.Fit;
        _canvas.HoverChanged += OnHover;
        _canvas.Clicked += OnClicked;
        _askInspect.Pressed += () =>
        {
            if (_scene?.Selected is { } id) InspectRequested?.Invoke(id, _scene.Layout.NameOf(id));
        };
        _openWorkspace.Pressed += () =>
        {
            if (_scene?.Selected is { } id) OpenWorkspaceRequested?.Invoke(id);
        };
        _tooltip.Visible = false;
        _inspector.Visible = false;
    }

    public void Bind(DiagramScene scene, ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(palette);
        _scene = scene;
        _palette = palette;
        _canvas.Scene = scene;
        foreach (var (button, layer) in _layers) button.SetPressedNoSignal((scene.Layers & layer) != 0);
        Redraw();
    }

    public void Unbind()
    {
        _scene = null;
        _canvas.Scene = null;
        _tooltip.Visible = false;
        _inspector.Visible = false;
    }

    public void SetPalette(ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        Redraw();
    }

    public void ApplyLabels(ConsoleLabels labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        _labels = labels;
        _layers[0].Button.Text = labels.DiagramLayerPower;
        _layers[1].Button.Text = labels.DiagramLayerFluids;
        _layers[2].Button.Text = labels.DiagramLayerPropellant;
        _layers[3].Button.Text = labels.DiagramLayerData;
        _fit.Text = labels.DiagramFit;
        _askInspect.Text = labels.DiagramAskInspect;
        _openWorkspace.Text = labels.DiagramOpenWorkspace;
    }

    /// <summary>The runtime's 10 Hz pump: redraws only when the knowledge behind the frame changed.</summary>
    public void Refresh()
    {
        if (_scene is { KnowledgeOutdated: true } && IsVisibleInTree()) Redraw();
    }

    private void Redraw()
    {
        if (_scene is not { } scene) return;
        _canvas.Show(scene.BuildFrame(), _palette, scene.Layout.Options.NodeRadiusPx);
        ShowInspector();
    }

    private void ToggleLayer(DiagramLayers layer)
    {
        if (_scene is not { } scene) return;
        scene.ToggleLayer(layer);
        Redraw();
    }

    private void OnHover(ComponentId? component, Vector2 pointer)
    {
        if (_scene is not { } scene) return;
        if (scene.Hovered != component)
        {
            scene.Hover(component);
            Redraw();
        }
        if (component is not { } id)
        {
            _tooltip.Visible = false;
            return;
        }
        var tooltip = scene.Tooltip(id);
        _tooltipText.Text = TooltipLines(tooltip);
        _tooltip.Visible = true;
        var size = _tooltip.GetCombinedMinimumSize();
        _tooltip.Position = new Vector2(Math.Min(pointer.X + 16, Size.X - size.X), Math.Min(pointer.Y + 16, Size.Y - size.Y)).Round();
    }

    private void OnClicked(ComponentId? component)
    {
        if (_scene is not { } scene) return;
        scene.Select(component);
        Redraw();
    }

    private void ShowInspector()
    {
        if (_scene is not { Selected: { } id } scene)
        {
            _inspector.Visible = false;
            return;
        }
        var tooltip = scene.Tooltip(id);
        var display = scene.DisplayOf(id);
        _inspector.Visible = true;
        _inspectorName.Text = tooltip.Name;
        _inspectorFamily.Text = tooltip.Family;
        _inspectorState.Text = tooltip.Badge is { } badge ? tooltip.KnownState + "  " + badge : tooltip.KnownState;
        _inspectorConfirmed.Text = tooltip.ConfirmedBy is { } who && tooltip.ConfirmedAt is { } at
            ? _labels?.DiagramConfirmed(who, ReadoutFormatter.FormatMetShort(at)) ?? who
            : _labels?.DiagramNotConfirmed ?? string.Empty;
        _inspectorFeeds.Text = string.Join("\n", new[] { tooltip.Feeds, tooltip.FedBy }.Where(line => line is not null));
        // The workspace opens only for a failure the ground has confirmed (UI.md 4.5).
        _openWorkspace.Visible = display.State == DiagramLookState.ConfirmedDamaged;
    }

    private static string TooltipLines(DiagramTooltip tooltip)
    {
        var lines = new List<string>(4) { tooltip.Name + " · " + tooltip.Family, tooltip.Badge is { } badge ? tooltip.KnownState + " · " + badge : tooltip.KnownState };
        if (tooltip.Feeds is { } feeds) lines.Add(feeds);
        if (tooltip.FedBy is { } fedBy) lines.Add(fedBy);
        return string.Join("\n", lines);
    }
}
