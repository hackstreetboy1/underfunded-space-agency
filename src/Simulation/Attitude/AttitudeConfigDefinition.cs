using Usa.Simulation.Data;

namespace Usa.Simulation.Attitude;

/// <summary>Root of <c>data/config/attitude.json</c> (schema <c>attitude/1</c>).</summary>
/// <param name="RateThresholdsDegPerSec">Total-rate thresholds that raise <see cref="RateThresholdCrossed"/>, ascending.</param>
/// <param name="MinAxisTorqueFraction">
/// A thruster counts as acting on an axis only when that torque component exceeds this fraction of its
/// total torque, so tiny cross-couplings don't decide thruster selection.
/// </param>
/// <param name="FiringDutyTimeConstantS">Smoothing of the reported firing duty (status panels, plume cues).</param>
public sealed record AttitudeConfigDefinition(
    string Schema,
    IReadOnlyList<double> RateThresholdsDegPerSec,
    double ThresholdHysteresisDegPerSec,
    double MinAxisTorqueFraction,
    double FiringDutyTimeConstantS,
    RateDampDefinition RateDamp,
    AttitudeHoldDefinition AttitudeHold,
    IReadOnlyList<AttitudeVehicleDefinition> Vehicles,
    IReadOnlyList<DockingDefinition> Docking) : IVersionedDefinition;

/// <summary>Rate loop: an axis starts firing above the deadband and stops once inside the settle rate.</summary>
/// <param name="RateTimeConstantS">Time over which one step's command aims to null the rate error (at least two 50 ms controller steps).</param>
public sealed record RateDampDefinition(double DeadbandDegPerSec, double SettleDegPerSec, double RateTimeConstantS);

/// <summary>
/// Pointing loop around the rate loop: the commanded slew rate is <c>PointingGainPerS × error</c>,
/// limited to <c>MaxSlewDegPerSec</c>; the rate loop then fires thrusters bang-bang to follow it.
/// </summary>
public sealed record AttitudeHoldDefinition(
    double DeadbandDegPerSec,
    double SettleDegPerSec,
    double RateTimeConstantS,
    double PointingGainPerS,
    double MaxSlewDegPerSec);

/// <summary>Controller set-up for one vehicle.</summary>
public sealed record AttitudeVehicleDefinition(
    string Id,
    AttitudeMode InitialMode,
    string InitialControlSet,
    IReadOnlyList<ControlSetDefinition> ControlSets);

/// <summary>
/// The thrusters a controller may use in one configuration (e.g. <c>oams</c>, <c>rcs</c>): listed ids
/// plus every thruster whose group is listed.
/// </summary>
public sealed record ControlSetDefinition(
    string Id,
    IReadOnlyList<string>? Thrusters = null,
    IReadOnlyList<string>? Groups = null);

/// <summary>Docked geometry of a vehicle pair. The primary's body frame becomes the stack frame.</summary>
/// <param name="SecondaryComOffsetXM">Secondary centre of mass along the primary's X axis from the primary's centre of mass.</param>
/// <param name="SecondaryReversed">The secondary faces the other way (nose to nose), turned half a turn about yaw.</param>
public sealed record DockingDefinition(
    string Primary,
    string Secondary,
    double SecondaryComOffsetXM,
    bool SecondaryReversed);
