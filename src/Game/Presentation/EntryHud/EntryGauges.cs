namespace Usa.Game.Presentation.EntryHud;

/// <summary>A tolerance mark on the g gauge.</summary>
public sealed record GaugeMark(double Value, double Fraction01, string Label, HudTone Tone);

/// <summary>
/// The g-load gauge with the crew tolerance marks (UI 4.2, GDD 7.3). It shows the telemetered accelerometer (Known);
/// tone and marks key off that value.
/// </summary>
/// <param name="Readout">Text, state badge and <c>accelerometer · MET …</c> meta line.</param>
/// <param name="PeakValue">Highest accelerometer reading received (knowledge, not the true peak); null before the first.</param>
/// <param name="PeakFraction01">Where the peak mark sits on the gauge.</param>
/// <param name="PeakText">e.g. <c>peak 8.2 g</c>; empty without a peak or words.</param>
/// <param name="PeakMetText">MET of the peak reading, <c>MET 08:12:04</c>; empty without one.</param>
public sealed record GLoadGauge(
    double? Value,
    double Fraction01,
    string Text,
    HudTone Tone,
    IReadOnlyList<GaugeMark> Marks,
    HudReadoutRow Readout,
    double? PeakValue = null,
    double PeakFraction01 = 0,
    string PeakText = "",
    string PeakMetText = "")
{
    /// <param name="peak">The peak reading so far; the current measured value counts too, so the peak never reads below it.</param>
    public static GLoadGauge From(in HudReadout g, EntryHudLimits limits, double? nowMetS = null, HudReadout peak = default, EntryHudWords? words = null)
    {
        var gauge = From(g, limits, nowMetS);
        var best = peak.HasValue && peak.State is ReadoutState.Known or ReadoutState.Stale ? peak : default;
        if (g.HasValue && g.State is ReadoutState.Known or ReadoutState.Stale && (!best.HasValue || g.Value > best.Value)) best = g;
        if (!best.HasValue) return gauge;
        var max = Math.Max(limits.GaugeMaxG, 1e-6);
        return gauge with
        {
            PeakValue = best.Value,
            PeakFraction01 = Math.Clamp(best.Value / max, 0, 1),
            PeakText = EntryHudWords.Fill((words ?? EntryHudWords.None).PeakG, ("value", EntryHudFormat.GLoad(best.Value))),
            PeakMetText = best.MetS is { } at ? EntryHudFormat.Met(at) : "",
        };
    }

    public static GLoadGauge From(in HudReadout g, EntryHudLimits limits, double? nowMetS)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var max = Math.Max(limits.GaugeMaxG, 1e-6);
        GaugeMark[] marks =
        [
            new(limits.CrewCautionG, Math.Clamp(limits.CrewCautionG / max, 0, 1), EntryHudFormat.GMark(limits.CrewCautionG), HudTone.Caution),
            new(limits.CrewLimitG, Math.Clamp(limits.CrewLimitG / max, 0, 1), EntryHudFormat.GMark(limits.CrewLimitG), HudTone.Critical),
        ];
        var row = HudReadoutRow.From(g, EntryHudFormat.Number, " g", nowMetS);
        if (!g.HasValue) return new GLoadGauge(null, 0, row.Text, HudTone.Unknown, marks, row);

        var value = g.Value;
        var tone = value >= limits.CrewLimitG ? HudTone.Critical : value >= limits.CrewCautionG ? HudTone.Caution : HudTone.Ok;
        return new GLoadGauge(value, Math.Clamp(value / max, 0, 1), row.Text, tone, marks, row);
    }
}

/// <summary>The heating proxy bar (UI 4.2). Always a ground prediction, so it is titled <see cref="Title"/>.</summary>
/// <param name="Readout">Text, <c>EST</c> badge and <c>ground predict · MET …</c> meta line.</param>
public sealed record HeatingBar(double Fraction01, string Text, HudTone Tone, HudReadoutRow Readout)
{
    public const string Title = "heating (predicted)";

    public static HeatingBar From(in HudReadout heating01, EntryHudLimits limits, double? nowMetS = null)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var row = HudReadoutRow.From(heating01, EntryHudFormat.PercentNumber01, "%", nowMetS);
        if (!heating01.HasValue) return new HeatingBar(0, row.Text, HudTone.Unknown, row);
        var fraction = Math.Clamp(heating01.Value, 0, 1);
        var tone = fraction >= limits.HeatingCritical01 ? HudTone.Critical
            : fraction >= limits.HeatingCaution01 ? HudTone.Caution
            : HudTone.Neutral;
        return new HeatingBar(fraction, row.Text, tone, row);
    }
}

/// <summary>Entry checklist progress (UI 4.2), shown only when the player authorized the procedure.</summary>
/// <param name="Text"><c>Entry checklist 2/4</c>.</param>
public sealed record ChecklistProgress(int Completed, int Total, double Fraction01, string Text)
{
    public const string Title = "Entry checklist";

    /// <summary>Null when the checklist is not authorized.</summary>
    public static ChecklistProgress? From(in EntryChecklistInputs checklist)
    {
        if (!checklist.Authorized) return null;
        var total = Math.Max(0, checklist.TotalSteps);
        var completed = Math.Clamp(checklist.CompletedSteps, 0, total);
        var text = total > 0 ? FormattableString.Invariant($"{Title} {completed}/{total}") : Title;
        return new ChecklistProgress(completed, total, total > 0 ? completed / (double)total : 0, text);
    }
}
