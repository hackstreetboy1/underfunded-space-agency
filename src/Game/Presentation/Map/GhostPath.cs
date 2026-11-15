using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Game.Presentation.Map;

/// <summary>The ✕ where a previewed trajectory comes down, with its zone and entry quality.</summary>
/// <param name="PositionM">Inertial impact point (where the vehicle meets the surface at <see cref="Time"/>).</param>
/// <param name="ZoneLabel">Zone rim label, or <c>OUTSIDE ZONES</c>.</param>
/// <param name="Verdict">The zone verdict over the whole landing arc; <see cref="LandingZoneVerdict.None"/> without one.</param>
/// <param name="Band">The arc's half-width (<c>±70 km</c>), shown after the zone label as <c>≈ ZONE ±70 km</c>; null without dispersion.</param>
public sealed record LandingMarker(
    MapPoint Position,
    Vec2d PositionM,
    MissionTime Time,
    double LongitudeDeg,
    string? ZoneId,
    string ZoneLabel,
    double RecoveryWaitHours,
    CorridorQuality Corridor,
    string CorridorWord,
    string TimeText,
    LandingZoneVerdict Verdict = LandingZoneVerdict.None,
    string? Band = null);

/// <summary>
/// A burn preview drawn on the map: the post-burn coast (<c>info-ghost</c>, dashed), and when the preview lands, the
/// coast stops at the entry interface and an approximate descent leads to the landing marker.
/// <see cref="Coast"/> is null when there is no coast to draw (fewer than two points, e.g. the burn ends at the
/// interface): a polyline needs at least two.
/// </summary>
/// <param name="Corridor">
/// The entry corridor where the previewed coast descends through the interface, so the player sees the corridor
/// quality before executing (UI.md 4.1); null when the preview does not cross it after the burn.
/// </param>
/// <param name="LandingArc">The landing dispersion on the rim (X8), drawn instead of a bare ✕; null without dispersion.</param>
/// <param name="ZoneAtLanding">
/// The landing zone where it will be at splashdown (the body turns between burn and landing; Gate 4 rev 2, drawn
/// dashed); null when the preview lands outside every zone or not at all.
/// </param>
public sealed record GhostPath(
    MapPolyline? Coast, MapPolyline? Descent, LandingMarker? Landing, EntryCorridorBand? Corridor = null, RecoveryZoneArc? ZoneAtLanding = null,
    MapPolyline? LandingArc = null)
{
    private const double DescentStepRad = 0.01;

    public static GhostPath For(
        ManeuverPreview preview, double bodyRadiusM, MapTransform transform, ConicPathSampler sampler, double toleranceM,
        double clipRadiusM, MissionTime now, Func<string, string> zoneLabel, MapText text)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentNullException.ThrowIfNull(sampler);
        ArgumentNullException.ThrowIfNull(zoneLabel);
        ArgumentNullException.ThrowIfNull(text);
        var conic = preview.ResultingConic;
        var arc = ConicArc.Of(conic, bodyRadiusM, clipRadiusM);
        var path = sampler.Sample(conic, arc, toleranceM);
        var start = ConicGeometry.AnomalyAt(conic, preview.Plan.EndTime);

        if (preview.Entry is { Landing: { } landing } entry)
        {
            var interfaceAnomaly = ConicGeometry.AnomalyAt(conic, entry.InterfaceTime);
            var coastPoints = entry.InterfaceTime > preview.Plan.EndTime ? path.Between(start, interfaceAnomaly) : [];
            var descentStart = coastPoints.Count > 0 ? coastPoints[^1] : ConicGeometry.PositionAt(conic, start);
            var landingPoint = Vec2d.FromPolar(bodyRadiusM, landing.ImpactAngleRad);
            var descent = DescentLine(descentStart, landingPoint, conic.Direction, transform);
            var marker = new LandingMarker(
                transform.WorldToScreen(landingPoint), landingPoint, landing.Time, landing.Zone.LandingLongitudeDeg,
                landing.Zone.ZoneId, landing.Zone.ZoneId is { } id ? zoneLabel(id) : text.OutsideZones,
                landing.Zone.RecoveryWaitHours, entry.Corridor, text.CorridorWord(entry.Corridor),
                text.Relative(now, landing.Time));
            if (preview.Dispersion is not { } dispersion) return new GhostPath(ToScreen(coastPoints, false, transform), descent, marker);
            marker = marker with { Verdict = VerdictOf(landing.Zone, dispersion), Band = Band(dispersion) };
            var landingArc = RimArc(bodyRadiusM, landing.ImpactAngleRad, dispersion.HalfWidthRad, transform);
            return new GhostPath(ToScreen(coastPoints, false, transform), descent, marker, LandingArc: landingArc);
        }

        var points = arc.IsClosed ? path.PointsM : path.From(start);
        return new GhostPath(ToScreen(points, arc.IsClosed, transform), null, null);
    }

    public static LandingZoneVerdict VerdictOf(in ZoneEvaluation zone, LandingDispersion dispersion) =>
        zone.ZoneId is null ? LandingZoneVerdict.OutsideZones : dispersion.InZone ? LandingZoneVerdict.InZone : LandingZoneVerdict.NearEdge;

    /// <summary><c>±70 km</c>: the arc's half-width.</summary>
    public static string Band(LandingDispersion dispersion) => "±" + FlightFormat.AltitudeKm(dispersion.HalfWidthKm);

    private static MapPolyline RimArc(double radiusM, double centreRad, double halfWidthRad, MapTransform transform)
    {
        var steps = Math.Max(4, (int)Math.Ceiling(2 * halfWidthRad / DescentStepRad));
        var points = new MapPoint[steps + 1];
        for (var i = 0; i <= steps; i++)
            points[i] = transform.WorldToScreen(Vec2d.FromPolar(radiusM, centreRad - halfWidthRad + 2 * halfWidthRad * i / steps));
        return new MapPolyline(points, false, MapStyleKeys.LandingArc);
    }

    /// <summary>Polar interpolation from the interface point to the landing point, in the direction of motion.</summary>
    private static MapPolyline DescentLine(Vec2d fromM, Vec2d toM, int direction, MapTransform transform)
    {
        var startAngle = fromM.Angle;
        var sweep = direction * Angles.WrapPositive(direction * (toM.Angle - startAngle));
        var steps = Math.Max(8, (int)Math.Ceiling(Math.Abs(sweep) / DescentStepRad));
        var startRadius = fromM.Length;
        var endRadius = toM.Length;
        var points = new MapPoint[steps + 1];
        for (var i = 0; i <= steps; i++)
        {
            var t = (double)i / steps;
            points[i] = transform.WorldToScreen(Vec2d.FromPolar(startRadius + (endRadius - startRadius) * t, startAngle + sweep * t));
        }
        return new MapPolyline(points, false, MapStyleKeys.GhostDescent);
    }

    private static MapPolyline? ToScreen(IReadOnlyList<Vec2d> pointsM, bool closed, MapTransform transform)
    {
        if (pointsM.Count < 2) return null;
        var points = new MapPoint[pointsM.Count];
        for (var i = 0; i < points.Length; i++) points[i] = transform.WorldToScreen(pointsM[i]);
        return new MapPolyline(points, closed, MapStyleKeys.GhostPath);
    }
}
