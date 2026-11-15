using Usa.Game.Presentation.Map;

namespace Usa.Game.UI.Map.Geometry;

/// <summary>
/// The nav-uncertainty band (X8: 2σ radial, at least 3 px wide) rebuilt in drawn space: the path is projected with the
/// ×11 altitude scale and the half width, a radial distance, is exaggerated by the same factor, so the band hugs the
/// drawn path instead of a projected copy of the true-scale band.
/// </summary>
public static class ExaggeratedBand
{
    /// <summary>X8 minimum: a band never narrower than 3 px in total.</summary>
    public const double MinHalfWidthPx = 1.5;

    /// <param name="path">True-scale screen points (the model's polyline).</param>
    /// <param name="startHalfWidthPx">True-scale half width at the path start (the model's band, or the nav σ in pixels).</param>
    /// <param name="endHalfWidthPx">True-scale half width at the path end (start × growth).</param>
    public static UncertaintyBand? Around(
        IReadOnlyList<MapPoint> path, bool closed, double startHalfWidthPx, double endHalfWidthPx, AltitudeExaggeration projection)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(projection);
        if (path.Count < 2 || (startHalfWidthPx <= 0 && endHalfWidthPx <= 0)) return null;
        var scale = projection.Factor * projection.BodyScale;
        return UncertaintyBand.Around(
            projection.Project(path), closed,
            Math.Max(MinHalfWidthPx, startHalfWidthPx * scale),
            Math.Max(MinHalfWidthPx, endHalfWidthPx * scale));
    }

    /// <summary>
    /// The visible triangles of <see cref="Around"/> for a path already in drawn space (<paramref name="drawnPath"/>),
    /// appended to <paramref name="triangles"/> without allocating (<see cref="UncertaintyBand.AppendTriangles"/>).
    /// </summary>
    public static void AppendVisibleTriangles(
        IReadOnlyList<MapPoint> drawnPath, bool closed, double startHalfWidthPx, double endHalfWidthPx, AltitudeExaggeration projection,
        ClipRect clip, List<MapPoint> triangles, int maxTriangles)
    {
        ArgumentNullException.ThrowIfNull(drawnPath);
        ArgumentNullException.ThrowIfNull(projection);
        if (drawnPath.Count < 2 || (startHalfWidthPx <= 0 && endHalfWidthPx <= 0)) return;
        var scale = projection.Factor * projection.BodyScale;
        UncertaintyBand.AppendTriangles(
            drawnPath, closed,
            Math.Max(MinHalfWidthPx, startHalfWidthPx * scale),
            Math.Max(MinHalfWidthPx, endHalfWidthPx * scale),
            (clip.MinX, clip.MinY, clip.MaxX, clip.MaxY), triangles, maxTriangles);
    }
}
