using Usa.Simulation.Ports;

namespace Usa.Simulation.Attitude;

/// <summary>
/// Fields served under <c>attitude.&lt;vehicleId&gt;.</c>. Rates are signed, in the vehicle's own body frame.
/// Pointing is the vehicle X axis projected onto the orbital plane (inertial, [0, 360)).
/// </summary>
public static class AttitudeFields
{
    public const string RateRollDps = "rate_roll_dps";
    public const string RatePitchDps = "rate_pitch_dps";
    public const string RateYawDps = "rate_yaw_dps";
    public const string RateTotalDps = "rate_total_dps";

    /// <summary>'off' | 'rate_damp' | 'attitude_hold'. Settable by effects.</summary>
    public const string Mode = "mode";

    /// <summary>Enabled thruster set, e.g. 'oams' or 'rcs'; '' for none. Settable by effects.</summary>
    public const string ControlSet = "control_set";

    public const string PointingDeg = "pointing_deg";

    /// <summary>Elevation of the X axis above the orbital plane; thrust along X loses cos of this in the plane.</summary>
    public const string OutOfPlaneDeg = "out_of_plane_deg";

    /// <summary>Angle between the X axis and the hold target; 0 outside attitude hold.</summary>
    public const string ErrorDeg = "error_deg";

    /// <summary>Signed in-plane error from the hold target, (-180, 180], positive counter-clockwise; 0 outside attitude hold.</summary>
    public const string SignedErrorDeg = "signed_error_deg";

    /// <summary>Hold target in-plane angle. Settable (set/add) by effects.</summary>
    public const string TargetDeg = "target_deg";

    /// <summary>Smoothed mean valve fraction over the vehicle's thrusters, 0..1 (e.g. Lodestar ACS activity).</summary>
    public const string FiringDuty = "firing_duty";

    /// <summary>Sun pitch, (-180, 180], 0 when the arrays face the Sun; served only when the mission has a Sun (<c>flight.json</c> <c>sun</c>).</summary>
    public const string SunPitchDeg = "sun_pitch_deg";

    /// <summary>Splits "attitude.&lt;vehicleId&gt;.&lt;field&gt;" without allocating.</summary>
    internal static bool TrySplit(string path, out ReadOnlySpan<char> vehicleId, out ReadOnlySpan<char> field)
    {
        vehicleId = default;
        field = default;
        if (!path.StartsWith(StatePaths.Attitude, StringComparison.Ordinal)) return false;
        var rest = path.AsSpan(StatePaths.Attitude.Length);
        var dot = rest.LastIndexOf('.');
        if (dot <= 0 || dot == rest.Length - 1) return false;
        vehicleId = rest[..dot];
        field = rest[(dot + 1)..];
        return true;
    }
}
