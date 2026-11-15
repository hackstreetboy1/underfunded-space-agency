using Usa.Game.Presentation.Map;

namespace Usa.Game.UI.Map.Geometry;

/// <summary>How a circle meets a <see cref="ClipRect"/>.</summary>
public enum CircleCoverage
{
    /// <summary>The circle and its disc lie wholly outside.</summary>
    None,

    /// <summary>The disc covers the whole rectangle; its outline is not visible.</summary>
    Encloses,

    /// <summary>The outline passes through the rectangle.</summary>
    Crosses,
}

/// <summary>
/// Viewport clipping and screen-error sampling for the orbit map. Zoomed in, the drawn Earth, atmosphere ring and orbits
/// have radii of tens of thousands of pixels: drawing them whole (fixed angular steps, dashes all the way round) made
/// tens of thousands of canvas commands per redraw. These helpers keep only what can be seen, so the work stays bounded
/// by the viewport at any zoom while unclipped geometry comes out exactly as before.
/// </summary>
public static class ViewportClip
{
    /// <summary>Vertices closer than this (pixels) to their neighbour or to the line through both neighbours are dropped.</summary>
    public const double SnapPx = 1e-3;

    /// <summary>A clipped fill smaller than this (square pixels) is a sliver and is dropped.</summary>
    public const double MinFillAreaPx2 = 1e-3;

    private const double TwoPi = 2 * Math.PI;

