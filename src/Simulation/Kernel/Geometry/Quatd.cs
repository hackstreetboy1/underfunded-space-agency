namespace Usa.Simulation.Kernel.Geometry;

/// <summary>Double-precision unit quaternion for body orientation (body → inertial).</summary>
public readonly record struct Quatd(double W, double X, double Y, double Z)
{
    public static readonly Quatd Identity = new(1, 0, 0, 0);

    public double Length => Math.Sqrt(W * W + X * X + Y * Y + Z * Z);

    public static Quatd FromAxisAngle(Vec3d axis, double angleRad)
    {
        var unit = axis.Normalized();
        var half = angleRad / 2;
        var sin = Math.Sin(half);
        return new Quatd(Math.Cos(half), unit.X * sin, unit.Y * sin, unit.Z * sin);
    }

    public Quatd Normalized()
    {
        var length = Length;
        return length > 0 ? new Quatd(W / length, X / length, Y / length, Z / length) : Identity;
    }

    public Quatd Conjugate => new(W, -X, -Y, -Z);

    // The generated record ToString would print Conjugate, a Quatd, and recurse forever.
    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"({W:R}, {X:R}, {Y:R}, {Z:R})");

    public Vec3d Rotate(Vec3d v)
    {
        var u = new Vec3d(X, Y, Z);
        var t = 2.0 * u.Cross(v);
        return v + W * t + u.Cross(t);
    }

    /// <summary>Time derivative for body-frame angular velocity <paramref name="omega"/> (rad/s).</summary>
    public Quatd Derivative(Vec3d omega) => this * new Quatd(0, omega.X, omega.Y, omega.Z) * 0.5;

    public static Quatd operator *(Quatd a, Quatd b) => new(
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z,
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W);

    public static Quatd operator *(Quatd q, double s) => new(q.W * s, q.X * s, q.Y * s, q.Z * s);

    public static Quatd operator +(Quatd a, Quatd b) => new(a.W + b.W, a.X + b.X, a.Y + b.Y, a.Z + b.Z);
}
