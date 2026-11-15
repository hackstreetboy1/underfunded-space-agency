using Godot;
using Usa.Game.UI.Theme;
using Usa.Game.Presentation.Console.Resources;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.UI.Console.Binding;
using Usa.Game.UI.Console.Components;
using Usa.Game.UI.Console.Overlays;

namespace Usa.Game.UI.Console.Regions;

/// <summary>
/// The resource strip (<c>ResourceStrip.tscn</c>, Figma G1-03 node 12:286; UI.md 3.2): one tile per resource, the Crew
/// tile's mini health bars and the forecast block (title with its EST badge and coverage, the range, then limit and
/// confidence), whose toggle opens the breakdown popover (<c>ForecastBreakdown.tscn</c>).
/// </summary>
public sealed partial class ResourceStrip : MarginContainer
{
    private readonly BindingScope _bindings = new();
    private HBoxContainer _tiles = null!;
    private readonly HashSet<ReadoutTile> _fitHidden = [];
    private bool _fitQueued;
    private Control _crewTile = null!;
    private Label _crewCaption = null!;
    private VBoxContainer _crewBars = null!;
    private Label _forecastCaption = null!;
    private BadgeView _forecastBadge = null!;
    private Label _forecastValue = null!;
    private Label _forecastText = null!;
    private BadgeView _forecastCoverage = null!;
    private Button _forecastToggle = null!;
    private ForecastBreakdownPopup _breakdown = null!;
    private Control _forecastBlock = null!;
    private ResourceStripViewModel? _strip;
    private ListMirror<ResourceTileViewModel, ReadoutTile>? _tileViews;
    private ConsoleLayoutMetrics? _layout;
    private ConsoleLabels? _labels;

    [Export]
    public PackedScene? TileScene { get; set; }

    [Export]
    public PackedScene? CrewBarScene { get; set; }

    public override void _Ready()
    {
        _tiles = GetNode<HBoxContainer>("%Tiles");
        _crewTile = GetNode<Control>("%CrewTile");
        _crewCaption = GetNode<Label>("%CrewCaption");
        _crewBars = GetNode<VBoxContainer>("%CrewBars");
        _forecastCaption = GetNode<Label>("%ForecastCaption");
        _forecastBadge = GetNode<BadgeView>("%ForecastBadge");
        _forecastValue = GetNode<Label>("%ForecastValue");
        _forecastText = GetNode<Label>("%ForecastText");
        _forecastCoverage = GetNode<BadgeView>("%ForecastCoverage");
        _forecastToggle = GetNode<Button>("%ForecastToggle");
        StatusIconView.UseIcon(_forecastToggle, UiIcons.ActionChevron);
        _breakdown = GetNode<ForecastBreakdownPopup>("%Breakdown");
        _forecastBlock = GetNode<Control>("%Forecast");
        _forecastToggle.Pressed += () => _strip?.ToggleBreakdown();
        _breakdown.Closed += () => _strip?.CloseBreakdown();
    }

    private void QueueFit()
    {
        if (_fitQueued) return;
        _fitQueued = true;
        Callable.From(FitTiles).CallDeferred();
    }

    /// <summary>
    /// Keeps every shown tile whole: when the row is wider than its space the sparklines go first, then trailing tiles are
    /// hidden (never cut in half at the edge). Tiles the layout hides stay hidden; space coming back restores the rest.
    /// </summary>
    private void FitTiles()
    {
        _fitQueued = false;
        if (_tiles.GetParent() is not Control scroll || scroll.Size.X <= 0) return;
        var available = scroll.Size.X;
        var tiles = _tiles.GetChildren().OfType<ReadoutTile>().ToList();
        var separation = _tiles.GetThemeConstant("separation");
        foreach (var tile in tiles)
        {
            if (_fitHidden.Remove(tile)) tile.Visible = true;
            tile.SparklineSlot.Visible = true;
        }

        float Width() => tiles.Where(t => t.Visible).Sum(t => t.GetCombinedMinimumSize().X + separation) - separation;
        if (Width() <= available) return;
        foreach (var tile in tiles) tile.SparklineSlot.Visible = false;
        for (var i = tiles.Count - 1; i >= 0 && Width() > available; i--)
        {
            if (!tiles[i].Visible) continue;
            tiles[i].Visible = false;
            _fitHidden.Add(tiles[i]);
        }
    }

    public override void _ExitTree() => Unbind();

    public void Bind(ResourceStripViewModel strip, ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(strip);
        ArgumentNullException.ThrowIfNull(palette);
        Unbind();
        _strip = strip;
        if (TileScene is { } tileScene)
        {
            _tileViews = _bindings.Mirror(strip.Tiles, new ListBinder<ResourceTileViewModel, ReadoutTile>(_tiles, tileScene, (view, tile) =>
            {
                view.Bind(tile, palette);
                if (_labels is { } labels) view.ApplyLabels(labels);
                if (_layout is { } layout) view.Visible = !layout.IsTileHidden(tile.Id);
                QueueFit();
            }));
        }
        if (CrewBarScene is { } barScene)
        {
            _bindings.Mirror(strip.CrewHealth, new ListBinder<CrewHealthBar, CrewHealthMini>(_crewBars, barScene, static (view, bar) => view.Bind(bar)));
        }
        _bindings.Observe(strip.Forecast, pill =>
        {
            if (pill.Title.Length > 0) _forecastCaption.Text = pill.Title;
            _forecastValue.Text = pill.Value;
            _forecastText.Text = pill.Detail;
            _forecastText.TooltipText = pill.Text;
            _forecastBadge.Display(pill.Badge ?? string.Empty, ColourTokens.Info);
            _forecastCoverage.Display(pill.Coverage);
            _forecastToggle.Disabled = !pill.Available;
        });
        _bindings.Observe(strip.Breakdown, breakdown =>
        {
            if (breakdown is not null) _breakdown.Display(breakdown);
        });
        _bindings.Observe(strip.BreakdownOpen, ShowBreakdown);
    }

    public void Unbind()
    {
        _bindings.Release();
        _strip = null;
        _tileViews = null;
        if (IsNodeReady() && _breakdown.Visible) _breakdown.Hide();
    }

    public void ApplyLabels(ConsoleLabels labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        _labels = labels;
        _crewCaption.Text = labels.ResourceCrew;
        _forecastCaption.Text = labels.ForecastTitle;
        _forecastToggle.TooltipText = labels.ForecastBreakdownTitle;
        _breakdown.ApplyLabels(labels);
        if (_tileViews is null) return;
        foreach (var view in _tileViews.Views) view.ApplyLabels(labels);
    }

    public void ApplyLayout(ConsoleLayoutMetrics layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
        CustomMinimumSize = new Vector2(0, layout.ResourceStripPx);
        _crewTile.Visible = layout.ShowCrewTile;
        if (_tileViews is null) return;
        foreach (var view in _tileViews.Views)
        {
            if (view.TileId is { } id) view.Visible = !layout.IsTileHidden(id);
        }
        _fitHidden.Clear();
        QueueFit();
    }

    private void ShowBreakdown(bool open)
    {
        _forecastToggle.SetPressedNoSignal(open);
        if (!open)
        {
            if (_breakdown.Visible) _breakdown.Hide();
            return;
        }
        // Under the forecast block, kept inside the window.
        var block = _forecastBlock.GetGlobalRect();
        var size = _breakdown.Size;
        var x = Math.Max(0, (int)(block.End.X - size.X));
        _breakdown.Popup(new Rect2I(x, (int)block.End.Y + 4, size.X, size.Y));
    }
}
