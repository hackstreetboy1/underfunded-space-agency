namespace Usa.Game.UI.Console.Binding;

public enum ConsoleLayoutMode
{
    /// <summary>The 1920 × 1080 reference layout (UI.md 3, Figma G1-03 node 12:242).</summary>
    Full,

    /// <summary>The compact layout for a 1440 × 810 logical canvas (Figma node 21:807), pending Gate 1 decision 1.</summary>
    Compact,
}

/// <summary>
/// Sizes the console shell applies to its containers (logical pixels). The containers stretch, so these are minimums
/// and fixed column widths; the centre column and the event log take the rest.
/// </summary>
/// <param name="MainViewPx">View tabs plus the main view.</param>
/// <param name="HiddenTileIds">Resource tiles the layout leaves out (their ids, e.g. <c>food</c>).</param>
public sealed record ConsoleLayoutMetrics(
    ConsoleLayoutMode Mode,
    int GutterPx,
    int HeaderPx,
    int ResourceStripPx,
    int LeftColumnPx,
    int CentreColumnPx,
    int RightColumnPx,
    int MainViewPx,
    int TaskBarPx,
    int EventLogMinPx,
    int TipCardMinPx,
    int CrewCardPx,
    int CrewFeedWidthPx,
    int CrewFeedHeightPx,
    bool CollapseTipCard,
    bool ShowCrewRole,
    bool ShowCrewTile,
    IReadOnlyList<string> HiddenTileIds)
{
    /// <summary>UI.md 3.4: the live crew feed is 192 × 144.</summary>
    public const int FullCrewFeedWidthPx = 192;

    public const int FullCrewFeedHeightPx = 144;

    /// <summary>Narrowest logical width that fits the three columns and their gutters.</summary>
    public int RequiredWidthPx => LeftColumnPx + CentreColumnPx + RightColumnPx + 4 * GutterPx;

    /// <summary>The reference layout from the layout tokens (<c>layout.*</c> in <c>UiTokens</c>).</summary>
    public static ConsoleLayoutMetrics Full(
        int gutterPx, int headerPx, int resourceStripPx, int leftColumnPx, int centreColumnPx, int rightColumnPx,
        int viewTabsPx, int mainViewPx, int taskBarPx, int eventLogPx, int tipCardPx, int crewCardPx) =>
        new(ConsoleLayoutMode.Full, gutterPx, headerPx, resourceStripPx, leftColumnPx, centreColumnPx, rightColumnPx,
            viewTabsPx + mainViewPx, taskBarPx, eventLogPx, tipCardPx, crewCardPx, FullCrewFeedWidthPx, FullCrewFeedHeightPx,
            CollapseTipCard: false, ShowCrewRole: true, ShowCrewTile: true, HiddenTileIds: []);

    /// <summary>
    /// The G1-03 compact frame (node 21:807): left 352, centre 600, right 440; crew cards 144 with a 160 × 112 feed and no
    /// role line; the tip card starts collapsed; the Food and Crew tiles are hidden; the main view and the event log share
    /// what the 810 px canvas leaves.
    /// </summary>
    public static ConsoleLayoutMetrics CompactOf(ConsoleLayoutMetrics full)
    {
        ArgumentNullException.ThrowIfNull(full);
        return full with
        {
            Mode = ConsoleLayoutMode.Compact,
            LeftColumnPx = 352,
            CentreColumnPx = 600,
            MainViewPx = 446,
            EventLogMinPx = 128,
            CrewCardPx = 144,
            CrewFeedWidthPx = 160,
            CrewFeedHeightPx = 112,
            CollapseTipCard = true,
            ShowCrewRole = false,
            ShowCrewTile = false,
            HiddenTileIds = ["food"],
        };
    }

    /// <summary>The full layout when the logical canvas is wide enough for it, otherwise the compact one.</summary>
    public static ConsoleLayoutMetrics Select(int logicalWidthPx, ConsoleLayoutMetrics full, ConsoleLayoutMetrics compact)
    {
        ArgumentNullException.ThrowIfNull(full);
        ArgumentNullException.ThrowIfNull(compact);
        return logicalWidthPx >= full.RequiredWidthPx ? full : compact;
    }

    public bool IsTileHidden(string tileId)
    {
        foreach (var hidden in HiddenTileIds)
        {
            if (string.Equals(hidden, tileId, StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
