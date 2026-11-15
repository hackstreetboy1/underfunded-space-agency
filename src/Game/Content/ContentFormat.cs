using System.Globalization;

namespace Usa.Game.Content;

/// <summary>Invariant-culture number and time formats used to fill content placeholders (UI.md 6.2).</summary>
public static class ContentFormat
{
    /// <summary>0 dp, midpoint away from zero (the same rounding as the score), never "-0".</summary>
    public static string WholeNumber(double value) => Rounded(value, 0, "0");

    public static string OneDecimal(double value) => Rounded(value, 1, "0.0");

    /// <summary>Mission elapsed time as <c>HH:MM:SS</c>; hours grow past 99 rather than wrapping.</summary>
    public static string Met(double metS)
    {
        if (!double.IsFinite(metS)) return TextTemplate.MissingValue;
        var total = (long)Math.Floor(Math.Max(0.0, metS));
        var hours = total / 3600;
        var minutes = total / 60 % 60;
        var seconds = total % 60;
        return string.Create(CultureInfo.InvariantCulture, $"{hours:00}:{minutes:00}:{seconds:00}");
    }

    private static string Rounded(double value, int decimals, string format)
    {
        if (!double.IsFinite(value)) return TextTemplate.MissingValue;
        var rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        if (rounded == 0) rounded = 0; // folds -0 into 0
        return rounded.ToString(format, CultureInfo.InvariantCulture);
    }
}
