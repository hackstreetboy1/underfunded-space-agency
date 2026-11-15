using System.Globalization;

namespace Usa.Game.UI.Frontend.Model;

/// <summary>Mission elapsed time as <c>HH:MM:SS</c> (UI.md 6.2); hours keep counting past 99.</summary>
public static class MetText
{
    public static string FromMilliseconds(long milliseconds)
    {
        var totalSeconds = Math.Max(milliseconds, 0) / 1000;
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds / 60 % 60;
        var seconds = totalSeconds % 60;
        return string.Create(CultureInfo.InvariantCulture, $"{hours:00}:{minutes:00}:{seconds:00}");
    }

    public static string FromSeconds(double seconds) =>
        double.IsFinite(seconds) ? FromMilliseconds((long)Math.Floor(seconds * 1000.0)) : FromMilliseconds(0);
}
