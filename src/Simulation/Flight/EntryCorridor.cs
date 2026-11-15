namespace Usa.Simulation.Flight;

/// <summary>Acceptable flight-path angles at the entry interface. Both limits are negative (descending).</summary>
public sealed record EntryCorridor(double InterfaceAltitudeM, double SteepestFlightPathAngleDeg, double ShallowestFlightPathAngleDeg)
{
    public double CentreFlightPathAngleDeg => (SteepestFlightPathAngleDeg + ShallowestFlightPathAngleDeg) / 2;

    public CorridorQuality Classify(double flightPathAngleDeg) =>
        flightPathAngleDeg > ShallowestFlightPathAngleDeg ? CorridorQuality.TooShallow
        : flightPathAngleDeg < SteepestFlightPathAngleDeg ? CorridorQuality.TooSteep
        : CorridorQuality.Ok;
}

public enum CorridorQuality
{
    /// <summary>The trajectory never reaches the entry interface.</summary>
    NoEntry,
    TooShallow,
    Ok,
    TooSteep,
}
