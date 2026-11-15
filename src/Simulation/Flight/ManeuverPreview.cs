using Usa.Simulation.Kernel;

namespace Usa.Simulation.Flight;

/// <summary>What a planned burn would do, for the map preview.</summary>
/// <param name="RemainingDeltaVMps">Δv the engine's leftover propellant could still give after this burn.</param>
/// <param name="PropellantLimited">The engine ran dry before the planned duration.</param>
/// <param name="Entry">Null when the resulting trajectory never reaches the entry interface within the horizon.</param>
/// <param name="Dispersion">Where the landing may really fall given what the ground knows (X8); null for a preview without <see cref="PreviewUncertainty"/> or without a landing.</param>
public sealed record ManeuverPreview(
    ManeuverPlan Plan,
    double DeltaVMps,
    double PropellantUsedKg,
    double RemainingDeltaVMps,
    bool PropellantLimited,
    Conic ResultingConic,
    double PeriapsisKm,
    double ApoapsisKm,
    EntryPrediction? Entry,
    LandingDispersion? Dispersion = null)
{
    public CorridorQuality Corridor => Entry?.Corridor ?? CorridorQuality.NoEntry;

    public ZoneEvaluation? LandingZone => Entry?.Landing?.Zone;
}

/// <summary>Predicted entry: corridor at the interface, loads on the way down and where it lands.</summary>
/// <param name="FlightPathAngleDeg">
/// At the mission's entry interface altitude, from the coast conic (relative to the body entered) of the arc that
/// reaches the atmosphere; for a burn in that body's sphere of influence this is the post-burn conic.
/// </param>
/// <param name="Landing">Null if the vehicle has not landed by the preview horizon (e.g. after a skip-out).</param>
public sealed record EntryPrediction(
    MissionTime InterfaceTime,
    double FlightPathAngleDeg,
    double InterfaceSpeedMps,
    CorridorQuality Corridor,
    double PeakGLoad,
    double PeakHeatingProxy,
    LandingSite? Landing);
