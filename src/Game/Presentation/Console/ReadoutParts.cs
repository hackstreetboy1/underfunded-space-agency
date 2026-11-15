using Usa.Game.Presentation.ViewModels;

namespace Usa.Game.Presentation.Console;

/// <summary>
/// A readout's number and unit as separate strings, for layouts that set the unit smaller beside the value (Figma G1-03
/// tiles and gauges: <c>212</c> <c>kg</c>). UI.md 6.2 "units always": the unit stays beside a placeholder too
/// (<c>—</c> <c>°C</c>), so an unknown value still says what it measures; a ground band is a word and has none.
/// Percentages follow the copy rule (<c>84 %</c>), integers as in <see cref="ReadoutFormatter.FormatPercent"/>.
/// </summary>
/// <param name="Value"><c>212</c>, <c>≈66–70</c>, <c>—</c>, <c>LOW</c>.</param>
/// <param name="Unit"><c>kg</c>; empty for unitless values and ground bands.</param>
/// <param name="SecondaryValue">The second value of a conflict, same unit; otherwise null.</param>
public sealed record ReadoutParts(string Value, string Unit, string? SecondaryValue = null)
{
    public const string Percent = "%";

    public static ReadoutParts Empty { get; } = new(ReadoutFormatter.Placeholder, string.Empty);

    /// <summary>Value and unit joined with a space, <c>84 %</c>, <c>— °C</c>.</summary>
    public string Joined => Unit.Length == 0 ? Value : Value + " " + Unit;

    public static ReadoutParts From(ReadoutSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var unit = snapshot.Unit;
        return snapshot.Knowledge switch
        {
            ReadoutKnowledge.Known or ReadoutKnowledge.Stale => new(Number(snapshot.Value, unit), unit),
            ReadoutKnowledge.Conflict => new(Number(snapshot.Value, unit), unit, Number(snapshot.ConflictValue, unit)),
            ReadoutKnowledge.Estimated => new(Estimate(snapshot), unit),
            ReadoutKnowledge.GroundBand => new(string.IsNullOrEmpty(snapshot.GroundBand) ? ReadoutFormatter.Placeholder : snapshot.GroundBand, string.Empty),
            _ => new(ReadoutFormatter.Placeholder, unit),
        };
    }

    /// <summary>A number formatted for its unit without the unit: <c>212</c>, <c>6.1</c>, <c>84</c> for percent.</summary>
    public static string Number(double? value, string unit)
    {
        if (value is not { } v || !double.IsFinite(v)) return ReadoutFormatter.Placeholder;
        return unit == Percent ? ReadoutFormatter.FormatPercent(v).TrimEnd('%') : ReadoutFormatter.FormatNumber(v);
    }

    private static string Estimate(ReadoutSnapshot snapshot)
    {
        if (snapshot.RangeMin is { } min && snapshot.RangeMax is { } max && double.IsFinite(min) && double.IsFinite(max))
        {
            var range = snapshot.Unit == Percent
                ? Number(min, Percent) + ReadoutFormatter.RangeDash + Number(max, Percent)
                : ReadoutFormatter.FormatRange(min, max, string.Empty);
            return "≈" + range;
        }
        return snapshot.Value is { } value && double.IsFinite(value) ? "≈" + Number(value, snapshot.Unit) : ReadoutFormatter.Placeholder;
    }
}
