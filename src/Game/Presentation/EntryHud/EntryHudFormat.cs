using System.Globalization;

namespace Usa.Game.Presentation.EntryHud;

/// <summary>
/// HUD number formatting (UI 6.2), invariant culture: SI units always; 0 dp at magnitude 100 and above, 1 dp below;
/// integer percentages; the typographic minus (U+2212); relative times as <c>in 12 min</c>.
/// </summary>
public static class EntryHudFormat
{
    public const string Placeholder = "—";
    public const string Minus = "−";
    public const string Approx = "≈ ";

    /// <summary>En dash between the ends of a range (UI 6.2).</summary>
    public const string RangeDash = "–";

    public const string StaleWord = "STALE";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary><c>212</c>, <c>42.5</c>, <c>−1.6</c>.</summary>
    public static string Number(double value)
    {
        if (!double.IsFinite(value)) return Placeholder;
        var rounded = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        var text = Math.Abs(rounded) >= 100
            ? Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", Invariant)
            : rounded.ToString("0.0", Invariant);
        return WithMinus(text);
    }

    /// <summary>Altitude in kilometres: <c>120 km</c>, <c>84.2 km</c>, <c>3.2 km</c>.</summary>
    public static string AltitudeKm(double altitudeM) => WithUnit(Number(altitudeM / 1000.0), "km");

    public static string DistanceKm(double km) => WithUnit(Number(km), "km");

    public static string Speed(double metresPerSecond) => WithUnit(Number(metresPerSecond), "m/s");

    /// <summary>Always one decimal, since the corridor is narrow: <c>−1.6°</c>, <c>0.0°</c>.</summary>
    public static string AngleDeg(double degrees)
    {
        var number = AngleNumber(degrees);
        return number == Placeholder ? number : number + "°";
    }

    /// <summary>The number of <see cref="AngleDeg"/> without the degree sign.</summary>
    public static string AngleNumber(double degrees) =>
        double.IsFinite(degrees) ? WithMinus(Math.Round(degrees, 1, MidpointRounding.AwayFromZero).ToString("0.0", Invariant)) : Placeholder;

    /// <summary>The number of <see cref="Percent01"/> without the percent sign.</summary>
    public static string PercentNumber01(double fraction) =>
        double.IsFinite(fraction) ? WithMinus(Math.Round(fraction * 100.0, MidpointRounding.AwayFromZero).ToString("0", Invariant)) : Placeholder;

    /// <summary>MET as <c>MET 07:52:31</c> (UI 6.2: HH:MM:SS; hours keep counting past 24).</summary>
    public static string Met(double metSeconds)
    {
        if (!double.IsFinite(metSeconds)) return Placeholder;
        var whole = (long)Math.Floor(Math.Max(0, metSeconds));
        return string.Create(Invariant, $"MET {whole / 3600:00}:{whole / 60 % 60:00}:{whole % 60:00}");
    }

    /// <summary>An event time without the prefix, <c>07:43:40</c>.</summary>
    public static string Clock(double metSeconds)
    {
        if (!double.IsFinite(metSeconds)) return Placeholder;
        var whole = (long)Math.Floor(Math.Max(0, metSeconds));
        return string.Create(Invariant, $"{whole / 3600:00}:{whole / 60 % 60:00}:{whole % 60:00}");
    }

    /// <summary>A predicted time to the minute, <c>08:25</c>.</summary>
    public static string ClockShort(double metSeconds)
    {
        if (!double.IsFinite(metSeconds)) return Placeholder;
        var whole = (long)Math.Floor(Math.Max(0, metSeconds));
        return string.Create(Invariant, $"{whole / 3600:00}:{whole / 60 % 60:00}");
    }

    /// <summary>Longitude magnitude in whole degrees for the splash point (<c>135</c>), from any angle in degrees.</summary>
    public static (string Degrees, bool East) Longitude(double degrees)
    {
        var wrapped = ((degrees % 360) + 540) % 360 - 180;
        var rounded = Math.Round(wrapped, MidpointRounding.AwayFromZero);
        if (rounded == -180) rounded = 180;
        return (Math.Abs(rounded).ToString("0", Invariant), rounded >= 0);
    }

