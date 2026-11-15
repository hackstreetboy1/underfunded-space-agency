using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Attitude;

/// <summary>A rigid body and the vehicles in it: one for a free vehicle, primary then secondary for a docked stack.</summary>
internal sealed class AttitudeBody(RigidBodyAttitude rigid, AttitudeVehicle[] members)
{
    public RigidBodyAttitude Rigid { get; } = rigid;

    public AttitudeVehicle[] Members { get; } = members;

    /// <summary>Sort key: the first member's id, which is unique because a vehicle is in exactly one body.</summary>
    public string Key => Members[0].Id;

    /// <summary>Net thruster force over the last step, body frame.</summary>
    public Vec3d NetForceN { get; set; }
}
