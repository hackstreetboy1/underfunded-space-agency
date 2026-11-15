using Godot;
using Usa.Game.Presentation.Console;
using Usa.Game.Presentation.Console.Resources;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.Presentation.ViewModels;
using Usa.Game.UI.Console.Binding;

namespace Usa.Game.UI.Console.Components;

/// <summary>
/// A resource-strip tile (<c>ReadoutTile.tscn</c>, Figma G1-03 "Tile O2" … node 12:287): label, value (Readout M) with its
/// unit set small beside it (UI.md 6.2: always shown, also beside a placeholder), knowledge badge, second value, state word,
/// trend and the 60-minute sparkline. A click opens the matching systems-board tab (UI.md 3.2).
/// </summary>
public sealed partial class ReadoutTile : PanelContainer
{
    private readonly BindingScope _bindings = new();
    private Label _label = null!;
    private Label _value = null!;
    private Label _unit = null!;
    private Label _secondary = null!;
    private Label _detail = null!;
    private Label _trend = null!;
    private StatusIconView _trendIcon = null!;
    private BadgeView _badge = null!;
    private Button _ask = null!;
    private Sparkline _sparkline = null!;
    private ResourceTileViewModel? _tile;
    private ConsolePalette _palette = ConsolePalette.Default;
    private ReadoutText? _text;
    private KnowledgeVisual _knowledge = KnowledgeBadges.Unknown;

    /// <summary>The bound tile's id (<c>o2</c>, <c>food</c>…); null while unbound.</summary>
    public string? TileId => _tile?.Id;

    /// <summary>The 60-minute sparkline (<c>Sparkline.tscn</c>).</summary>
    public Control SparklineSlot => _sparkline;

    public override void _Ready()
    {
        _label = GetNode<Label>("%Label");
        _value = GetNode<Label>("%Value");
        _unit = GetNode<Label>("%Unit");
        _secondary = GetNode<Label>("%Secondary");
        _detail = GetNode<Label>("%Detail");
        _trend = GetNode<Label>("%Trend");
        // The trend arrow is an icon before the rate (review R3-11).
        _trendIcon = StatusIconView.Create(12);
        _trend.GetParent().AddChild(_trendIcon);
        _trend.GetParent().MoveChild(_trendIcon, _trend.GetIndex());
        _badge = GetNode<BadgeView>("%Badge");
        _ask = GetNode<Button>("%Ask");
        _sparkline = GetNode<Sparkline>("%Sparkline");
        _ask.Pressed += () => _tile?.Ask();
        GuiInput += OnGuiInput;
    }

    public override void _ExitTree() => Unbind();

    public void Bind(ResourceTileViewModel tile, ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentNullException.ThrowIfNull(palette);
        Unbind();
        _tile = tile;
        _palette = palette;
        _label.Text = tile.Label;
        _bindings.Observe(tile.Primary.Knowledge, knowledge =>
        {
            _knowledge = knowledge;
            Refresh();
        });
        _bindings.Observe(tile.Primary.Readout.Text, text =>
        {
            _text = text;
            Refresh();
        });
        _bindings.Observe(tile.Primary.Parts, parts =>
        {
            _value.Text = parts.SecondaryValue is { } second ? parts.Value + " / " + second : parts.Value;
            _unit.ShowText(parts.Unit);
        });
        _bindings.Observe(tile.HasSecondary, has => _secondary.Visible = has);
        _bindings.Observe(tile.Secondary.Parts, parts => _secondary.Text = parts.Joined);
        _bindings.Observe(tile.Detail, _detail.ShowText);
        _bindings.Observe(tile.Trend, trend =>
        {
            _trend.ShowText(StatusIcons.TrendText(trend));
            _trendIcon.ShowIcon(StatusIcons.For(trend.Direction), palette.Of(ColourTokens.TextMid));
        });
        _bindings.Observe(tile.Sparkline, series => _sparkline.Show(series, palette));
    }

    public void Unbind()
    {
        _bindings.Release();
        _tile = null;
        _text = null;
    }

    public void ApplyLabels(ConsoleLabels labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        _ask.Text = labels.Ask;
    }

    private void Refresh()
    {
        if (_text is not { } text) return;
        _value.SetFontColour(_palette.Of(text.Dimmed ? ColourTokens.TextMid : _knowledge.ValueColourToken));
        _badge.Display(_knowledge, text.Badge);
        _ask.Visible = text.CanAsk;
    }

    private void OnGuiInput(InputEvent input)
    {
        if (_tile is null || input is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) return;
        _tile.Open();
        AcceptEvent();
    }
}
