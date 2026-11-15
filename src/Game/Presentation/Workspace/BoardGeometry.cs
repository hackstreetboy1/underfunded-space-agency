namespace Usa.Game.Presentation.Workspace;

/// <summary>A pointer position in board pixels (origin at the board's top-left; the tray lies at negative X).</summary>
public readonly record struct BoardPoint(double X, double Y);

/// <summary>A snapped board position in grid cells of <see cref="BoardLayout.CellPx"/> pixels.</summary>
public readonly record struct GridPoint(int Column, int Row)
{
    public int XPx => Column * BoardLayout.CellPx;

    public int YPx => Row * BoardLayout.CellPx;

    /// <summary>The nearest grid cell to a pixel position.</summary>
    public static GridPoint Snap(double xPx, double yPx) => new(
        (int)Math.Round(xPx / BoardLayout.CellPx, MidpointRounding.AwayFromZero),
        (int)Math.Round(yPx / BoardLayout.CellPx, MidpointRounding.AwayFromZero));
}

/// <summary>
/// Board size and footprints in grid cells (UI 4.6: 16 px snap grid). The defaults are the approved layout (Gate 5
/// decision 5, Figma G5-01a node 36:6607): a 496 × 496 grid in the 500 px board column, board parts 144 × 48 ("Board part"
/// in node 36:242), the fixed source and sink drawn as parts at the left (16, 48 and 16, 304 px), and the bottom rows left
/// to the board legend.
/// </summary>
/// <param name="NodeColumns">Footprint of the fixed source and sink nodes (the "Fixed port" variant is part-sized).</param>
/// <param name="SourceColumn">Top-left cell of the fixed source; with <paramref name="SourceRow"/>.</param>
/// <param name="SinkColumn">Top-left cell of the fixed sink; with <paramref name="SinkRow"/>.</param>
/// <param name="LegendRows">Bottom rows under the board legend (port key and route length); cards stay above them.</param>
public sealed record BoardLayout(
    int Columns = 31,
    int Rows = 31,
    int CardColumns = 9,
    int CardRows = 3,
    int NodeColumns = 9,
    int NodeRows = 3,
    int SourceColumn = 1,
    int SourceRow = 3,
    int SinkColumn = 1,
    int SinkRow = 19,
    int LegendRows = 3)
{
    public const int CellPx = 16;

    public static BoardLayout Default { get; } = new();

    public int WidthPx => Columns * CellPx;

    public int HeightPx => Rows * CellPx;

    /// <summary>Rows a card may occupy: the board above the legend.</summary>
    public int CardAreaRows => Rows - LegendRows;

    public GridPoint SourceCell => new(SourceColumn, SourceRow);

    public GridPoint SinkCell => new(SinkColumn, SinkRow);

    /// <summary>A fixed node's connector faces the middle of the board: its right edge in the left half, else its left edge.</summary>
    public bool NodePortOnRight(GridPoint node) => (2 * node.Column) + NodeColumns <= Columns;

    internal void Validate()
    {
        if (CardColumns < 2 || CardRows < 1 || NodeColumns < 1 || NodeRows < 1)
            throw new ArgumentException("Card and node footprints must be at least one cell (cards two wide).");
        if (LegendRows < 0 || Columns < CardColumns || CardAreaRows < CardRows)
            throw new ArgumentException("The board above the legend must fit at least one card.");
        if (!NodeFits(SourceCell) || !NodeFits(SinkCell))
            throw new ArgumentException("The fixed source and sink must lie on the board above the legend.");
        if (SourceColumn < SinkColumn + NodeColumns && SinkColumn < SourceColumn + NodeColumns
            && SourceRow < SinkRow + NodeRows && SinkRow < SourceRow + NodeRows)
            throw new ArgumentException("The fixed source and sink must not overlap.");
    }

    private bool NodeFits(GridPoint node) =>
        node.Column >= 0 && node.Row >= 0 && node.Column + NodeColumns <= Columns && node.Row + NodeRows <= CardAreaRows;
}
