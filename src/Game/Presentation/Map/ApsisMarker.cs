using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Game.Presentation.Map;

public enum ApsisKind
{
    Periapsis,
    Apoapsis,
}

/// <summary>A Pe or Ap marker with its Readout-S label and "time to" chip.</summary>
/// <param name="Label">e.g. <c>Pe 290 km</c>.</param>
/// <param name="TimeToText">e.g. <c>in 12 min</c>; null when the apsis is not ahead (a departing hyperbola).</param>
/// <param name="Meta">Source, MET and state of the orbit (<c>ground tracking · MET 06:58</c>, <c>EST · …</c>); null when the host gave none.</param>
public sealed record ApsisMarker(
    ApsisKind Kind,
    MapPoint Position,
    Vec2d PositionM,
    double AltitudeKm,
    string Label,
    MissionTime? NextTime,
    string? TimeToText,
    string? Meta = null)
{
    /// <summary>Markers for the apsides that lie on the drawn arc of <paramref name="conic"/>.</summary>
    public static IReadOnlyList<ApsisMarker> For(
        in Conic conic, ConicArc arc, double bodyRadiusM, MapTransform transform, MissionTime now, double minEccentricity, MapText text,
        OrbitFix? orbit = null)
    {
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentNullException.ThrowIfNull(text);
        if (arc.IsEmpty || conic.Eccentricity < minEccentricity) return [];
        var meta = orbit is null ? null : text.OrbitMeta(orbit, now);
        var markers = new List<ApsisMarker>(2);
        if (arc.Contains(0)) markers.Add(Create(ApsisKind.Periapsis, conic, 0, bodyRadiusM, transform, now, text, meta));
        if (!conic.IsHyperbolic && arc.Contains(Math.PI)) markers.Add(Create(ApsisKind.Apoapsis, conic, Math.PI, bodyRadiusM, transform, now, text, meta));
        return markers;
    }

    private static ApsisMarker Create(
        ApsisKind kind, in Conic conic, double anomaly, double bodyRadiusM, MapTransform transform, MissionTime now, MapText text, string? meta)
    {
        var position = ConicGeometry.PositionAt(conic, anomaly);
        var altitudeKm = (position.Length - bodyRadiusM) / 1000;
        var next = conic.NextTimeAtTrueAnomaly(anomaly, now);
        return new ApsisMarker(
            kind, transform.WorldToScreen(position), position, altitudeKm, text.Apsis(kind, altitudeKm),
            next, next is { } time ? text.Relative(now, time) : null, meta);
    }
}
