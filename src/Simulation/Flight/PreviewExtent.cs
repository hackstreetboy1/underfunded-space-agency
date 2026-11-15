using Usa.Simulation.Kernel;

namespace Usa.Simulation.Flight;

/// <summary>How far <see cref="ManeuverPlanner.Fly"/> follows the trajectory after the burn.</summary>
internal enum PreviewExtent
{
    /// <summary>No entry prediction: the resulting conic only.</summary>
    Burn,

    /// <summary>Stops at the entry interface; the corridor angle is known, the landing is not.</summary>
    Interface,

    /// <summary>Stops at the drogue's nominal deploy altitude; from there the capsule drifts with the air.</summary>
    Drogue,

    /// <summary>The full preview, to the ground (or the horizon).</summary>
    Landing,
}

internal readonly record struct TimedState(MissionTime Time, StateVector State);

/// <summary>States either side of a previewed burn and where the preview stopped.</summary>
/// <param name="Burned">False when the vehicle landed before the start, so nothing was burned.</param>
/// <param name="DroguePoint">The first integrated state at or below the drogue's nominal altitude, when the preview got there.</param>
internal readonly record struct PreviewTrace(
    bool Burned, TimedState PreBurn, TimedState PostBurn, TimedState Stop, FlightPhase StopPhase, CelestialBody StopBody, TimedState? DroguePoint);
