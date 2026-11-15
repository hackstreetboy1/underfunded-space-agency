using Usa.Game.Presentation.Map;

namespace Usa.Game.UI.Map.Geometry;

/// <summary>An axis-aligned screen rectangle (pixels, Y down) that drawn geometry is clipped to.</summary>
public readonly record struct ClipRect(double MinX, double MinY, double MaxX, double MaxY)
{
    /// <summary>The viewport grown by <paramref name="marginPx"/> on every side, so strokes and joins that cross the edge still look whole.</summary>
    public static ClipRect Viewport(double widthPx, double heightPx, double marginPx) =>
        new(-marginPx, -marginPx, widthPx + marginPx, heightPx + marginPx);

    public MapPoint Centre => new((MinX + MaxX) / 2, (MinY + MaxY) / 2);

    public bool Contains(MapPoint point) => point.X >= MinX && point.X <= MaxX && point.Y >= MinY && point.Y <= MaxY;

    public bool Overlaps(double minX, double minY, double maxX, double maxY) =>
        maxX >= MinX && minX <= MaxX && maxY >= MinY && minY <= MaxY;

    /// <summary>Distance from <paramref name="point"/> to the nearest point of the rectangle (0 inside).</summary>
    public double DistanceTo(MapPoint point)
    {
        var dx = Math.Max(Math.Max(MinX - point.X, 0), point.X - MaxX);
        var dy = Math.Max(Math.Max(MinY - point.Y, 0), point.Y - MaxY);
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>Distance from <paramref name="point"/> to the farthest corner.</summary>
    public double FarthestCornerDistance(MapPoint point)
    {
        var dx = Math.Max(Math.Abs(point.X - MinX), Math.Abs(point.X - MaxX));
        var dy = Math.Max(Math.Abs(point.Y - MinY), Math.Abs(point.Y - MaxY));
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>Liang–Barsky: the parameter range [<paramref name="t0"/>, <paramref name="t1"/>] of segment a→b inside the rectangle.</summary>
    public bool ClipSegment(MapPoint a, MapPoint b, out double t0, out double t1)
    {
        t0 = 0;
        t1 = 1;
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Edge(-dx, a.X - MinX, ref t0, ref t1)
            && Edge(dx, MaxX - a.X, ref t0, ref t1)
            && Edge(-dy, a.Y - MinY, ref t0, ref t1)
            && Edge(dy, MaxY - a.Y, ref t0, ref t1);
    }

    private static bool Edge(double p, double q, ref double t0, ref double t1)
    {
        if (p == 0) return q >= 0;
        var r = q / p;
        if (p < 0)
        {
            if (r > t1) return false;
            if (r > t0) t0 = r;
        }
        else
        {
            if (r < t0) return false;
            if (r < t1) t1 = r;
        }
        return true;
    }
}