    /// <summary>The visible runs of <paramref name="points"/>; each run has at most <paramref name="maxRunPoints"/> points (longer runs are split, sharing an end point).</summary>
    public static void Polyline(IReadOnlyList<MapPoint> points, ClipRect rect, PolylineSet output, int maxRunPoints)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRunPoints, 2);
        var open = false;
        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            if (!rect.ClipSegment(a, b, out var t0, out var t1))
            {
                if (open) output.End();
                open = false;
                continue;
            }
            if (!open || t0 > 0)
            {
                output.Begin();
                output.Add(t0 > 0 ? Lerp(a, b, t0) : a);
                open = true;
            }
            else if (output.OpenLength >= maxRunPoints)
            {
                var last = output.Last;
                output.Begin();
                output.Add(last);
            }
            output.Add(t1 < 1 ? Lerp(a, b, t1) : b);
            if (t1 < 1)
            {
                output.End();
                open = false;
            }
        }
        output.End();
    }

    /// <summary>
    /// The visible dashes of a dashed polyline as point pairs, in the same phase as drawing it whole (a dash starts at
    /// path length 0, <paramref name="startLengthPx"/> being the length already run before the first point), at most
    /// <paramref name="maxDashes"/>.
    /// </summary>
    public static void Dashes(
        IReadOnlyList<MapPoint> points, double dashPx, double gapPx, ClipRect rect, List<MapPoint> pairs, int maxDashes,
        double startLengthPx = 0)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dashPx);
        ArgumentOutOfRangeException.ThrowIfNegative(gapPx);
        var limit = pairs.Count + (2 * maxDashes);
        var period = dashPx + gapPx;
        var run = startLengthPx;
        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var segment = points[i] - a;
            var length = segment.Length;
            if (length == 0) continue;
            if (rect.ClipSegment(a, points[i], out var t0, out var t1))
            {
                var unit = segment * (1 / length);
                var from = run + (t0 * length);
                var to = run + (t1 * length);
                if (gapPx == 0)
                {
                    pairs.Add(a + (unit * (t0 * length)));
                    pairs.Add(a + (unit * (t1 * length)));
                }
                else
                {
                    for (var k = Math.Floor(from / period); k * period < to; k++)
                    {
                        var lo = Math.Max(k * period, from);
                        var hi = Math.Min((k * period) + dashPx, to);
                        if (hi <= lo) continue;
                        pairs.Add(a + (unit * (lo - run)));
                        pairs.Add(a + (unit * (hi - run)));
                        if (pairs.Count >= limit) return;
                    }
                }
                if (pairs.Count >= limit) return;
            }
            run += length;
        }
    }

    public static CircleCoverage Coverage(MapPoint centre, double radiusPx, ClipRect rect)
    {
        if (!(radiusPx > 0) || rect.DistanceTo(centre) > radiusPx) return CircleCoverage.None;
        return rect.FarthestCornerDistance(centre) <= radiusPx ? CircleCoverage.Encloses : CircleCoverage.Crosses;
    }

    /// <summary>
    /// Screen angles (clockwise-positive, as <c>draw_arc</c>) from <paramref name="centre"/> that can reach the rectangle:
    /// the whole turn [0, 2π] when the centre is inside, otherwise the rectangle's angular extent (less than π).
    /// </summary>
    public static (double Start, double End) Wedge(MapPoint centre, ClipRect rect)
    {
        if (rect.Contains(centre)) return (0, TwoPi);
        var towards = rect.Centre - centre;
        var reference = Math.Atan2(towards.Y, towards.X);
        var start = double.MaxValue;
        var end = double.MinValue;
        Corner(rect.MinX, rect.MinY);
        Corner(rect.MaxX, rect.MinY);
        Corner(rect.MaxX, rect.MaxY);
        Corner(rect.MinX, rect.MaxY);
        return (start, end);

        void Corner(double x, double y)
        {
            var angle = reference + WrapPi(Math.Atan2(y - centre.Y, x - centre.X) - reference);
            start = Math.Min(start, angle);
            end = Math.Max(end, angle);
        }
    }

    /// <summary>
    /// The parts of the arc <paramref name="start"/> → <paramref name="end"/> (either direction) inside the wedge, in arc
    /// order and in the arc's own angle values.
    /// </summary>
    public static void ArcSpans(double start, double end, (double Start, double End) wedge, List<(double From, double To)> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);
        spans.Clear();
        if (wedge.End - wedge.Start >= TwoPi)
        {
            spans.Add((start, end));
            return;
        }
        var lo = Math.Min(start, end);
        var hi = Math.Max(start, end);
        var first = Math.Floor((lo - wedge.End) / TwoPi);
        var last = Math.Ceiling((hi - wedge.Start) / TwoPi);
        for (var k = first; k <= last; k++)
        {
            var a = Math.Max(lo, wedge.Start + (k * TwoPi));
            var b = Math.Min(hi, wedge.End + (k * TwoPi));
            if (b > a) spans.Add((a, b));
        }
        if (end >= start) return;
        spans.Reverse();
        for (var i = 0; i < spans.Count; i++) spans[i] = (spans[i].To, spans[i].From);
    }

    /// <summary>Angular step whose chord stays within <paramref name="tolerancePx"/> of a circle of <paramref name="radiusPx"/>.</summary>
    public static double StepFor(double radiusPx, double tolerancePx) =>
        radiusPx <= tolerancePx ? Math.PI / 2 : Math.Min(Math.PI / 2, 2 * Math.Acos(1 - (tolerancePx / radiusPx)));

    /// <summary>Appends points along a circle from one screen angle to another, sampled to the tolerance and capped.</summary>
    public static void AppendArc(
        MapPoint centre, double radiusPx, double from, double to, double tolerancePx, int maxPoints, List<MapPoint> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var segments = (int)Math.Clamp(Math.Ceiling(Math.Abs(to - from) / StepFor(radiusPx, tolerancePx)), 1, maxPoints - 1);
        for (var i = 0; i <= segments; i++)
        {
            var angle = from + ((to - from) * i / segments);
            output.Add(centre + new MapPoint(Math.Cos(angle) * radiusPx, Math.Sin(angle) * radiusPx));
        }
    }

    /// <summary>Sutherland–Hodgman: clips a convex polygon to the rectangle in place.</summary>
    public static void ClipConvexPolygon(List<MapPoint> polygon, ClipRect rect, List<MapPoint> scratch)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        ArgumentNullException.ThrowIfNull(scratch);
        for (var edge = 0; edge < 4 && polygon.Count > 0; edge++) ClipAgainst(polygon, scratch, rect, edge);
    }

    private static void ClipAgainst(List<MapPoint> polygon, List<MapPoint> scratch, ClipRect rect, int edge)
    {
        scratch.Clear();
        for (var i = 0; i < polygon.Count; i++)
        {
            var current = polygon[i];
            var previous = polygon[(i + polygon.Count - 1) % polygon.Count];
            var dc = Inside(rect, edge, current);
            var dp = Inside(rect, edge, previous);
            if (dc >= 0)
            {
                if (dp < 0) scratch.Add(Lerp(previous, current, dp / (dp - dc)));
                scratch.Add(current);
            }
            else if (dp >= 0)
            {
                scratch.Add(Lerp(previous, current, dp / (dp - dc)));
            }
        }
        polygon.Clear();
        polygon.AddRange(scratch);
    }

    /// <summary>
    /// Clips a convex polygon to the rectangle (in place) and appends what is left as a triangle fan, every three points
    /// one triangle. Filled through a triangle array, the fill never reaches Godot's polygon triangulator, which rejects
    /// huge or degenerate polygons ("triangulation failed"). False when nothing usable is visible.
    /// </summary>
    public static bool AppendConvexFill(List<MapPoint> polygon, ClipRect rect, List<MapPoint> scratch, List<MapPoint> triangles)
    {
        ArgumentNullException.ThrowIfNull(triangles);
        ClipConvexPolygon(polygon, rect, scratch);
        if (!Simplify(polygon)) return false;
        for (var i = 1; i + 1 < polygon.Count; i++)
        {
            triangles.Add(polygon[0]);
            triangles.Add(polygon[i]);
            triangles.Add(polygon[i + 1]);
        }
        return true;
    }

    /// <summary>
    /// Removes repeated, collinear and back-tracking vertices (within <see cref="SnapPx"/>) in place. False, with the
    /// polygon emptied, when fewer than three vertices or less than <see cref="MinFillAreaPx2"/> of area remain.
    /// </summary>
    public static bool Simplify(List<MapPoint> polygon)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        var removed = true;
        while (removed && polygon.Count >= 3)
        {
            removed = false;
            for (var i = 0; i < polygon.Count && polygon.Count >= 3; i++)
            {
                var previous = polygon[(i + polygon.Count - 1) % polygon.Count];
                var next = polygon[(i + 1) % polygon.Count];
                var chord = next - previous;
                var offset = polygon[i] - previous;
                var chordLength = chord.Length;
                var off = chordLength > 0 ? Math.Abs((chord.X * offset.Y) - (chord.Y * offset.X)) / chordLength : offset.Length;
                if (off > SnapPx && polygon[i].DistanceTo(previous) > SnapPx) continue;
                polygon.RemoveAt(i);
                i--;
                removed = true;
            }
        }
        if (polygon.Count >= 3 && Math.Abs(SignedArea(polygon)) >= MinFillAreaPx2) return true;
        polygon.Clear();
        return false;
    }

    /// <summary>Shoelace area, positive for counter-clockwise in a Y-up frame.</summary>
    public static double SignedArea(IReadOnlyList<MapPoint> polygon)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        var sum = 0.0;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            sum += (a.X * b.Y) - (b.X * a.Y);
        }
        return sum / 2;
    }

    /// <summary>Signed distance inside one rectangle edge (0 left, 1 right, 2 top, 3 bottom).</summary>
    private static double Inside(ClipRect rect, int edge, MapPoint p) => edge switch
    {
        0 => p.X - rect.MinX,
        1 => rect.MaxX - p.X,
        2 => p.Y - rect.MinY,
        _ => rect.MaxY - p.Y,
    };

    private static double WrapPi(double angle)
    {
        var wrapped = Math.IEEERemainder(angle, TwoPi);
        return wrapped <= -Math.PI ? wrapped + TwoPi : wrapped;
    }

    private static MapPoint Lerp(MapPoint a, MapPoint b, double t) => a + ((b - a) * t);
}
