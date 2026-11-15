namespace Usa.Game.Presentation.EntryHud;

/// <summary>
/// A <see cref="HudReadout"/> formatted per UI 2.3: Known <c>14.6 km</c> with meta <c>cabin altimeter · MET 07:52:31</c>;
/// Stale the same value with a <c>STALE 12s</c> badge; Estimated <c>≈ 62.0 ±2.0 km</c> with an <c>EST</c> badge;
/// Unknown a dash with an <c>UNKNOWN</c> badge.
/// </summary>
/// <param name="Badge">State badge word; empty for Known (the meta line carries it).</param>
/// <param name="Meta">Source and MET: <c>ground predict · MET 07:41:05</c>; just the source when the MET is not known.</param>
/// <param name="MetText"><c>MET 07:52:31</c>, or empty when not known.</param>
public sealed record HudReadoutRow(string Text, string Badge, string Meta, ReadoutState State, string Source, string MetText)
{
    public const string EstimatedBadge = "EST";
    public const string UnknownBadge = "UNKNOWN";

    /// <param name="number">Formats a number in display units without the unit (<c>62.0</c>); must be linear so band widths format alike.</param>
    /// <param name="unit">Appended to every number, with its own leading space when needed (<c>" km"</c>, <c>"°"</c>).</param>
    /// <param name="nowMetS">Current MET, for the age on a stale badge; null leaves the age out.</param>
    public static HudReadoutRow From(in HudReadout readout, Func<double, string> number, string unit, double? nowMetS)
    {
        ArgumentNullException.ThrowIfNull(number);
        ArgumentNullException.ThrowIfNull(unit);
        var source = readout.Source ?? string.Empty;
        var metText = readout.MetS is { } met && double.IsFinite(met) ? EntryHudFormat.Met(met) : string.Empty;
        var meta = metText.Length == 0 ? source : source.Length == 0 ? metText : source + " · " + metText;

        if (!readout.HasValue)
            return new HudReadoutRow(EntryHudFormat.Placeholder, UnknownBadge, meta, ReadoutState.Unknown, source, metText);

        switch (readout.State)
        {
            case ReadoutState.Estimated:
                return new HudReadoutRow(EstimateText(readout, number, unit), EstimatedBadge, meta, ReadoutState.Estimated, source, metText);
            case ReadoutState.Stale:
                var age = nowMetS is { } now && readout.MetS is { } at ? now - at : double.NaN;
                return new HudReadoutRow(number(readout.Value) + unit, EntryHudFormat.StaleBadge(age), meta, ReadoutState.Stale, source, metText);
            default:
                return new HudReadoutRow(number(readout.Value) + unit, string.Empty, meta, ReadoutState.Known, source, metText);
        }
    }

    /// <summary><c>≈ 62.0 km</c> without a band, <c>≈ 62.0 ±2.0 km</c> for a symmetric one, else <c>≈ 60.0–64.5 km</c>.</summary>
    private static string EstimateText(in HudReadout readout, Func<double, string> number, string unit)
    {
        var value = number(readout.Value);
        if (!double.IsFinite(readout.Low) || !double.IsFinite(readout.High)) return EntryHudFormat.Approx + value + unit;

        var above = number(readout.High - readout.Value);
        var below = number(readout.Value - readout.Low);
        if (above == below)
        {
            return above == number(0)
                ? EntryHudFormat.Approx + value + unit
                : EntryHudFormat.Approx + value + " ±" + above + unit;
        }
        return EntryHudFormat.Approx + number(readout.Low) + EntryHudFormat.RangeDash + number(readout.High) + unit;
    }
}
