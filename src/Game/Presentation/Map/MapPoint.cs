namespace Usa.Game.Presentation.Map;

/// <summary>A point or vector in screen pixels: origin top-left, Y down (Godot canvas convention).</summary>
public readonly record struct MapPoint(double X, double Y)
{
    public double Length => Math.Sqrt(X * X + Y * Y);

    public MapPoint Normalized()
    {
        var length = Length;
        return length > 0 ? new MapPoint(X / length, Y / length) : default;
    }

    public double DistanceTo(MapPoint other) => (this - other).Length;

    /// <summary>Rotated 90° clockwise on screen (counter-clockwise in Y-up maths).</summary>
    public MapPoint Perpendicular => new(-Y, X);

    // The generated record ToString would print Perpendicular, a MapPoint, and recurse forever.
    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"({X:R}, {Y:R})");

    public static MapPoint operator +(MapPoint a, MapPoint b) => new(a.X + b.X, a.Y + b.Y);

    public static MapPoint operator -(MapPoint a, MapPoint b) => new(a.X - b.X, a.Y - b.Y);

    public static MapPoint operator *(MapPoint a, double s) => new(a.X * s, a.Y * s);

    public static MapPoint operator *(double s, MapPoint a) => new(a.X * s, a.Y * s);
}
