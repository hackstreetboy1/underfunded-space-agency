using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Attitude;

/// <summary>
/// One thruster as the attitude model sees it; the integration step maps vehicle data onto it.
/// Geometry is in the vehicle body frame (X nose/roll, Y pitch, Z yaw).
/// </summary>
/// <param name="GroupId">Thruster group, e.g. <c>tandem/rcs/ring_a</c>; empty when the thruster has none.</param>
/// <param name="TankId">Tank the propellant comes from, e.g. <c>tandem/oams/tank</c>.</param>
/// <param name="PositionM">Nozzle position relative to the vehicle's own centre of mass.</param>
/// <param name="DirectionUnit">Direction of the force on the vehicle (opposite to the plume).</param>
public sealed record ThrusterSpec(
    string Id,
    string GroupId,
    string TankId,
    Vec3d PositionM,
    Vec3d DirectionUnit,
    double ThrustN,
    double IspS);
