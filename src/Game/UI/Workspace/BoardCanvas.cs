using Godot;
using Usa.Game.Presentation.Workspace;
using Usa.Game.UI.Console;

namespace Usa.Game.UI.Workspace;

/// <summary>
/// The workspace board (Figma G5-01a node 36:6607, "Board part" Part / Fixed port / Invalid in 36:242): the 16 px grid
/// (<c>text.off</c> at 25 % layer opacity, Gate 5 rev 2), fixed source and sink ports, placed cards, power wires and the
/// live drag and wire gestures with their red ✕ and reason. Origin top-left = board pixel (0, 0); it only draws, the
/// workspace view forwards pointer input to the model.
/// </summary>
public sealed partial class BoardCanvas : Control
{
    public const string CantConnect = "Can't connect";

    private Control _gridLayer = null!;
    private Control _contentLayer = null!;
    private WorkspaceModel? _model;
    private ConsolePalette _palette = ConsolePalette.Default;
    private Vector2 _pointer;

    public override void _Ready()
    {
        _gridLayer = GetNode<Control>("%GridLayer");
        _contentLayer = GetNode<Control>("%ContentLayer");
        _gridLayer.Draw += DrawGrid;
        _contentLayer.Draw += DrawContent;
    }

    public void Bind(WorkspaceModel? model)
    {
        _model = model;
        Redraw();
    }

    public void SetPalette(ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        Redraw();
    }

    /// <summary>The pointer in board pixels, for the end of a wire being drawn.</summary>
    public void SetPointer(Vector2 boardPosition)
    {
        _pointer = boardPosition;
        if (_model?.Wire is not null) _contentLayer.QueueRedraw();
    }

    public void Redraw()
    {
        if (!IsNodeReady()) return;
        _gridLayer.QueueRedraw();
        _contentLayer.QueueRedraw();
    }

    private Color Token(string key) => _palette.Of(key);

    private void DrawGrid()
    {
        if (_model is not { } model) return;
        var layout = model.Layout;
        var colour = Token("text.off");
        for (var column = 0; column <= layout.Columns; column++)
        {
            var x = column * BoardLayout.CellPx;
            _gridLayer.DrawLine(new Vector2(x, 0), new Vector2(x, layout.HeightPx), colour, 1);
        }
        for (var row = 0; row <= layout.Rows; row++)
        {
            var y = row * BoardLayout.CellPx;
            _gridLayer.DrawLine(new Vector2(0, y), new Vector2(layout.WidthPx, y), colour, 1);
        }
    }

    private void DrawContent()
    {
        if (_model is not { IsOpen: true } model) return;
        var font = GetThemeFont("font", "Small");
        var fontSize = GetThemeFontSize("font_size", "Small");
        var metaFont = GetThemeFont("font", "Meta");
        var metaSize = GetThemeFontSize("font_size", "Meta");

        foreach (var wire in model.Wires) DrawWire(new Vector2(wire.AXPx, wire.AYPx), new Vector2(wire.BXPx, wire.BYPx), Token("caution"), false);

        foreach (var node in model.FixedNodes)
        {
            var rect = new Rect2(node.XPx, node.YPx, node.WidthPx, node.HeightPx);
            _contentLayer.DrawRect(rect, Token("bg.2"));
            _contentLayer.DrawRect(rect, Token("info"), false, 1);
            DrawPort(node.Anchor);
            var labelX = node.Port == BoardPort.Source ? node.XPx : node.XPx + node.WidthPx;
            var text = node.Label;
            var width = metaFont.GetStringSize(text, HorizontalAlignment.Left, -1, metaSize).X;
            var x = node.Port == BoardPort.Source ? labelX : labelX - width;
            _contentLayer.DrawString(metaFont, new Vector2(x, node.YPx - 4), text, HorizontalAlignment.Left, -1, metaSize, Token("info"));
        }

        foreach (var card in model.Cards)
        {
            var rect = new Rect2(card.XPx, card.YPx, card.WidthPx, card.HeightPx);
            var dragged = model.Drag is { Card: { } moving } && moving == card.Id;
            _contentLayer.DrawRect(rect, Token("bg.2"));
            _contentLayer.DrawRect(rect, Token(dragged ? "text.off" : "line.2"), false, 1);
            var textLeft = card.XPx + 10;
            _contentLayer.DrawString(font, new Vector2(textLeft, card.YPx + 20), card.Name, HorizontalAlignment.Left, card.WidthPx - 20, fontSize, Token("text.hi"));
            var spec = string.Join(" · ", new[] { card.LengthText, card.RatingText }.Where(s => s is not null));
            _contentLayer.DrawString(metaFont, new Vector2(textLeft, card.YPx + 36), spec, HorizontalAlignment.Left, card.WidthPx - 20, metaSize, Token("text.mid"));
            foreach (var port in card.Ports) DrawPort(port);
        }

        if (model.Drag is { } drag) DrawDrag(drag, font, fontSize, metaFont, metaSize);
        if (model.Wire is { } gesture) DrawWireGesture(model, gesture, font, fontSize, metaFont, metaSize);
    }

