using Godot;

namespace Usa.Game.UI.Console;

/// <summary>
/// Style boxes the theme has no type for (transcript bubbles, the caption backing), built from the active token set so
/// they follow the default and high-contrast modes.
/// </summary>
internal static class ConsoleStyles
{
    /// <summary>A filled box with the control radius and the 8 / 6 px bubble padding (Figma G1-03 node 13:370).</summary>
    public static StyleBoxFlat Fill(ConsolePalette palette, Color colour)
    {
        var tokens = palette.Tokens;
        var box = new StyleBoxFlat { BgColor = colour };
        box.SetCornerRadiusAll(tokens.RadiusControlPx);
        box.ContentMarginLeft = tokens.Space8Px + 2;
        box.ContentMarginRight = tokens.Space8Px + 2;
        box.ContentMarginTop = tokens.Space4Px + 2;
        box.ContentMarginBottom = tokens.Space4Px + 2;
        return box;
    }

    /// <summary>The caption backing: <c>bg.0</c> at the settings' opacity (UI.md 2.4, default 70 %).</summary>
    public static StyleBoxFlat CaptionBacking(ConsolePalette palette, float opacity)
    {
        var colour = palette.Tokens.ColorBg0;
        colour.A = Math.Clamp(opacity, 0f, 1f);
        return Fill(palette, colour);
    }
}
