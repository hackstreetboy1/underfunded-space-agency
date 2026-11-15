using Godot;

namespace Usa.Game.UI.Console;

/// <summary>Small presentation steps the console views repeat.</summary>
internal static class ControlExtensions
{
    private static readonly StringName FontColor = "font_color";

    /// <summary>Switches the theme type variation only when it differs, so unchanged rows do not re-theme.</summary>
    public static void SetVariation(this Control control, string variation)
    {
        if (control.ThemeTypeVariation.ToString() != variation) control.ThemeTypeVariation = variation;
    }

    public static void SetFontColour(this Label label, Color colour) => label.AddThemeColorOverride(FontColor, colour);

    /// <summary>Returns the label to its theme colour.</summary>
    public static void ClearFontColour(this Label label) => label.RemoveThemeColorOverride(FontColor);

    /// <summary>Sets the text and hides the label while it is empty.</summary>
    public static void ShowText(this Label label, string? text)
    {
        label.Text = text ?? string.Empty;
        label.Visible = !string.IsNullOrEmpty(text);
    }
}
