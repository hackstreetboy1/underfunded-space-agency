namespace Usa.Game.UI.Console.Binding;

/// <summary>
/// The UI scale applied on top of <c>canvas_items</c> stretch (base 1920 × 1080, <c>expand</c>), UI.md 3 and Gate 1 decision 1:
/// <list type="bullet">
/// <item>Body text (14 px at the base size) never renders below <see cref="MinBodyPx"/>: the factor is at least
/// 12 / (14 × the stretch), so 1366 × 768, 1440 × 900 and 1600 × 900 windows get just enough (review R3-09).</item>
/// <item>Windows narrower than <see cref="SmallWindowWidthPx"/> keep the approved <see cref="SmallWindowScale"/>, so 1280 × 720
/// gets a 0.889 factor, a 1440 × 810 logical canvas and the compact console.</item>
/// </list>
/// The player's own UI scale wins when it is larger.
/// </summary>
public static class UiScaleRule
{
    public const int SmallWindowWidthPx = 1366;

    public const float SmallWindowScale = 4f / 3f;

    public const float BaseWidthPx = 1920f;

    public const float BaseHeightPx = 1080f;

    public const float BodyPx = 14f;

    public const float MinBodyPx = 12f;

    /// <summary>For a 16:9 window of this width.</summary>
    /// <param name="userScale">The settings' UI scale (0.8–1.5).</param>
    /// <returns>The <c>content_scale_factor</c> to apply.</returns>
    public static float Effective(int windowWidthPx, float userScale) => Effective(windowWidthPx, windowWidthPx * 9 / 16, userScale);

    /// <param name="userScale">The settings' UI scale (0.8–1.5).</param>
    /// <returns>The <c>content_scale_factor</c> to apply.</returns>
    public static float Effective(int windowWidthPx, int windowHeightPx, float userScale)
    {
        var scale = float.IsFinite(userScale) && userScale > 0 ? userScale : 1f;
        if (windowWidthPx <= 0 || windowHeightPx <= 0) return scale;
        if (windowWidthPx < SmallWindowWidthPx) scale = Math.Max(scale, SmallWindowScale);
        var stretch = Math.Min(windowWidthPx / BaseWidthPx, windowHeightPx / BaseHeightPx);
        return Math.Max(scale, MinBodyPx / (BodyPx * stretch));
    }

    /// <summary>Body text size on screen for a window and factor (the stretch times the factor times 14 px).</summary>
    public static float BodyOnScreenPx(int windowWidthPx, int windowHeightPx, float factor) =>
        BodyPx * ScreenPixelsPerUnit(windowWidthPx, windowHeightPx, factor);

    /// <summary>Screen pixels per UI unit: the <c>canvas_items</c> stretch (the smaller side against 1920 × 1080) times the factor.</summary>
    public static float ScreenPixelsPerUnit(int windowWidthPx, int windowHeightPx, float factor) =>
        Math.Min(windowWidthPx / BaseWidthPx, windowHeightPx / BaseHeightPx) * factor;

    /// <summary>
    /// The canvas the UI lays out in (<c>expand</c> aspect): the window over <see cref="ScreenPixelsPerUnit"/>, so a 16:9 window
    /// of any size gets 1920 × 1080 at factor 1 and an ultrawide one gets extra width.
    /// </summary>
    public static (float Width, float Height) LogicalCanvas(int windowWidthPx, int windowHeightPx, float factor)
    {
        var perUnit = ScreenPixelsPerUnit(windowWidthPx, windowHeightPx, factor);
        return perUnit > 0 ? (windowWidthPx / perUnit, windowHeightPx / perUnit) : (BaseWidthPx, BaseHeightPx);
    }

    public const float MinRenderDensity = 0.5f;

    /// <summary>Caps a 3D view's render size on 5K and larger screens.</summary>
    public const float MaxRenderDensity = 3f;

    /// <summary>
    /// Render-target size for a 3D view laid out at a logical size, at <paramref name="screenPixelsPerUnit"/> (clamped to
    /// <see cref="MinRenderDensity"/>–<see cref="MaxRenderDensity"/>), so it renders one texel per screen pixel instead of
    /// being stretched up from the 1920 × 1080 layout size. At least 1 × 1.
    /// </summary>
    public static (int Width, int Height) RenderPixels(float logicalWidth, float logicalHeight, float screenPixelsPerUnit)
    {
        var density = float.IsFinite(screenPixelsPerUnit) ? Math.Clamp(screenPixelsPerUnit, MinRenderDensity, MaxRenderDensity) : 1f;
        return (Math.Max(1, (int)MathF.Round(logicalWidth * density)), Math.Max(1, (int)MathF.Round(logicalHeight * density)));
    }
}
