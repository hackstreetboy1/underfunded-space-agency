using Usa.Game.Presentation.Map;

namespace Usa.Game.UI.Map.Geometry;

/// <summary>Drawn-space helpers for the rim arcs, the marker tick and dashed polylines.</summary>
public static class RimGeometry
{
    /// <summary>Points along a circle of <paramref name="radiusPx"/> from one screen angle to another (clockwise-positive).</summary>
    public static MapPoint[] Arc(AltitudeExaggeration projection, double radiusPx, double startScreenRad, double endScreenRad, double stepRad = 0.02)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stepRad);
        var steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(endScreenRad - startScreenRad) / stepRad));
        var points = new MapPoint[steps + 1];
        for (var i = 0; i <= steps; i++)
            points[i] = projection.OnCircle(radiusPx, startScreenRad + ((endScreenRad - startScreenRad) * i / steps));
        return points;
    }

    /// <summary>
    /// The marker's along-track tick (X8: ±2σ along track, at least 3 px): a segment through <paramref name="position"/>
    /// along <paramref name="directionScreen"/>.
    /// </summary>
    public static (MapPoint From, MapPoint To) AlongTrackTick(MapPoint position, MapPoint directionScreen, double halfLengthPx)
    {
        var unit = directionScreen.Normalized();
        var half = Math.Max(ExaggeratedBand.MinHalfWidthPx, halfLengthPx);
        return (position - (unit * half), position + (unit * half));
    }

    /// <summary>Splits a polyline into dash segments (<c>draw_dashed_line</c> only handles one straight segment).</summary>
    public static List<(MapPoint From, MapPoint To)> Dashes(IReadOnlyList<MapPoint> points, double dashPx, double gapPx)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dashPx);
        ArgumentOutOfRangeException.ThrowIfNegative(gapPx);
        var dashes = new List<(MapPoint, MapPoint)>();
        var drawing = true;
        var left = dashPx;
        for (var i = 1; i < points.Count; i++)
        {
            var from = points[i - 1];
            var segment = points[i] - from;
            var length = segment.Length;
            if (length == 0) continue;
            var unit = segment * (1 / length);
            var at = 0.0;
            while (at < length)
            {
                var step = Math.Min(left, length - at);
                if (drawing) dashes.Add((from + (unit * at), from + (unit * (at + step))));
                at += step;
                left -= step;
                if (left <= 1e-9)
                {
                    drawing = !drawing || gapPx == 0;
                    left = drawing ? dashPx : gapPx;
                }
            }
        }
        return dashes;
    }
}
