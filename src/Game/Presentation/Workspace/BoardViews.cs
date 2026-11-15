using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Workspace;

/// <summary>A placed part. Pixel values are integers on the 16 px grid.</summary>
/// <param name="Name">Short name, capitalised, as on the tray card; length and rating are separate.</param>
public sealed record BoardCard(
    CardId Id,
    PartId Part,
    string Name,
    string IconKey,
    GridPoint Cell,
    int XPx,
    int YPx,
    int WidthPx,
    int HeightPx,
    string? LengthText,
    string? RatingText,
    IReadOnlyList<BoardPortView> Ports);

/// <summary>A connector and its wire anchor (card ports sit at the middle of the left and right edges).</summary>
public sealed record BoardPortView(BoardPort Port, string ConnectorId, string ConnectorName, int AnchorXPx, int AnchorYPx, bool Wired);

/// <summary>The goal's fixed source or sink port; it can be wired but never moved or removed.</summary>
public sealed record FixedNode(BoardPort Port, string Label, GridPoint Cell, int XPx, int YPx, int WidthPx, int HeightPx, BoardPortView Anchor);

/// <summary>A wire between two connectors, lower port first.</summary>
public sealed record BoardWire(BoardPort A, BoardPort B, int AXPx, int AYPx, int BXPx, int BYPx);
