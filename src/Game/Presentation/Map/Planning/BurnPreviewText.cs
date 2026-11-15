using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Map.Planning;

/// <summary>The propellant rows of the preview, derived from what the ground knows (X8).</summary>
/// <param name="Used">e.g. <c>7.4 kg</c>, or a range for a band.</param>
/// <param name="RemainingDeltaV">e.g. <c>140 m/s</c>, a range for a band, or the placeholder when unknown.</param>
/// <param name="Badge">Empty for a fresh reading or a design value; otherwise <c>EST</c>, <c>STALE 12m</c> or <c>UNKNOWN</c>.</param>
/// <param name="Meta">Source and MET of the knowledge (<c>reported by June Okafor · MET 06:58</c>); null when there is none.</param>
/// <param name="State">Null when the figure is a design value (the solid retro sequence).</param>
public sealed record PropellantFigures(string Used, string RemainingDeltaV, string Badge, string? Meta, PlannerKnowledgeState? State);

/// <summary>The planner's preview block, formatted per UI.md 6.2.</summary>
/// <param name="PropellantUsed">Knowledge-derived; see <see cref="Propellant"/> for its badge and source.</param>
/// <param name="RemainingDeltaV">Knowledge-derived; see <see cref="Propellant"/> for its badge and source.</param>
/// <param name="Entry">e.g. <c>CORRIDOR OK · −1.6°</c> or <c>NO ENTRY</c>.</param>
/// <param name="LandingZone">Zone label, <c>OUTSIDE ZONES</c>, or the placeholder when the preview does not land.</param>
/// <param name="StatusToken">Worst status of the preview: <c>ok</c>, <c>caution</c> or <c>critical</c>.</param>
/// <param name="EntryAngle">The entry flight-path angle alone (<c>−1.3°</c>), for the <c>angle · g · badge</c> row (Gate 4 rev 2); the placeholder without entry.</param>
/// <param name="LandingBand">The landing arc's half-width (<c>±70 km</c>, X8); empty without ground uncertainty.</param>
/// <param name="Verdict">IN ZONE only when the whole arc is inside the zone, else NEAR EDGE (a caution); see <see cref="CorridorText.ZoneVerdictToken"/>.</param>
public sealed record BurnPreviewText(
    string Duration,
    string DeltaV,
    string PropellantUsed,
    string RemainingDeltaV,
    string Periapsis,
    string Apoapsis,
    string Entry,
    CorridorQuality Corridor,
    string LandingZone,
    string RecoveryWait,
    string PeakGLoad,
    string LandingTime,
    string StatusToken,
    PropellantFigures Propellant,
    string EntryAngle,
    string LandingBand = "",
    LandingZoneVerdict Verdict = LandingZoneVerdict.None,
    string VerdictWord = "")
{
    private const double MinDeltaVBandMps = 0.5;

    /// <param name="propellant">The knowledge-derived propellant rows; null shows the preview's own figures (design-value engines only).</param>
    public static BurnPreviewText From(
        ManeuverPreview preview, MissionTime now, Func<string, string> zoneLabel, IReadOnlyList<PlannerMessage> warnings, MapText text,
        PropellantFigures? propellant = null)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(zoneLabel);
        ArgumentNullException.ThrowIfNull(warnings);
        var entry = preview.Entry;
        var landing = entry?.Landing;
        var status = "ok";
        var dispersion = landing is not null ? preview.Dispersion : null;
        var verdict = dispersion is not null && landing is { } judged ? GhostPath.VerdictOf(judged.Zone, dispersion) : LandingZoneVerdict.None;
        if (verdict == LandingZoneVerdict.NearEdge) status = "caution";
        foreach (var warning in warnings)
        {
            if (warning.Severity == PlannerSeverity.Critical) status = "critical";
            else if (warning.Severity == PlannerSeverity.Caution && status == "ok") status = "caution";
        }
        propellant ??= new PropellantFigures(
            FlightFormat.Mass(preview.PropellantUsedKg), FlightFormat.Speed(preview.RemainingDeltaVMps), string.Empty, null, null);

        return new BurnPreviewText(
            FlightFormat.Duration(preview.Plan.EffectiveDurationS),
            DeltaVText(preview),
            propellant.Used,
            propellant.RemainingDeltaV,
            text.Apsis(ApsisKind.Periapsis, preview.PeriapsisKm),
            double.IsFinite(preview.ApoapsisKm) ? text.Apsis(ApsisKind.Apoapsis, preview.ApoapsisKm) : text.Get("map.apoapsisEscape"),
            entry is null
                ? text.CorridorWord(CorridorQuality.NoEntry)
                : $"{text.CorridorWord(entry.Corridor)} · {FlightFormat.AngleDeg(entry.FlightPathAngleDeg)}",
            preview.Corridor,
            landing is { } site ? site.Zone.ZoneId is { } id ? zoneLabel(id) : text.OutsideZones : FlightFormat.Placeholder,
            landing is { } wait ? text.Format("planner.previewRecovery", ("hours", FlightFormat.Hours(wait.Zone.RecoveryWaitHours))) : FlightFormat.Placeholder,
            entry is null ? FlightFormat.Placeholder : FlightFormat.GLoad(entry.PeakGLoad),
            landing is { } time ? text.Format("planner.previewSplashdown", ("time", text.Relative(now, time.Time))) : FlightFormat.Placeholder,
            status,
            propellant,
            entry is null ? FlightFormat.Placeholder : FlightFormat.AngleDeg(entry.FlightPathAngleDeg),
            dispersion is null ? string.Empty : GhostPath.Band(dispersion),
            verdict,
            text.ZoneVerdictWord(verdict));
    }

    /// <summary>One value, or <c>≈ 91.6–92.4 m/s</c> when the mass band spreads Δv by half a metre per second or more (X8).</summary>
    private static string DeltaVText(ManeuverPreview preview) =>
        preview.Dispersion is { } band && band.DeltaVHighMps - band.DeltaVLowMps >= MinDeltaVBandMps
            ? "≈ " + FlightFormat.SpeedRange(band.DeltaVLowMps, band.DeltaVHighMps)
            : FlightFormat.Speed(preview.DeltaVMps);
}
