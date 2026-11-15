using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Attitude;

/// <summary>
/// Where a vehicle sits in the rigid body it belongs to. A free vehicle uses <see cref="Identity"/>.
/// In a stack, the vehicle's centre of mass is <see cref="OffsetXM"/> along the stack X axis from the
/// stack centre of mass, and <see cref="Reversed"/> means it is turned half a turn about yaw (nose to nose).
/// </summary>
public readonly record struct StackMount(double OffsetXM, bool Reversed)
{
    public static readonly StackMount Identity = new(0, false);

    /// <summary>Vehicle frame → stack frame rotation.</summary>
    public Quatd Rotation => Reversed ? new Quatd(0, 0, 0, 1) : Quatd.Identity;

    /// <summary>Rotates a vehicle-frame vector into the stack frame.</summary>
    public Vec3d ToStack(Vec3d vehicleVector) =>
        Reversed ? new Vec3d(-vehicleVector.X, -vehicleVector.Y, vehicleVector.Z) : vehicleVector;

    /// <summary>Rotates a stack-frame vector into the vehicle frame.</summary>
    public Vec3d FromStack(Vec3d stackVector) => ToStack(stackVector); // a half turn is its own inverse

    /// <summary>Maps a point relative to the vehicle's centre of mass to a point relative to the stack's.</summary>
    public Vec3d PointToStack(Vec3d vehiclePointM)
    {
        var rotated = ToStack(vehiclePointM);
        return new Vec3d(rotated.X + OffsetXM, rotated.Y, rotated.Z);
    }
}
