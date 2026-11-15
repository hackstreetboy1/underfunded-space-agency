using Usa.Simulation.Ports;

namespace Usa.Simulation.Attitude;

/// <summary>Serves <c>attitude.&lt;vehicleId&gt;.&lt;field&gt;</c>; fields are listed in <see cref="AttitudeFields"/>.</summary>
/// <param name="sunInertialDeg">
/// The mission's fixed inertial Sun direction (same convention as <c>pointing_deg</c>); when given, <c>sun_pitch_deg</c> is
/// served for every vehicle. Null (no Sun, mission 1) leaves that field unknown.
/// </param>
public sealed class AttitudeStateProvider(AttitudeSystem system, double? sunInertialDeg = null) : IStateProvider
{
    public string Prefix => StatePaths.Attitude;

    public bool TryGet(string path, out StateValue value)
    {
        value = StateValue.None;
        return AttitudeFields.TrySplit(path, out var vehicleId, out var field) &&
               system.TryGetVehicle(vehicleId, out var vehicle) &&
               TryRead(vehicle, field, out value);
    }

    public bool IsKnownPath(string path) => TryGet(path, out _);

    private bool TryRead(AttitudeVehicle vehicle, ReadOnlySpan<char> field, out StateValue value)
    {
        switch (field)
        {
            case AttitudeFields.RateRollDps: value = vehicle.RateRollDegPerSec; return true;
            case AttitudeFields.RatePitchDps: value = vehicle.RatePitchDegPerSec; return true;
            case AttitudeFields.RateYawDps: value = vehicle.RateYawDegPerSec; return true;
            case AttitudeFields.RateTotalDps: value = vehicle.RateTotalDegPerSec; return true;
            case AttitudeFields.Mode: value = AttitudeModeText.ToText(vehicle.Mode); return true;
            case AttitudeFields.ControlSet: value = vehicle.ControlSetId; return true;
            case AttitudeFields.PointingDeg: value = vehicle.PointingDeg; return true;
            case AttitudeFields.OutOfPlaneDeg: value = vehicle.OutOfPlaneDeg; return true;
            case AttitudeFields.ErrorDeg: value = vehicle.AttitudeErrorDeg; return true;
            case AttitudeFields.SignedErrorDeg: value = vehicle.SignedErrorDeg; return true;
            case AttitudeFields.TargetDeg: value = vehicle.HoldTargetDeg; return true;
            case AttitudeFields.FiringDuty: value = vehicle.FiringDuty; return true;
            case AttitudeFields.SunPitchDeg when sunInertialDeg is { } sunDeg:
                value = PointingMath.SunPitchDeg(vehicle.PointingDeg, sunDeg);
                return true;
            default: value = StateValue.None; return false;
        }
    }
}