    /// <summary>The stale badge with the reading's age: <c>STALE 8s</c>, <c>STALE 12m</c>, <c>STALE 2h</c>; just <c>STALE</c> without an age.</summary>
    public static string StaleBadge(double ageSeconds)
    {
        if (!double.IsFinite(ageSeconds) || ageSeconds < 0) return StaleWord;
        var whole = (long)Math.Floor(ageSeconds);
        if (whole < 60) return string.Create(Invariant, $"{StaleWord} {whole}s");
        if (whole < 3600) return string.Create(Invariant, $"{StaleWord} {whole / 60}m");
        return string.Create(Invariant, $"{StaleWord} {whole / 3600}h");
    }

    public static string GLoad(double g) => WithUnit(Number(g), "g");

    /// <summary>A g value on a gauge mark: <c>6 g</c>, <c>7.5 g</c>.</summary>
    public static string GMark(double g) =>
        double.IsFinite(g)
            ? WithMinus(Math.Round(g, 1, MidpointRounding.AwayFromZero).ToString("0.#", Invariant)) + " g"
            : Placeholder;

    /// <summary>Integer percent of a 0..1 fraction: <c>62%</c>.</summary>
    public static string Percent01(double fraction) => Percent(fraction * 100.0);

    public static string Percent(double percent)
    {
        if (!double.IsFinite(percent)) return Placeholder;
        return WithMinus(Math.Round(percent, MidpointRounding.AwayFromZero).ToString("0", Invariant)) + "%";
    }

    /// <summary>
    /// Countdown to an event: <c>now</c>, <c>in 45 s</c>, <c>in 1 min 40 s</c> (seconds kept under 10 min, where they
    /// matter for a deploy), <c>in 12 min</c>, <c>in 2 h 05 min</c>. Seconds round up so a pending event never reads 0 s.
    /// </summary>
    public static string Countdown(double seconds)
    {
        if (!double.IsFinite(seconds)) return Placeholder;
        if (seconds <= 0.5) return "now";
        // The tolerance keeps floating-point noise (100.000000001 s) from adding a second.
        var whole = (long)Math.Ceiling(seconds - 1e-6);
        if (whole < 60) return string.Create(Invariant, $"in {whole} s");
        if (whole < 600) return string.Create(Invariant, $"in {whole / 60} min {whole % 60:00} s");
        return "in " + Span(seconds);
    }

    /// <summary>An estimated wait: <c>≈ 30 min</c>, <c>≈ 2.5 h</c>, <c>≈ 12 h</c>.</summary>
    public static string WaitEstimate(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) return Placeholder;
        if (seconds < 3570) return Approx + Span(seconds);
        var hours = seconds / 3600.0;
        var number = hours >= 10
            ? Math.Round(hours, MidpointRounding.AwayFromZero).ToString("0", Invariant)
            : Math.Round(hours, 1, MidpointRounding.AwayFromZero).ToString("0.#", Invariant);
        return Approx + number + " h";
    }

    /// <summary><c>12 min</c>, <c>2 h 05 min</c>, <c>3 h</c>; at least one minute.</summary>
    public static string Span(double seconds)
    {
        var minutes = Math.Max(1, (long)Math.Round(Math.Max(0, seconds) / 60.0, MidpointRounding.AwayFromZero));
        if (minutes < 60) return string.Create(Invariant, $"{minutes} min");
        return minutes % 60 == 0
            ? string.Create(Invariant, $"{minutes / 60} h")
            : string.Create(Invariant, $"{minutes / 60} h {minutes % 60:00} min");
    }

    // Also turns "-0.0" (a tiny negative rounded away) into "0.0".
    private static string WithMinus(string text)
    {
        if (!text.StartsWith('-')) return text;
        var magnitude = text[1..];
        foreach (var c in magnitude)
        {
            if (c is not ('0' or '.')) return Minus + magnitude;
        }
        return magnitude;
    }

    private static string WithUnit(string number, string unit) => number == Placeholder ? number : number + " " + unit;
}
