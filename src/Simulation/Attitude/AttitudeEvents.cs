using Usa.Simulation.Kernel;

namespace Usa.Simulation.Attitude;

/// <summary>A vehicle's control mode or its enabled thruster set changed.</summary>
public sealed record AttitudeModeChanged(
    MissionTime Time,
    string VehicleId,
    AttitudeMode PreviousMode,
    AttitudeMode Mode,
    string PreviousControlSet,
    string ControlSet) : ISimEvent;

/// <summary>
/// A vehicle's total body rate crossed a configured threshold. Rising when it goes above the threshold;
/// falling once it drops below the threshold minus the configured hysteresis.
/// </summary>
public sealed record RateThresholdCrossed(
    MissionTime Time,
    string VehicleId,
    double ThresholdDegPerSec,
    double RateDegPerSec,
    bool Rising) : ISimEvent;
