using Usa.Simulation.Kernel;

namespace Usa.Simulation.Flight;

/// <summary>
/// What the ground believes about one vehicle's orbit (X8): the last radar fix propagated with Kepler, with every
/// commanded burn applied at the ledger mass. Built from tracking only; the truth path is never applied to it.
/// </summary>
/// <param name="Time">When <see cref="State"/> holds (the estimate may trail the clock by less than a step).</param>
/// <param name="SigmaAlongM">1σ along-track position error now (difficulty scale included).</param>
/// <param name="SigmaRadialM">1σ radial position error now.</param>
/// <param name="FixAt">The last radar fix; null before the first.</param>
/// <param name="FixStationId">The station of the last fix; null for the launch fix.</param>
/// <param name="Estimated">A commanded burn has been applied since the fix: shown as EST · ground predict.</param>
/// <param name="Stale">No fix for longer than <see cref="NavigationConfig.StaleAfterS"/>.</param>
/// <param name="NextFixAt">When the next fix is expected; null while that can't be predicted (burning, in the atmosphere).</param>
public sealed record NavEstimate(
    string VehicleId,
    MissionTime Time,
    FlightPhase Phase,
    StateVector State,
    Conic Conic,
    double PeriapsisKm,
    double ApoapsisKm,
    double SigmaAlongM,
    double SigmaRadialM,
    MissionTime? FixAt,
    string? FixStationId,
    bool Estimated,
    bool Stale,
    MissionTime? NextFixAt);

/// <summary>The ground's mass for a vehicle (X8 ledger); the Knowledge <c>GroundMassLedger</c> supplies it through the session.</summary>
public interface IGroundMassSource
{
    /// <summary>The ledger's best mass and its band edges for <paramref name="vehicleId"/> (the stack while docked).</summary>
    bool TryGetMass(string vehicleId, out double lowKg, out double bestKg, out double highKg);
}
