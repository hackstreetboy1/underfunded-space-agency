using Usa.Simulation.Kernel;

namespace Usa.Simulation.Flight;

/// <summary>A read-only snapshot of one vehicle's flight for queries, displays and the caller's thrust logic.</summary>
/// <param name="AirspeedMps">Speed relative to the rotating atmosphere/surface.</param>
/// <param name="GLoad">Non-gravitational acceleration over g0 (thrust and drag); 1 once landed.</param>
/// <param name="HeatingProxy">√ρ |v|³ in SI units; zero outside the atmosphere.</param>
public readonly record struct FlightKinematics(
    MissionTime Time,
    FlightPhase Phase,
    string BodyId,
    StateVector State,
    double AltitudeM,
    double AirspeedMps,
    double GLoad,
    double HeatingProxy)
{
    public double SpeedMps => State.SpeedMps;

    public double FlightPathAngleRad => State.FlightPathAngleRad;
}

/// <summary>Where and how a vehicle came down.</summary>
public readonly record struct LandingSite(MissionTime Time, string BodyId, double ImpactAngleRad, double ImpactSpeedMps, ZoneEvaluation Zone);

/// <summary>An entry-interface crossing.</summary>
public readonly record struct InterfaceCrossing(MissionTime Time, double FlightPathAngleRad, double SpeedMps, bool Entering);
