namespace Usa.Game.UI.Map.Geometry;

/// <summary>Where the burn planner sits in the Orbit view.</summary>
public enum PlannerPlacement
{
    /// <summary>Docked right beside the map (UI.md 4.1, 300 wide).</summary>
    Docked,

    /// <summary>A drawer over the map's right edge, opened from the map (the compact 720p view, UI.md 3 Gate 1 rule).</summary>
    Drawer,
}

/// <summary>
/// The Orbit view's layout for the main-view region it is given: 1000 × 560 at the 1080p reference and 600 × 406 in the
/// compact 720p console (review 3, R3-04). The planner docks when the map keeps <see cref="MapMinWidthPx"/> beside it and
/// becomes a drawer otherwise; its body scrolls, so only its header and Execute row need height.
/// </summary>
public sealed record OrbitMapLayout(
    PlannerPlacement Placement,
    float MapWidthPx,
    float PlannerWidthPx,
    float RightChipsWidthPx,
    float LegendWidthPx)
{
    /// <summary>UI.md 4.1: the planner panel is 300 wide.</summary>
    public const float PlannerPanelPx = 300;

    /// <summary>Narrowest map that still shows the disc, both chip columns and the controls.</summary>
    public const float MapMinWidthPx = 400;

    public const float MapMinHeightPx = 240;

    /// <summary>The planner header (title and mode aside) and its footer (Execute, Clear): never scrolled away.</summary>
    public const float PlannerHeaderPx = 40;

    public const float PlannerFooterPx = 52;

    /// <summary>The held-order line above Execute (two lines of Small text), shown while a burn order waits for its mark.</summary>
    public const float PlannerHeldOrderPx = 44;

    /// <summary>The scrolled planner body never gets less than this.</summary>
    public const float PlannerBodyMinPx = 120;

    public const float GutterPx = 12;

    /// <summary>The left chip column (orbit, nav, altitude note), as drawn in G4-01.</summary>
    public const float InfoChipsWidthPx = 308;

    public const float RightChipsMaxPx = 320;

    public const float RightChipsMinPx = 160;

    public const float FocusButtonWidthPx = 144;

    public const float LegendMaxPx = 520;

    public const float LegendMinPx = 220;

    /// <summary>The drawer toggle at the map's top right (compact only); the right chips start below it.</summary>
    public const float DrawerToggleHeightPx = 32;

    public static float PlannerMinHeightPx => PlannerHeaderPx + PlannerBodyMinPx + PlannerHeldOrderPx + PlannerFooterPx;

    public static OrbitMapLayout For(float widthPx, float heightPx)
    {
        _ = heightPx;
        var docked = widthPx >= MapMinWidthPx + PlannerPanelPx;
        var mapWidth = docked ? widthPx - PlannerPanelPx : widthPx;
        var rightChips = Math.Clamp(mapWidth - (2 * GutterPx) - InfoChipsWidthPx - 8, RightChipsMinPx, RightChipsMaxPx);
        var legend = Math.Clamp(mapWidth - (2 * GutterPx) - FocusButtonWidthPx - 8, LegendMinPx, LegendMaxPx);
        return new OrbitMapLayout(
            docked ? PlannerPlacement.Docked : PlannerPlacement.Drawer,
            mapWidth,
            Math.Min(PlannerPanelPx, widthPx),
            rightChips,
            legend);
    }
}
