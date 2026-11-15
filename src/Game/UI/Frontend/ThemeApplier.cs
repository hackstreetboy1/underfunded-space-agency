using Godot;
using Usa.Game.Services.Settings;
using Usa.Game.UI.Console.Binding;
using Usa.Game.UI.Theme;

namespace Usa.Game.UI.Frontend;

/// <summary>
/// Applies the display settings to the root window: <c>UsaTheme.tres</c> or its high-contrast twin as the window theme
/// (so every scene, the console included, inherits it), the clear colour (<c>bg.0</c>), and the UI scale through
/// <c>content_scale_factor</c> with the approved 720p rule and the Body ≥ 12 px floor (<see cref="UiScaleRule"/>), which
/// overrides a lower player scale: at 1600×900 a 0.8 scale is raised so Body stays at 12 px.
/// </summary>
public static class ThemeApplier
{
    public static UiTokenSet TokensFor(DisplaySettings display)
    {
        ArgumentNullException.ThrowIfNull(display);
        return display.HighContrast ? UiTokens.HighContrast : UiTokens.Default;
    }

    /// <returns>The token set now in effect.</returns>
    public static UiTokenSet Apply(Window root, DisplaySettings display)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(display);
        var tokens = TokensFor(display);
        var path = display.HighContrast ? UiTokens.HighContrastThemePath : UiTokens.ThemePath;
        if (ResourceLoader.Exists(path) && ResourceLoader.Load<Godot.Theme>(path) is { } theme && root.Theme != theme) root.Theme = theme;
        RenderingServer.SetDefaultClearColor(tokens.ColorBg0);
        ApplyScale(root, display);
        return tokens;
    }

    public static void ApplyScale(Window root, DisplaySettings display)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(display);
        // The stretch follows the smaller side, so a window that is not 16:9 needs its real height for the Body floor.
        var factor = UiScaleRule.Effective(root.Size.X, root.Size.Y, display.UiScale);
        if (!Mathf.IsEqualApprox(root.ContentScaleFactor, factor)) root.ContentScaleFactor = factor;
    }
}
