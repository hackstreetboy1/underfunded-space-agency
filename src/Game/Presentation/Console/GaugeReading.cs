using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.Presentation.ViewModels;

namespace Usa.Game.Presentation.Console;

/// <summary>
/// What an arc gauge draws (UI.md 3.9, Figma G1-02 "Arc gauge" Nominal / Caution / Critical / Unknown): the filled share of
/// the arc, the caution and critical bands, and the status. Only a value the ground has (known, stale, estimated or
/// conflicting) fills the arc; an unknown value or a ground band leaves it empty with the Unknown status.
/// </summary>
/// <param name="Fraction">Magnitude over the scale's maximum, 0–1; null when nothing is known.</param>
/// <param name="CautionFraction">Where the caution band starts on the arc; null without one.</param>
/// <param name="CriticalFraction">Where the critical band starts on the arc; null without one.</param>
public sealed record GaugeReading(double? Fraction, double? CautionFraction, double? CriticalFraction, ConsoleStatus Status)
{
    public static GaugeReading From(ReadoutSnapshot sanitized, GaugeScale scale)
    {
        ArgumentNullException.ThrowIfNull(sanitized);
        ArgumentNullException.ThrowIfNull(scale);
        var max = scale.Max > 0 && double.IsFinite(scale.Max) ? scale.Max : 1;
        var caution = Share(scale.CautionAt, max);
        var critical = Share(scale.CriticalAt, max);
        var value = sanitized.Knowledge is ReadoutKnowledge.Unknown or ReadoutKnowledge.GroundBand ? null : sanitized.Value;
        if (value is not { } v || !double.IsFinite(v)) return new GaugeReading(null, caution, critical, ConsoleStatus.Unknown);

        var magnitude = Math.Abs(v);
        var status = scale.CriticalAt is { } c && magnitude >= c ? ConsoleStatus.Warning
            : scale.CautionAt is { } w && magnitude >= w ? ConsoleStatus.Caution
            : ConsoleStatus.Nominal;
        return new GaugeReading(Math.Clamp(magnitude / max, 0, 1), caution, critical, status);
    }

    private static double? Share(double? at, double max) => at is { } a && double.IsFinite(a) ? Math.Clamp(a / max, 0, 1) : null;
}
