using System.Globalization;
using Usa.Game.Services.Settings;

namespace Usa.Game.UI.Frontend.Model;

/// <summary>Value texts and slider steps of the settings rows (G6-05): "80 %", "22 px", UI scale in 5 % steps.</summary>
public static class SettingsText
{
    public const int UiScaleStepPercent = 5;

    /// <summary>0..1 as a whole percentage, "80 %".</summary>
    public static string Percent(float linear) =>
        string.Create(CultureInfo.InvariantCulture, $"{PercentOf(linear)} %");

    public static int PercentOf(float linear) =>
        float.IsFinite(linear) ? (int)Math.Round(Math.Clamp(linear, 0f, 1f) * 100f, MidpointRounding.AwayFromZero) : 0;

    /// <summary>Slider percent back to linear gain.</summary>
    public static float LinearOf(double percent) => (float)Math.Clamp(percent, 0, 100) / 100f;

    public static string Px(int pixels) => string.Create(CultureInfo.InvariantCulture, $"{pixels} px");

    /// <summary>UI scale factor to a slider percent on the 5 % grid, inside 80–150.</summary>
    public static int UiScalePercent(float scale)
    {
        var clamped = float.IsFinite(scale) ? Math.Clamp(scale, DisplaySettings.MinUiScale, DisplaySettings.MaxUiScale) : 1f;
        var percent = (int)Math.Round(clamped * 100f / UiScaleStepPercent, MidpointRounding.AwayFromZero) * UiScaleStepPercent;
        return Math.Clamp(percent, (int)Math.Round(DisplaySettings.MinUiScale * 100f), (int)Math.Round(DisplaySettings.MaxUiScale * 100f));
    }

    public static float UiScaleOf(double percent) =>
        UiScalePercent((float)(percent / 100.0)) / 100f;

    public static string UiScale(float scale) => string.Create(CultureInfo.InvariantCulture, $"{UiScalePercent(scale)} %");

    public static int CaptionSize(double value) =>
        Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), CaptionSettings.MinSizePx, CaptionSettings.MaxSizePx);

    /// <summary>The <see cref="FrontendTextDefaults"/> key naming a window mode.</summary>
    public static string WindowModeKey(WindowMode mode) => mode switch
    {
        WindowMode.Fullscreen => "settings.windowModeFullscreen",
        WindowMode.ExclusiveFullscreen => "settings.windowModeExclusive",
        _ => "settings.windowModeWindowed",
    };
}
