namespace Usa.Simulation.Kernel.Geometry;

/// <summary>Double-precision 2D vector for orbital mechanics (metres, m/s).</summary>
public readonly record struct Vec2d(double X, double Y)
{
    public static readonly Vec2d Zero = new(0, 0);
    public static readonly Vec2d UnitX = new(1, 0);
    public static readonly Vec2d UnitY = new(0, 1);

    public double Length => Math.Sqrt(X * X + Y * Y);

    public double LengthSquared => X * X + Y * Y;

    public double Angle => Math.Atan2(Y, X);

    public static Vec2d FromPolar(double length, double angleRad) =>
        new(length * Math.Cos(angleRad), length * Math.Sin(angleRad));

    public double Dot(Vec2d other) => X * other.X + Y * other.Y;

    /// <summary>Z component of the 3D cross product; positive when <paramref name="other"/> is counter-clockwise.</summary>
    public double Cross(Vec2d other) => X * other.Y - Y * other.X;

    public Vec2d Normalized()
    {
        var length = Length;
        return length > 0 ? new Vec2d(X / length, Y / length) : Zero;
    }

    /// <summary>Rotated 90° counter-clockwise.</summary>
    public Vec2d Perpendicular => new(-Y, X);

    public Vec2d Rotated(double angleRad)
    {
        var cos = Math.Cos(angleRad);
        var sin = Math.Sin(angleRad);
        return new Vec2d(X * cos - Y * sin, X * sin + Y * cos);
    }

    // The generated record ToString would print Perpendicular, a Vec2d, and recurse forever.
    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"({X:R}, {Y:R})");

    public static Vec2d operator +(Vec2d a, Vec2d b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2d operator -(Vec2d a, Vec2d b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2d operator -(Vec2d a) => new(-a.X, -a.Y);
    public static Vec2d operator *(Vec2d a, double s) => new(a.X * s, a.Y * s);
    public static Vec2d operator *(double s, Vec2d a) => new(a.X * s, a.Y * s);
    public static Vec2d operator /(Vec2d a, double s) => new(a.X / s, a.Y / s);
}
