namespace Usa.Game.Presentation.EntryHud;

/// <summary>A "time to drogue/main altitude" row (UI 4.2).</summary>
/// <param name="TargetText">The deploy altitude, <c>15.0 km</c>.</param>
/// <param name="Seconds">Estimated seconds to the altitude; null when there is no estimate.</param>
/// <param name="Text"><c>in 1 min 40 s</c>, <c>now</c>, <c>deployed</c>, <c>skipped</c>, or a dash.</param>
/// <param name="IsEstimate">The text is an estimate (the view adds the <c>EST</c> badge, UI 2.3).</param>
public sealed record TimeToAltitude(string Label, string TargetText, double? Seconds, string Text, HudTone Tone, bool IsEstimate)
{
    /// <summary>A slower descent than this (m/s) counts as not descending: no estimate.</summary>
    public const double MinDescentMps = 0.5;

    public const string SkippedText = "skipped";

    /// <summary>
    /// A chute that was never deployed and whose step a later chute has superseded (the drogue once the main is out):
    /// <c>skipped</c> in a neutral tone, so the row does not invite a pointless deploy.
    /// </summary>
    public static TimeToAltitude Skipped(string label, double targetAltitudeM)
    {
        ArgumentNullException.ThrowIfNull(label);
        return new(label, EntryHudFormat.AltitudeKm(targetAltitudeM), null, SkippedText, HudTone.Neutral, false);
    }

    /// <summary>
    /// Straight-line estimate from the current descent rate (velocity × sin flight-path angle). Drag keeps slowing the
    /// descent, so it reads early during the hot part of entry and settles once the capsule falls steadily; hence the
    /// estimate flag. Past the altitude with the chute still stowed it reads <c>now</c> in caution.
    /// </summary>
    public static TimeToAltitude Estimate(string label, double targetAltitudeM, EntryEventProgress chute, in EntryFlightInputs flight) =>
        Estimate(
            label,
            targetAltitudeM,
            chute,
            flight.DisplayedAltitude(EntryHudLimits.Default.AltimeterCeilingM),
            flight.VelocityMps,
            flight.FlightPathAngleDeg);

    /// <summary>The estimate from the shown altitude row and the predicted speed and flight-path angle (always EST).</summary>
    public static TimeToAltitude Estimate(
        string label, double targetAltitudeM, EntryEventProgress chute, in HudReadout altitudeM, in HudReadout velocityMps, in HudReadout flightPathAngleDeg)
    {
        ArgumentNullException.ThrowIfNull(label);
        var target = EntryHudFormat.AltitudeKm(targetAltitudeM);
        switch (chute)
        {
            case EntryEventProgress.Done: return new(label, target, null, "deployed", HudTone.Ok, false);
            case EntryEventProgress.InProgress: return new(label, target, null, "deploying", HudTone.Info, false);
            case EntryEventProgress.Failed: return new(label, target, null, "failed", HudTone.Critical, false);
        }

        var altitude = altitudeM.ValueOrNaN;
        var velocity = velocityMps.ValueOrNaN;
        var angle = flightPathAngleDeg.ValueOrNaN;
        if (!double.IsFinite(altitude) || !double.IsFinite(velocity) || !double.IsFinite(angle))
            return new(label, target, null, EntryHudFormat.Placeholder, HudTone.Unknown, false);
        if (altitude <= targetAltitudeM) return new(label, target, 0, "now", HudTone.Caution, false);

        var verticalMps = velocity * Math.Sin(angle * Math.PI / 180.0);
        if (verticalMps > -MinDescentMps) return new(label, target, null, EntryHudFormat.Placeholder, HudTone.Neutral, false);

        var seconds = (altitude - targetAltitudeM) / -verticalMps;
        return new(label, target, seconds, EntryHudFormat.Countdown(seconds), HudTone.Neutral, true);
    }
}
