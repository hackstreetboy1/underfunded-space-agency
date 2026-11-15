using Usa.Simulation.Kernel;

namespace Usa.Simulation.Knowledge;

/// <summary>
/// Entry instrument facts (X8): the accelerometer and the cabin altimeter, downlinked as precise telemetry readings at
/// 1 Hz. Definitions live in the mission fact catalog; these are the ids and the altimeter's valid range.
/// </summary>
public static class EntryFacts
{
    /// <summary>Accelerometer reading in g.</summary>
    public static readonly FactId GLoad = new("entry.g_load");

    /// <summary>Cabin (barometric) altimeter reading in km; only valid at or below <see cref="AltimeterCeilingKm"/>.</summary>
    public static readonly FactId AltitudeKm = new("entry.altitude_km");

    public const double AltimeterCeilingKm = 30;

    /// <summary>Entry readings go stale after this long without a new sample.</summary>
    public const double StaleAfterS = 5;
}
