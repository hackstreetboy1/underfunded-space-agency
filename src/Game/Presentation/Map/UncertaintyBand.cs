namespace Usa.Game.Presentation.Map;

/// <summary>
/// A path widened by the navigation uncertainty: <see cref="Left"/> and <see cref="Right"/> are the path offset to
/// each side (same count as the path). Fill it with <see cref="Triangles"/> (every three points one triangle), which
/// also works for closed rings where a single polygon would not.
/// </summary>
public sealed record UncertaintyBand(
    IReadOnlyList<MapPoint> Left,
    IReadOnlyList<MapPoint> Right,
    IReadOnlyList<MapPoint> Triangles,
    double StartHalfWidthPx,
    double EndHalfWidthPx)
{
    /// <summary>Miter joins are capped at this multiple of the half width, so sharp corners do not spike.</summary>
    private const double MiterLimit = 2.5;

    /// <summary>
    /// Offsets <paramref name="path"/> by a half width that tapers linearly (by arc length) from
    /// <paramref name="startHalfWidthPx"/> to <paramref name="endHalfWidthPx"/>; null when the band would be invisible.
    /// </summary>
    public static UncertaintyBand? Around(IReadOnlyList<MapPoint> path, bool closed, double startHalfWidthPx, double endHalfWidthPx)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count < 2 || (startHalfWidthPx <= 0 && endHalfWidthPx <= 0)) return null;
        var count = path.Count;
        var cumulative = new double[count];
        for (var i = 1; i < count; i++) cumulative[i] = cumulative[i - 1] + path[i].DistanceTo(path[i - 1]);
        var total = cumulative[count - 1];

        var left = new MapPoint[count];
        var right = new MapPoint[count];
        for (var i = 0; i < count; i++)
        {
            var halfWidth = Math.Max(0, total > 0
                ? startHalfWidthPx + (endHalfWidthPx - startHalfWidthPx) * cumulative[i] / total
                : startHalfWidthPx);
            var offset = OffsetAt(path, i, closed) * halfWidth;
            left[i] = path[i] + offset;
            right[i] = path[i] - offset;
        }

        var triangles = new List<MapPoint>((count - 1) * 6);
        for (var i = 0; i < count - 1; i++)
        {
            triangles.Add(left[i]);
            triangles.Add(right[i]);
            triangles.Add(left[i + 1]);
            triangles.Add(left[i + 1]);
            triangles.Add(right[i]);
            triangles.Add(right[i + 1]);
        }
        return new UncertaintyBand(left, right, triangles, startHalfWidthPx, endHalfWidthPx);
    }

    /// <summary>
    /// The same triangles as <see cref="Around"/>, appended to <paramref name="triangles"/> only for the segments whose
    /// quad overlaps the box (minX, minY, maxX, maxY), at most <paramref name="maxTriangles"/>; nothing else is allocated.
    /// The taper still runs over the whole path, so a clipped band is the visible part of the whole one.
    /// </summary>
    public static void AppendTriangles(
        IReadOnlyList<MapPoint> path, bool closed, double startHalfWidthPx, double endHalfWidthPx,
        (double MinX, double MinY, double MaxX, double MaxY) bounds, List<MapPoint> triangles, int maxTriangles)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(triangles);
        if (path.Count < 2 || (startHalfWidthPx <= 0 && endHalfWidthPx <= 0)) return;
        var count = path.Count;
        var total = 0.0;
        for (var i = 1; i < count; i++) total += path[i].DistanceTo(path[i - 1]);
        var limit = triangles.Count + (maxTriangles * 3);
        var run = 0.0;
        MapPoint previousLeft = default, previousRight = default;
        for (var i = 0; i < count && triangles.Count < limit; i++)
        {
            if (i > 0) run += path[i].DistanceTo(path[i - 1]);
            var halfWidth = Math.Max(0, total > 0
                ? startHalfWidthPx + (endHalfWidthPx - startHalfWidthPx) * run / total
                : startHalfWidthPx);
            var offset = OffsetAt(path, i, closed) * halfWidth;
            var left = path[i] + offset;
            var right = path[i] - offset;
            if (i > 0
                && Math.Max(Math.Max(previousLeft.X, previousRight.X), Math.Max(left.X, right.X)) >= bounds.MinX
                && Math.Min(Math.Min(previousLeft.X, previousRight.X), Math.Min(left.X, right.X)) <= bounds.MaxX
                && Math.Max(Math.Max(previousLeft.Y, previousRight.Y), Math.Max(left.Y, right.Y)) >= bounds.MinY
                && Math.Min(Math.Min(previousLeft.Y, previousRight.Y), Math.Min(left.Y, right.Y)) <= bounds.MaxY)
            {
                triangles.Add(previousLeft);
                triangles.Add(previousRight);
                triangles.Add(left);
                triangles.Add(left);
                triangles.Add(previousRight);
                triangles.Add(right);
            }
            previousLeft = left;
            previousRight = right;
        }
    }

    /// <summary>Miter offset direction at a vertex (unit normal scaled for the joint), pointing to the path's left.</summary>
    private static MapPoint OffsetAt(IReadOnlyList<MapPoint> path, int i, bool closed)
    {
        var last = path.Count - 1;
        // A closed path repeats its first point at the end, so the neighbours of either end are path[1] and path[last-1].
        var hasPrevious = i > 0 || closed;
        var hasNext = i < last || closed;
        var previous = i > 0 ? path[i - 1] : path[Math.Max(0, last - 1)];
        var next = i < last ? path[i + 1] : path[Math.Min(last, 1)];
        var incoming = hasPrevious ? (path[i] - previous).Normalized() : default;
        var outgoing = hasNext ? (next - path[i]).Normalized() : default;
        var tangent = (incoming + outgoing).Normalized();
        if (tangent.Length == 0) tangent = outgoing.Length > 0 ? outgoing : incoming;
        var normal = new MapPoint(tangent.Y, -tangent.X);
        var segmentNormal = outgoing.Length > 0 ? new MapPoint(outgoing.Y, -outgoing.X) : new MapPoint(incoming.Y, -incoming.X);
        var cos = normal.X * segmentNormal.X + normal.Y * segmentNormal.Y;
        var scale = cos > 1 / MiterLimit ? 1 / cos : MiterLimit;
        return normal * scale;
    }
}
