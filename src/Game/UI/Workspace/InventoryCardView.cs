using Godot;
using Usa.Game.Presentation.Workspace;
using Usa.Game.UI.Console;

namespace Usa.Game.UI.Workspace;

/// <summary>
/// One inventory tray card (<c>InventoryCard.tscn</c>, Figma "Inventory card" Available / All placed in G5-01c node 36:242):
/// part icon, short name, length and rating, connector shape icons and quantity. Pressing it starts a tray drag on the board.
/// </summary>
public sealed partial class InventoryCardView : PanelContainer
{
    private TextureRect _icon = null!;
    private Label _name = null!;
    private Label _spec = null!;
    private Control _ports = null!;
    private TextureRect _portA = null!;
    private Control _arrow = null!;
    private TextureRect _portB = null!;
    private Label _quantity = null!;
    private PartCard? _card;

    /// <summary>The player pressed an available card: the workspace begins a tray drag of this part.</summary>
    public event Action<PartCard>? DragStarted;

    public override void _Ready()
    {
        _icon = GetNode<TextureRect>("%Icon");
        _name = GetNode<Label>("%Name");
        _spec = GetNode<Label>("%Spec");
        _ports = GetNode<Control>("%Ports");
        _portA = _ports.GetNode<TextureRect>("PortA");
        _arrow = _ports.GetNode<Control>("Arrow");
        _portB = _ports.GetNode<TextureRect>("PortB");
        _quantity = GetNode<Label>("%Quantity");
        GuiInput += OnGuiInput;
    }

    public void Show(PartCard card, WorkspaceWords words, ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(palette);
        _card = card;
        _icon.Texture = PartIcons.Part(card.IconKey);
        _icon.SelfModulate = palette.Of("text.hi");
        _name.Text = card.Name;
        var spec = new List<string>(2);
        if (card.LengthText is { } length) spec.Add(length);
        if (card.RatingText is { } rating) spec.Add(rating);
        _spec.Text = spec.Count > 0 ? string.Join(" · ", spec) : card.CarriesCurrent ? string.Empty : words.NotAConductor;
        ShowPorts(card.Connectors, palette.Of("text.mid"));
        _quantity.Text = card.Quantity == 0 && card.OnBoard > 0
            ? FormattableString.Invariant($"0 / {card.OnBoard}")
            : FormattableString.Invariant($"×{card.Quantity}");
        if (card.Shortfall > 0) _quantity.Text += FormattableString.Invariant($" (−{card.Shortfall})");
        Modulate = new Color(1, 1, 1, card.CanPlace || !card.CarriesCurrent ? 1 : 0.45f);
        TooltipText = card.CanPlace ? words.CardDrag : card.CarriesCurrent ? words.CardNoneLeft : words.CardNotCircuit;
        MouseDefaultCursorShape = card.CanPlace ? CursorShape.Drag : CursorShape.Arrow;
    }

    private void ShowPorts(IReadOnlyList<ConnectorChip> connectors, Color tint)
    {
        _ports.Visible = connectors.Count > 0;
        if (connectors.Count == 0) return;
        _portA.Texture = PartIcons.Port(connectors[0].ShapeKey);
        _portA.SelfModulate = tint;
        _portA.TooltipText = connectors[0].Name;
        var pair = connectors.Count > 1;
        _arrow.Visible = pair;
        _portB.Visible = pair;
        if (!pair) return;
        _portB.Texture = PartIcons.Port(connectors[^1].ShapeKey);
        _portB.SelfModulate = tint;
        _portB.TooltipText = connectors[^1].Name;
    }

    private void OnGuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } || _card is not { CanPlace: true } card) return;
        DragStarted?.Invoke(card);
        AcceptEvent();
    }
}
