namespace Usa.Game.UI.Workspace.Geometry;

/// <summary>
/// The engineering workspace's layout for the main-view region it is given: 1000 × 560 at the 1080p reference and
/// 600 × 406 in the compact 720p console (review 3, R3-04). Goal header on top; inventory tray, board and test panel
/// below (UI.md 4.6: 240 / 500 / 260). When the three columns do not fit, the test panel becomes a drawer over the
/// board and the tray narrows. The 496 px board grid scales down to fit, never below <see cref="MinBoardScale"/>; what
/// still does not fit scrolls.
/// </summary>
public sealed record WorkspaceLayout(
    bool TestDocked,
    float TrayWidthPx,
    float BoardWidthPx,
    float TestWidthPx,
    float BoardScale,
    float BoardAreaHeightPx)
{
    public const float GoalHeaderPx = 56;

    public const float PanelHeaderPx = 36;

    public const float TrayFullPx = 240;

    /// <summary>Compact tray: an inventory card (name, spec and quantity) stays readable at 192 + padding.</summary>
    public const float TrayCompactPx = 216;

    public const float TestPanelPx = 260;

    /// <summary>The narrowest board column next to a docked test panel.</summary>
    public const float BoardMinWidthPx = 360;

    /// <summary>The board grid (31 × 16 px cells) and its inset in the column.</summary>
    public const float BoardGridPx = 496;

    public const float BoardInsetXPx = 2;

    public const float BoardInsetYPx = 4;

    /// <summary>Below this the 16 px grid and part labels stop being legible; the board scrolls instead.</summary>
    public const float MinBoardScale = 0.75f;

    /// <summary>The board legend strip over the bottom of the board column.</summary>
    public const float LegendPx = 36;

    /// <summary>Test panel header, Run test / Assign row and the reason line; the checks list scrolls.</summary>
    public const float TestMinHeightPx = PanelHeaderPx + 16 + 36 + 40;

    /// <summary>Header, scroll and hint of the tray; its cards scroll.</summary>
    public const float TrayMinHeightPx = PanelHeaderPx + 56 + 40;

    /// <summary>The board viewport never gets less than this (the board scrolls inside it).</summary>
    public const float BoardViewportMinPx = LegendPx + 96;

    /// <summary>The height below which some column would clip rather than scroll.</summary>
    public static float MinHeightPx => GoalHeaderPx + Math.Max(PanelHeaderPx + BoardViewportMinPx, Math.Max(TestMinHeightPx, TrayMinHeightPx));

    /// <summary>The scaled board with its inset.</summary>
    public float BoardExtentPx => (BoardGridPx * BoardScale) + BoardInsetYPx;

    /// <summary>The board fits its column without scrolling.</summary>
    public bool BoardFits => BoardExtentPx <= BoardAreaHeightPx && (BoardGridPx * BoardScale) + BoardInsetXPx <= BoardWidthPx;

    public static WorkspaceLayout For(float widthPx, float heightPx)
    {
        var docked = widthPx >= TrayFullPx + BoardMinWidthPx + TestPanelPx;
        var tray = docked ? TrayFullPx : TrayCompactPx;
        var board = Math.Max(0, widthPx - tray - (docked ? TestPanelPx : 0));
        var area = Math.Max(0, heightPx - GoalHeaderPx - PanelHeaderPx);
        var fit = Math.Min((board - (2 * BoardInsetXPx)) / BoardGridPx, (area - (2 * BoardInsetYPx)) / BoardGridPx);
        var scale = Math.Clamp(fit, MinBoardScale, 1f);
        return new WorkspaceLayout(docked, tray, board, Math.Min(TestPanelPx, widthPx), scale, area);
    }
}
