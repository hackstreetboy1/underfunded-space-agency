namespace Usa.Simulation.Kernel.Geometry;

/// <summary>Double-precision 3D vector. Body frame convention: X = roll axis (nose), Y = pitch, Z = yaw.</summary>
public readonly record struct Vec3d(double X, double Y, double Z)
{
    public static readonly Vec3d Zero = new(0, 0, 0);
    public static readonly Vec3d UnitX = new(1, 0, 0);
    public static readonly Vec3d UnitY = new(0, 1, 0);
    public static readonly Vec3d UnitZ = new(0, 0, 1);

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    public double LengthSquared => X * X + Y * Y + Z * Z;

    public double Dot(Vec3d other) => X * other.X + Y * other.Y + Z * other.Z;

    public Vec3d Cross(Vec3d other) =>
        new(Y * other.Z - Z * other.Y, Z * other.X - X * other.Z, X * other.Y - Y * other.X);

    public Vec3d Normalized()
    {
        var length = Length;
        return length > 0 ? this / length : Zero;
    }

    /// <summary>Component-wise product (e.g. diagonal inertia times angular velocity).</summary>
    public Vec3d Scale(Vec3d other) => new(X * other.X, Y * other.Y, Z * other.Z);

    public static Vec3d operator +(Vec3d a, Vec3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3d operator -(Vec3d a, Vec3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3d operator -(Vec3d a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3d operator *(Vec3d a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3d operator *(double s, Vec3d a) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3d operator /(Vec3d a, double s) => new(a.X / s, a.Y / s, a.Z / s);
}
