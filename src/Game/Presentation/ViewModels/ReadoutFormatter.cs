using System.Globalization;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.ViewModels;

/// <summary>
/// Number and readout formatting per UI.md 6.2 and 2.3, always in the invariant culture.
/// </summary>
/// <remarks>
/// The UI.md mass rule (0 dp at 100 and above, 1 dp below) is applied to every unit except <c>%</c>
/// (integers); range end points drop a trailing <c>.0</c> so ranges read <c>7.5–9 h</c> as in the spec.
/// Negative numbers use the typographic minus (U+2212).
/// </remarks>
public static class ReadoutFormatter
{
    public const string Placeholder = "—";
    public const string Minus = "−";
    public const string RangeDash = "–";
    public const string MetaSeparator = " · ";

    private const double WholeNumberThreshold = 100;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary><c>212</c>, <c>42.5</c>, <c>−4.2</c>: 0 dp at magnitude 100 and above, otherwise 1 dp.</summary>
    public static string FormatNumber(double value) => FormatNumber(value, trimZeroFraction: false);

    /// <summary>Integer percent, <c>87%</c>.</summary>
    public static string FormatPercent(double percent)
    {
        if (!double.IsFinite(percent)) return Placeholder;
        return WithMinus(Math.Round(percent, MidpointRounding.AwayFromZero).ToString("0", Invariant)) + "%";
    }

    /// <summary>Value with its unit: <c>212 kg</c>, <c>87%</c>, <c>21.5 °C</c>.</summary>
    public static string FormatQuantity(double value, string unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (!double.IsFinite(value)) return Placeholder;
        if (unit == "%") return FormatPercent(value);
        return WithUnit(FormatNumber(value), unit);
    }

    /// <summary>Signed rate per hour: <c>−4.2 kg/h</c>, <c>+0.3 kWh/h</c>; zero carries no sign.</summary>
    public static string FormatRatePerHour(double perHour, string unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (!double.IsFinite(perHour)) return Placeholder;
        var number = FormatNumber(perHour);
        if (perHour > 0 && !IsZeroText(number)) number = "+" + number;
        return unit.Length == 0 ? number + "/h" : $"{number} {unit}/h";
    }

    /// <summary>Trend arrow and signed rate: <c>▲ +1.5 kg/h</c>, <c>▼ −4.2 kg/h</c>; no arrow when it rounds to zero.</summary>
    public static string FormatTrend(double perHour, string unit)
    {
        var rate = FormatRatePerHour(perHour, unit);
        if (!double.IsFinite(perHour) || IsZeroText(FormatNumber(perHour))) return rate;
        return (perHour > 0 ? "▲ " : "▼ ") + rate;
    }

    /// <summary>Estimated range with an en dash: <c>7.5–9 h</c>, <c>40–60%</c>.</summary>
    public static string FormatRange(double min, double max, string unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (!double.IsFinite(min) || !double.IsFinite(max)) return Placeholder;
        if (unit == "%") return $"{FormatPercent(min).TrimEnd('%')}{RangeDash}{FormatPercent(max)}";
        return WithUnit(FormatNumber(min, trimZeroFraction: true) + RangeDash + FormatNumber(max, trimZeroFraction: true), unit);
    }

    /// <summary>MET clock <c>HH:MM:SS</c>; hours keep counting past 24.</summary>
    public static string FormatMet(MissionTime met)
    {
        var (sign, totalSeconds) = SplitSeconds(met.Milliseconds);
        return sign + string.Create(Invariant, $"{totalSeconds / 3600:00}:{totalSeconds / 60 % 60:00}:{totalSeconds % 60:00}");
    }

    /// <summary>MET for meta lines, <c>HH:MM</c> (UI.md 2.3: <c>Okafor · MET 06:58</c>).</summary>
    public static string FormatMetShort(MissionTime met)
    {
        var (sign, totalSeconds) = SplitSeconds(met.Milliseconds);
        return sign + string.Create(Invariant, $"{totalSeconds / 3600:00}:{totalSeconds / 60 % 60:00}");
    }

    /// <summary>Relative MET: <c>in 12 min</c>, <c>45 s ago</c>, <c>in 2 h 5 min</c>, <c>now</c>.</summary>
    public static string FormatRelative(MissionTime now, MissionTime at)
    {
        var deltaMs = at.Milliseconds - now.Milliseconds;
        if (Math.Abs(deltaMs) < 1000) return "now";
        var span = SpanWords(Math.Abs(deltaMs) / 1000);
        return deltaMs > 0 ? "in " + span : span + " ago";
    }

    /// <summary>Compact age for badges: <c>45s</c>, <c>12m</c>, <c>2h</c>, <c>1h 05m</c>.</summary>
    public static string FormatAge(TimeSpan age)
    {
        var totalSeconds = Math.Max(0, (long)age.TotalSeconds);
        if (totalSeconds < 60) return string.Create(Invariant, $"{totalSeconds}s");
        if (totalSeconds < 3600) return string.Create(Invariant, $"{totalSeconds / 60}m");
        var minutes = totalSeconds / 60 % 60;
        return minutes == 0
            ? string.Create(Invariant, $"{totalSeconds / 3600}h")
            : string.Create(Invariant, $"{totalSeconds / 3600}h {minutes:00}m");
    }