    private void DrawPort(BoardPortView port)
    {
        var at = new Vector2(port.AnchorXPx, port.AnchorYPx);
        var fill = port.Wired ? Token("caution") : Token("bg.1");
        if (port.ConnectorId == "d_type")
        {
            var rect = new Rect2(at - new Vector2(5, 4), new Vector2(10, 8));
            _contentLayer.DrawRect(rect, fill);
            _contentLayer.DrawRect(rect, Token("text.mid"), false, 1.5f);
        }
        else
        {
            _contentLayer.DrawCircle(at, 5, fill);
            _contentLayer.DrawArc(at, 5, 0, Mathf.Tau, 16, Token("text.mid"), 1.5f, true);
        }
    }

    private void DrawWire(Vector2 from, Vector2 to, Color colour, bool dashed)
    {
        var midX = Mathf.Round((from.X + to.X) / 2);
        Vector2[] path = [from, new(midX, from.Y), new(midX, to.Y), to];
        for (var i = 1; i < path.Length; i++)
        {
            if (dashed) _contentLayer.DrawDashedLine(path[i - 1], path[i], colour, 2, 4);
            else _contentLayer.DrawLine(path[i - 1], path[i], colour, 2);
        }
    }

    private void DrawDrag(DragPreview drag, Font font, int fontSize, Font metaFont, int metaSize)
    {
        var rect = new Rect2(drag.XPx, drag.YPx, drag.WidthPx, drag.HeightPx);
        var colour = Token(drag.IsValid ? "info" : "critical");
        _contentLayer.DrawRect(rect, Token("bg.3"));
        _contentLayer.DrawRect(rect, colour, false, 2);
        if (!drag.IsValid && drag.Message.Length > 0)
            DrawReason(new Vector2(drag.XPx, drag.YPx + drag.HeightPx + 6), CantConnect, drag.Message, font, fontSize, metaFont, metaSize);
    }

    private void DrawWireGesture(WorkspaceModel model, WireGesture gesture, Font font, int fontSize, Font metaFont, int metaSize)
    {
        if (FindPort(model, gesture.From) is not { } from) return;
        var start = new Vector2(from.AnchorXPx, from.AnchorYPx);
        var hover = gesture.Hover is { } target ? FindPort(model, target) : null;
        var end = hover is null ? _pointer : new Vector2(hover.AnchorXPx, hover.AnchorYPx);
        var invalid = hover is not null && !gesture.HoverValid;
        DrawWire(start, end, Token(invalid ? "critical" : "info"), true);
        if (!invalid) return;
        var critical = Token("critical");
        _contentLayer.DrawCircle(end, 8, Token("bg.1"));
        _contentLayer.DrawArc(end, 8, 0, Mathf.Tau, 20, critical, 1.5f, true);
        _contentLayer.DrawLine(end + new Vector2(-4, -4), end + new Vector2(4, 4), critical, 2, true);
        _contentLayer.DrawLine(end + new Vector2(-4, 4), end + new Vector2(4, -4), critical, 2, true);
        DrawReason(end + new Vector2(14, 6), CantConnect, gesture.HoverMessage, font, fontSize, metaFont, metaSize);
    }

    /// <summary>The tooltip-style reason box next to a rejected drop or connection.</summary>
    private void DrawReason(Vector2 origin, string title, string message, Font font, int fontSize, Font metaFont, int metaSize)
    {
        const float width = 260;
        const float padding = 8;
        var lines = metaFont.GetMultilineStringSize(message, HorizontalAlignment.Left, width - (padding * 2), metaSize);
        var size = new Vector2(width, fontSize + lines.Y + (padding * 3));
        var maxX = Size.X - size.X - 2;
        var at = new Vector2(Mathf.Round(Math.Clamp(origin.X, 2, Math.Max(2, maxX))), Mathf.Round(Math.Min(origin.Y, Size.Y - size.Y - 2)));
        var rect = new Rect2(at, size);
        _contentLayer.DrawRect(rect, Token("bg.2"));
        _contentLayer.DrawRect(rect, Token("line.2"), false, 1);
        _contentLayer.DrawString(font, at + new Vector2(padding, padding + fontSize), title, HorizontalAlignment.Left, -1, fontSize, Token("text.hi"));
        _contentLayer.DrawMultilineString(metaFont, at + new Vector2(padding, (padding * 2) + fontSize + metaSize), message,
            HorizontalAlignment.Left, width - (padding * 2), metaSize, -1, Token("text.mid"));
    }

    private static BoardPortView? FindPort(WorkspaceModel model, BoardPort port)
    {
        foreach (var node in model.FixedNodes)
        {
            if (node.Anchor.Port == port) return node.Anchor;
        }
        foreach (var card in model.Cards)
        {
            foreach (var view in card.Ports)
            {
                if (view.Port == port) return view;
            }
        }
        return null;
    }
}
