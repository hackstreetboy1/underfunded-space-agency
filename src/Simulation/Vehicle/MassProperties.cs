using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Vehicle;

/// <summary>Current mass properties of a vehicle's attached sections and contents.</summary>
/// <param name="CenterOfMassM">In the vehicle reference frame (the frame of thruster positions).</param>
/// <param name="InertiaKgM2">Principal moments about the centre of mass: X = roll, Y = pitch, Z = yaw. Products of inertia are neglected.</param>
public readonly record struct MassProperties(double MassKg, Vec3d CenterOfMassM, Vec3d InertiaKgM2);
