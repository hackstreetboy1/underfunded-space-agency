using Usa.Game.Presentation.Map;

namespace Usa.Game.UI.Map.Geometry;

/// <summary>
/// The approved Gate 4 legibility projection ("Altitude drawn ×11"): a radial remap of the model's true-scale screen
/// points around the body's centre. The body is drawn at <see cref="BodyScale"/> of its true-scale radius; altitude above
/// the surface is multiplied by <see cref="Factor"/> up to <see cref="KneeBodyRadii"/>, and drawn 1:1 beyond it so far
/// points (hyperbolic arms, the other vehicle at distance) stay on a monotonic, invertible scale. Angles about the
/// centre are preserved, so every polar quantity (anomaly, zone longitude, pointing) reads the same as the model's.
/// </summary>
/// <param name="Centre">The body's centre on screen (<see cref="EarthDisc.Centre"/>).</param>
/// <param name="BodyRadiusPx">The body's true-scale radius on screen (<see cref="EarthDisc.RadiusPx"/>).</param>
public sealed record AltitudeExaggeration(
    MapPoint Centre,
    double BodyRadiusPx,
    double Factor = AltitudeExaggeration.DefaultFactor,
    double BodyScale = AltitudeExaggeration.DefaultBodyScale,
    double KneeBodyRadii = AltitudeExaggeration.DefaultKneeBodyRadii)
{
    /// <summary>Gate 4 decision 1.</summary>
    public const double DefaultFactor = 11;

    /// <summary>G4-01 draws a 280 px Earth where the model's reset view gives about 428 px; this keeps a 300 km orbit inside the 700 × 600 canvas.</summary>
    public const double DefaultBodyScale = 0.65;

    /// <summary>Altitudes above a quarter body radius (about 1,600 km on Earth) are not exaggerated further.</summary>
    public const double DefaultKneeBodyRadii = 0.25;

    public static AltitudeExaggeration For(EarthDisc earth)
    {
        ArgumentNullException.ThrowIfNull(earth);
        return new AltitudeExaggeration(earth.Centre, earth.RadiusPx);
    }

    /// <summary>The body's drawn radius.</summary>
    public double DrawnBodyRadiusPx => BodyRadiusPx * BodyScale;

    private double KneePx => KneeBodyRadii * BodyRadiusPx;

    /// <summary>Drawn distance from the centre for a true-scale distance <paramref name="radiusPx"/>.</summary>
    public double ProjectRadius(double radiusPx)
    {
        if (!(BodyRadiusPx > 0)) return radiusPx;
        if (radiusPx <= BodyRadiusPx) return radiusPx * BodyScale;
        var altitude = radiusPx - BodyRadiusPx;
        var drawnAltitude = altitude <= KneePx ? altitude * Factor : (KneePx * Factor) + (altitude - KneePx);
        return (BodyRadiusPx + drawnAltitude) * BodyScale;
    }

    /// <summary>The inverse of <see cref="ProjectRadius"/>.</summary>
    public double UnprojectRadius(double drawnRadiusPx)
    {
        if (!(BodyRadiusPx > 0) || !(BodyScale > 0)) return drawnRadiusPx;
        var unscaled = drawnRadiusPx / BodyScale;
        if (unscaled <= BodyRadiusPx) return unscaled;
        var drawnAltitude = unscaled - BodyRadiusPx;
        var kneeDrawn = KneePx * Factor;
        var altitude = drawnAltitude <= kneeDrawn ? drawnAltitude / Factor : KneePx + (drawnAltitude - kneeDrawn);
        return BodyRadiusPx + altitude;
    }

    public MapPoint Project(MapPoint point)
    {
        var offset = point - Centre;
        var length = offset.Length;
        return length > 0 ? Centre + (offset * (ProjectRadius(length) / length)) : Centre;
    }

    /// <summary>A drawn (pointer) position back in the model's true-scale screen space.</summary>
    public MapPoint Unproject(MapPoint drawn)
    {
        var offset = drawn - Centre;
        var length = offset.Length;
        return length > 0 ? Centre + (offset * (UnprojectRadius(length) / length)) : Centre;
    }

    public MapPoint[] Project(IReadOnlyList<MapPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var drawn = new MapPoint[points.Count];
        for (var i = 0; i < drawn.Length; i++) drawn[i] = Project(points[i]);
        return drawn;
    }

    /// <summary>The point at <paramref name="drawnRadiusPx"/> from the centre along a screen angle (clockwise-positive, as <c>draw_arc</c>).</summary>
    public MapPoint OnCircle(double drawnRadiusPx, double screenAngleRad) =>
        Centre + new MapPoint(Math.Cos(screenAngleRad) * drawnRadiusPx, Math.Sin(screenAngleRad) * drawnRadiusPx);
}
