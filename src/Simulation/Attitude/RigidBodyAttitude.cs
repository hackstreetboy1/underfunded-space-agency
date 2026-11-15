using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Attitude;

/// <summary>
/// Rotational state of one rigid body with diagonal inertia: body-frame angular velocity and the
/// body → inertial orientation. The inertial X-Y plane is the orbital plane of the 2D map.
/// </summary>
public sealed class RigidBodyAttitude : IHashContributor
{
    public RigidBodyAttitude(Vec3d inertiaKgM2)
    {
        SetInertia(inertiaKgM2);
        Orientation = Quatd.Identity;
    }

    public Vec3d InertiaKgM2 { get; private set; }

    public Vec3d AngularVelocityRadPerSec { get; private set; }

    /// <summary>Body → inertial, unit length.</summary>
    public Quatd Orientation { get; private set; }

    public Vec3d AngularMomentumBodyNms => InertiaKgM2.Scale(AngularVelocityRadPerSec);

    public Vec3d AngularMomentumInertialNms => Orientation.Rotate(AngularMomentumBodyNms);

    public double RotationalEnergyJ => 0.5 * AngularVelocityRadPerSec.Dot(AngularMomentumBodyNms);

    public void SetInertia(Vec3d inertiaKgM2)
    {
        if (!MassProperties.IsValidInertia(inertiaKgM2))
            throw new ArgumentOutOfRangeException(nameof(inertiaKgM2), inertiaKgM2, "Inertia must be positive and finite.");
        InertiaKgM2 = inertiaKgM2;
    }

    public void SetState(Quatd orientation, Vec3d angularVelocityRadPerSec)
    {
        var length = orientation.Length;
        if (!(double.IsFinite(length) && length > 0)) throw new ArgumentException("Orientation must be a finite, non-zero quaternion.", nameof(orientation));
        if (!double.IsFinite(angularVelocityRadPerSec.LengthSquared))
            throw new ArgumentOutOfRangeException(nameof(angularVelocityRadPerSec), angularVelocityRadPerSec, "Angular velocity must be finite.");
        Orientation = orientation.Normalized();
        AngularVelocityRadPerSec = angularVelocityRadPerSec;
    }

    /// <summary>
    /// Advances one step with RK4 on Euler's equations I ω̇ = τ − ω × (I ω) and q̇ = ½ q ⊗ ω,
    /// holding the body-frame torque constant over the step.
    /// </summary>
    public void Step(Vec3d torqueNm, double dtS)
    {
        var halfDt = dtS * 0.5;
        var q0 = Orientation;
        var w0 = AngularVelocityRadPerSec;

        var k1W = AngularAcceleration(w0, torqueNm);
        var k1Q = q0.Derivative(w0);

        var w1 = w0 + k1W * halfDt;
        var k2W = AngularAcceleration(w1, torqueNm);
        var k2Q = (q0 + k1Q * halfDt).Derivative(w1);

        var w2 = w0 + k2W * halfDt;
        var k3W = AngularAcceleration(w2, torqueNm);
        var k3Q = (q0 + k2Q * halfDt).Derivative(w2);

        var w3 = w0 + k3W * dtS;
        var k4W = AngularAcceleration(w3, torqueNm);
        var k4Q = (q0 + k3Q * dtS).Derivative(w3);

        var sixthDt = dtS / 6.0;
        AngularVelocityRadPerSec = w0 + (k1W + k2W * 2.0 + k3W * 2.0 + k4W) * sixthDt;
        Orientation = (q0 + (k1Q + k2Q * 2.0 + k3Q * 2.0 + k4Q) * sixthDt).Normalized();
    }

    public void WriteHash(StateHashWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(InertiaKgM2.X);
        writer.Write(InertiaKgM2.Y);
        writer.Write(InertiaKgM2.Z);
        writer.Write(AngularVelocityRadPerSec.X);
        writer.Write(AngularVelocityRadPerSec.Y);
        writer.Write(AngularVelocityRadPerSec.Z);
        writer.Write(Orientation.W);
        writer.Write(Orientation.X);
        writer.Write(Orientation.Y);
        writer.Write(Orientation.Z);
    }

    private Vec3d AngularAcceleration(Vec3d omega, Vec3d torqueNm)
    {
        var inertia = InertiaKgM2;
        var gyroscopic = omega.Cross(inertia.Scale(omega));
        return new Vec3d(
            (torqueNm.X - gyroscopic.X) / inertia.X,
            (torqueNm.Y - gyroscopic.Y) / inertia.Y,
            (torqueNm.Z - gyroscopic.Z) / inertia.Z);
    }
}
