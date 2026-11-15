using Usa.Game.Presentation.Console.Resources;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.UI.Theme;

namespace Usa.Game.UI.Console;

/// <summary>
/// The approved icon (<c>assets/ui/icons</c>, <see cref="UiIcons"/>) for each status shape the view models name, so the console
/// draws shapes instead of font glyphs that IBM Plex lacks (review R3-11). The word always stays beside the icon.
/// </summary>
public static class StatusIcons
{
    /// <returns>Null for <see cref="StatusShape.None"/> and for <see cref="StatusShape.HollowCircle"/>, which has no approved icon
    /// and is drawn as a ring (deceased, objective open, no plan).</returns>
    public static string? For(StatusShape shape) => shape switch
    {
        StatusShape.FilledCircle => UiIcons.StatusNominal,
        StatusShape.Triangle => UiIcons.StatusCaution,
        StatusShape.DashedTriangle => UiIcons.StatusSuspect,
        StatusShape.Octagon => UiIcons.StatusFailed,
        StatusShape.SlashedSquare => UiIcons.StatusIsolated,
        StatusShape.DashedCircle => UiIcons.StatusUnknown,
        StatusShape.Clock => UiIcons.StateStale,
        StatusShape.InfoMark => UiIcons.StateInfo,
        StatusShape.Approximate => UiIcons.StateEstimated,
        StatusShape.Exchange => UiIcons.StateConflict,
        StatusShape.GroundBand => UiIcons.StateGroundBand,
        _ => null,
    };

    /// <summary>The trend arrow's icon; null without a direction (steady or unknown).</summary>
    public static string? For(TrendDirection direction) => direction switch
    {
        TrendDirection.Rising => UiIcons.TrendUp,
        TrendDirection.Falling => UiIcons.TrendDown,
        _ => null,
    };

    /// <summary>A trend's text without its leading arrow (the icon replaces it): <c>−4.2 kg/h</c>.</summary>
    public static string TrendText(TrendIndicator trend)
    {
        ArgumentNullException.ThrowIfNull(trend);
        var text = trend.Text;
        return text.Length > 1 && text[0] is '▲' or '▼' ? text[1..].TrimStart() : text;
    }
}
