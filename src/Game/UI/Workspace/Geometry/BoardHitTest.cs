using Usa.Game.Presentation.Workspace;

namespace Usa.Game.UI.Workspace.Geometry;

/// <summary>What lies under the pointer on the workspace board (board pixels, origin top-left).</summary>
public static class BoardHitTest
{
    /// <summary>A port is hit within this distance of its wire anchor: the 16 px connector icon plus slack.</summary>
    public const double PortRadiusPx = 10;

    /// <summary>The topmost card (last placed) whose rectangle contains <paramref name="point"/>.</summary>
    public static BoardCard? CardAt(IReadOnlyList<BoardCard> cards, BoardPoint point)
    {
        ArgumentNullException.ThrowIfNull(cards);
        for (var i = cards.Count - 1; i >= 0; i--)
        {
            var card = cards[i];
            if (Contains(card.XPx, card.YPx, card.WidthPx, card.HeightPx, point)) return card;
        }
        return null;
    }

    public static FixedNode? NodeAt(IReadOnlyList<FixedNode> nodes, BoardPoint point)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        foreach (var node in nodes)
        {
            if (Contains(node.XPx, node.YPx, node.WidthPx, node.HeightPx, point)) return node;
        }
        return null;
    }

    /// <summary>The connector nearest to <paramref name="point"/> within <paramref name="radiusPx"/>, on a card or a fixed node.</summary>
    public static BoardPortView? PortAt(
        IReadOnlyList<BoardCard> cards, IReadOnlyList<FixedNode> nodes, BoardPoint point, double radiusPx = PortRadiusPx)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(nodes);
        BoardPortView? best = null;
        var bestDistance = radiusPx * radiusPx;
        foreach (var card in cards)
        {
            foreach (var port in card.Ports) Consider(port);
        }
        foreach (var node in nodes) Consider(node.Anchor);
        return best;

        void Consider(BoardPortView port)
        {
            var dx = port.AnchorXPx - point.X;
            var dy = port.AnchorYPx - point.Y;
            var distance = (dx * dx) + (dy * dy);
            if (distance > bestDistance) return;
            bestDistance = distance;
            best = port;
        }
    }

    private static bool Contains(int x, int y, int width, int height, BoardPoint point) =>
        point.X >= x && point.X < x + width && point.Y >= y && point.Y < y + height;
}