    /// <summary>Time-compression indicator, <c>240×</c>.</summary>
    public static string FormatCompressionRate(double rate) =>
        double.IsFinite(rate) && rate > 0
            ? Math.Max(1, Math.Round(rate, MidpointRounding.AwayFromZero)).ToString("0", Invariant) + "×"
            : Placeholder;

    /// <param name="now">Current MET. With it a stale badge's age is derived from <see cref="ReadoutSnapshot.ObservedAt"/>,
    /// so it keeps counting between pushes; without it <see cref="ReadoutSnapshot.StaleAge"/> is shown as given.</param>
    public static ReadoutText Format(ReadoutSnapshot snapshot, MissionTime? now = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var unit = snapshot.Unit;
        var meta = Meta(snapshot.Source, snapshot.ObservedAt);
        var trend = snapshot.TrendPerHour is { } perHour ? FormatTrend(perHour, unit) : null;

        return snapshot.Knowledge switch
        {
            ReadoutKnowledge.Known => new ReadoutText(Quantity(snapshot.Value, unit), null, meta, null, trend, Dimmed: false, CanAsk: false),
            ReadoutKnowledge.Stale => new ReadoutText(
                Quantity(snapshot.Value, unit), null, meta,
                StaleAge(snapshot, now) is { } age ? "STALE " + FormatAge(age) : "STALE",
                trend, Dimmed: true, CanAsk: false),
            ReadoutKnowledge.Estimated => new ReadoutText(Estimate(snapshot), null, meta, "EST", trend, Dimmed: false, CanAsk: false),
            ReadoutKnowledge.Conflict => new ReadoutText(
                Quantity(snapshot.Value, unit), Quantity(snapshot.ConflictValue, unit), meta, "⇄ CONFLICT", trend, Dimmed: false, CanAsk: false)
            {
                SecondaryMeta = Meta(snapshot.ConflictSource, snapshot.ConflictObservedAt),
            },
            ReadoutKnowledge.GroundBand => new ReadoutText(
                string.IsNullOrEmpty(snapshot.GroundBand) ? Placeholder : snapshot.GroundBand, null, meta, "GND", null, Dimmed: false, CanAsk: false),
            _ => new ReadoutText(Placeholder, null, null, "UNKNOWN", null, Dimmed: false, CanAsk: true),
        };
    }

    private static string Estimate(ReadoutSnapshot snapshot)
    {
        if (snapshot.RangeMin is { } min && snapshot.RangeMax is { } max)
            return "≈" + FormatRange(min, max, snapshot.Unit);
        return snapshot.Value is { } value ? "≈" + FormatQuantity(value, snapshot.Unit) : Placeholder;
    }

    private static string Quantity(double? value, string unit) => value is { } v ? FormatQuantity(v, unit) : Placeholder;

    private static TimeSpan? StaleAge(ReadoutSnapshot snapshot, MissionTime? now) =>
        now is { } current && snapshot.ObservedAt is { } observedAt
            ? TimeSpan.FromMilliseconds(Math.Max(0, current.Milliseconds - observedAt.Milliseconds))
            : snapshot.StaleAge;

    private static string? Meta(string? source, MissionTime? observedAt)
    {
        var hasSource = !string.IsNullOrWhiteSpace(source);
        var met = observedAt is { } at ? "MET " + FormatMetShort(at) : null;
        if (hasSource && met is not null) return source + MetaSeparator + met;
        return hasSource ? source : met;
    }

    private static string FormatNumber(double value, bool trimZeroFraction)
    {
        if (!double.IsFinite(value)) return Placeholder;
        var rounded = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        var text = Math.Abs(rounded) >= WholeNumberThreshold
            ? Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", Invariant)
            : rounded.ToString(trimZeroFraction ? "0.#" : "0.0", Invariant);
        return WithMinus(text);
    }

    // Also turns "-0.0" (a tiny negative rounded away) into "0.0".
    private static string WithMinus(string text)
    {
        if (!text.StartsWith('-')) return text;
        var magnitude = text[1..];
        return IsZeroText(magnitude) ? magnitude : Minus + magnitude;
    }

    private static bool IsZeroText(string number)
    {
        foreach (var c in number)
        {
            if (c is not ('0' or '.')) return false;
        }
        return true;
    }

    private static string WithUnit(string number, string unit) => unit.Length == 0 ? number : number + " " + unit;

    private static (string Sign, long TotalSeconds) SplitSeconds(long milliseconds) =>
        milliseconds < 0 ? (Minus, -milliseconds / 1000) : (string.Empty, milliseconds / 1000);

    private static string SpanWords(long totalSeconds)
    {
        if (totalSeconds < 60) return string.Create(Invariant, $"{totalSeconds} s");
        if (totalSeconds < 3600) return string.Create(Invariant, $"{totalSeconds / 60} min");
        var minutes = totalSeconds / 60 % 60;
        return minutes == 0
            ? string.Create(Invariant, $"{totalSeconds / 3600} h")
            : string.Create(Invariant, $"{totalSeconds / 3600} h {minutes} min");
    }
}
