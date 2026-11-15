using Usa.Simulation.Kernel;

namespace Usa.Simulation.Flight;

public sealed record BurnStarted(MissionTime Time, string VehicleId) : ISimEvent;

/// <summary>
/// End of a powered segment. Flight lowers its own copy of the mass only to integrate the burn; the vehicle area owns
/// propellant and is the only one that deducts it. <see cref="ExternalPropellantKg"/> came from thrust the host supplied
/// (translation thrusters, a docked target's engine) and was already drawn by whoever supplied it; <see cref="ProgrammedPropellantKg"/>
/// came from burns flight scheduled itself (retro sequence, planned burns) and still has to be deducted by the host.
/// </summary>
/// <param name="PropellantUsedKg">Total over the segment: external plus programmed.</param>
public sealed record BurnEnded(
    MissionTime Time, string VehicleId, double DeltaVMps, double PropellantUsedKg, double ProgrammedPropellantKg = 0) : ISimEvent
{
    public double ExternalPropellantKg => PropellantUsedKg - ProgrammedPropellantKg;
}

public sealed record SoiChanged(MissionTime Time, string VehicleId, string FromBodyId, string ToBodyId) : ISimEvent;

/// <param name="Entering">True when descending through the interface, false on a skip-out.</param>
public sealed record AtmosphereInterfaceCrossed(
    MissionTime Time, string VehicleId, bool Entering, double FlightPathAngleDeg, double SpeedMps) : ISimEvent;

public sealed record ChuteDeployed(
    MissionTime Time, string VehicleId, ChuteKind Chute, ChuteDeployResult Result, double AltitudeM, double AirspeedMps) : ISimEvent;

/// <param name="ZoneId">Null when outside every recovery zone.</param>
public sealed record Splashdown(
    MissionTime Time, string VehicleId, string BodyId, double LandingLongitudeDeg, string? ZoneId,
    double RecoveryWaitHours, double ImpactSpeedMps) : ISimEvent;

/// <param name="CrewedId">The crewed vehicle heading the stack (the mission's <see cref="FlightVehicles.CrewedId"/>).</param>
/// <param name="TargetId">The target vehicle that joined or left the stack.</param>
public sealed record DockingChanged(MissionTime Time, bool Docked, string CrewedId = "", string TargetId = "") : ISimEvent;
