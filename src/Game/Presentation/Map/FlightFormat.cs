using System.Globalization;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Map;

/// <summary>
/// Number formatting for flight readouts (UI.md 6.2), following the console's <c>ReadoutFormatter</c> rules: SI units
/// always, mass 0 dp at or above 100 and 1 dp below, integer percentages with no space (<c>87%</c>), a true minus sign
/// (U+2212), the placeholder for any non-finite value, and a signed MET. Words (relative times) are in
/// <see cref="MapText"/>.
/// </summary>
public static class FlightFormat
{
    public const string Minus = "−";
    public const string EnDash = "–";
    public const string Placeholder = "—";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>Altitudes: whole kilometres (one decimal below 10 km).</summary>
    public static string AltitudeKm(double km) => WithUnit(km, Math.Abs(km) >= 10 ? "0" : "0.0", " km");

    public static string Speed(double metresPerSecond) =>
        WithUnit(metresPerSecond, Math.Abs(metresPerSecond) >= 100 ? "0" : "0.0", " m/s");

    public static string Mass(double kg) => WithUnit(kg, Math.Abs(kg) >= 100 ? "0" : "0.0", " kg");

    public static string Percent(double fraction) =>
        double.IsFinite(fraction) ? WithUnit(Math.Round(fraction * 100, MidpointRounding.AwayFromZero), "0", "%") : Placeholder;

    public static string AngleDeg(double degrees) => WithUnit(degrees, "0.0", "°");

    public static string GLoad(double g) => WithUnit(g, "0.0", " g");

    public static string Hours(double hours) => WithUnit(hours, Math.Abs(hours) >= 10 ? "0" : "0.#", " h");

    /// <summary>A burn or wait length: <c>16.0 s</c>, <c>2 min 05 s</c>.</summary>
    public static string Duration(double seconds)
    {
        if (!double.IsFinite(seconds)) return Placeholder;
        // Round first, so 59.96 s reads "1 min 00 s" and never "60.0 s".
        var tenths = Math.Round(seconds, 1, MidpointRounding.AwayFromZero);
        if (Math.Abs(tenths) < 60) return WithUnit(tenths, "0.0", " s");
        var whole = (long)Math.Round(Math.Abs(seconds), MidpointRounding.AwayFromZero);
        var sign = seconds < 0 ? Minus : string.Empty;
        return sign + (whole < 3600
            ? string.Create(Invariant, $"{whole / 60} min {whole % 60:00} s")
            : string.Create(Invariant, $"{whole / 3600} h {whole / 60 % 60:00} min"));
    }

    /// <summary>A range with an en dash: <c>7.5–9 h</c>.</summary>
    public static string Range(string low, string high, string unit) => $"{low}{EnDash}{high} {unit}";

    /// <summary><c>40–60 kg</c>; a single value when both ends format the same.</summary>
    public static string MassRange(double lowKg, double highKg) =>
        NumberRange(lowKg, highKg, Math.Abs(highKg) >= 100 ? "0" : "0.0", "kg");

    /// <summary><c>80–120 m/s</c>; a single value when both ends format the same.</summary>
    public static string SpeedRange(double lowMps, double highMps) =>
        NumberRange(lowMps, highMps, Math.Abs(highMps) >= 100 ? "0" : "0.0", "m/s");

    /// <summary>MET clock <c>HH:MM:SS</c>, signed (<c>−00:00:01</c> before lift-off); hours keep counting past 24.</summary>
    public static string Met(MissionTime time)
    {
        var milliseconds = time.Milliseconds;
        var sign = milliseconds < 0 ? Minus : string.Empty;
        var totalSeconds = Math.Abs(milliseconds) / 1000;
        return sign + string.Create(Invariant, $"{totalSeconds / 3600:00}:{totalSeconds / 60 % 60:00}:{totalSeconds % 60:00}");
    }

    /// <summary>An orbital period as total minutes and seconds, <c>90:19</c> (seconds rounded).</summary>
    public static string Period(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) return Placeholder;
        var whole = (long)Math.Round(seconds, MidpointRounding.AwayFromZero);
        return string.Create(Invariant, $"{whole / 60}:{whole % 60:00}");
    }

    /// <summary>MET for meta lines, <c>HH:MM</c>.</summary>
    public static string MetShort(MissionTime time)
    {
        var milliseconds = time.Milliseconds;
        var sign = milliseconds < 0 ? Minus : string.Empty;
        var totalSeconds = Math.Abs(milliseconds) / 1000;
        return sign + string.Create(Invariant, $"{totalSeconds / 3600:00}:{totalSeconds / 60 % 60:00}");
    }

    /// <summary>Compact age for badges: <c>45s</c>, <c>12m</c>, <c>2h</c>, <c>1h 05m</c>.</summary>
    public static string Age(double seconds)
    {
        var totalSeconds = double.IsFinite(seconds) ? Math.Max(0, (long)seconds) : 0;
        if (totalSeconds < 60) return string.Create(Invariant, $"{totalSeconds}s");
        if (totalSeconds < 3600) return string.Create(Invariant, $"{totalSeconds / 60}m");
        var minutes = totalSeconds / 60 % 60;
        return minutes == 0
            ? string.Create(Invariant, $"{totalSeconds / 3600}h")
            : string.Create(Invariant, $"{totalSeconds / 3600}h {minutes:00}m");
    }

    private static string NumberRange(double low, double high, string format, string unit)
    {
        if (!double.IsFinite(low) || !double.IsFinite(high)) return Placeholder;
        var lowText = WithUnit(low, format, string.Empty);
        var highText = WithUnit(high, format, string.Empty);
        return lowText == highText ? $"{highText} {unit}" : Range(lowText, highText, unit);
    }

    private static string WithUnit(double value, string format, string unit)
    {
        if (!double.IsFinite(value)) return Placeholder;
        var text = Math.Abs(value).ToString(format, Invariant);
        var isZero = double.Parse(text, Invariant) == 0;
        return (value < 0 && !isZero ? Minus + text : text) + unit;
    }
}
