namespace Usa.Game.Presentation.Spacecraft;

/// <summary>
/// The shape of the docking approach as a function of the crew's task progress (0..1), free of Godot so it can be checked
/// on its own. Like a real final approach: hold at the standoff while the pair lines up, a translation burn to accelerate,
/// a coast, a braking burn, then a slow close to contact at progress 1. Lateral and attitude errors from the line-up are
/// flown out with small pulses on the way in. Everything is a pure function of progress, never of frame time, so the
/// approach looks the same at any time-compression rate and holds still while frozen.
/// </summary>
public static class DockingApproachProfile
{
    /// <summary>End of the line-up: the pair settles from where it was shown onto the docking axis.</summary>
    public const double AlignEnd = 0.10;

    public const double AccelEnd = 0.30;
    public const double BrakeStart = 0.68;
    public const double BrakeEnd = 0.90;

    /// <summary>Closing speed after the braking burn, as a share of the coast speed (contact is gentle, not stopped).</summary>
    public const double FinalSpeedShare = 0.18;

    /// <summary>Thruster pulses per translation burn.</summary>
    public const int PulsesPerBurn = 8;

    /// <summary>Share of each pulse slot the jets are open.</summary>
    public const double PulseDuty = 0.5;

    // Lateral error (body axes across the docking axis, display metres) and attitude error (radians) at the line-up;
    // the first lateral axis is flown out early, the second later, so the corrections read as separate pulses.
    public const double LateralStart1 = 0.10;
    public const double LateralStop1 = 0.62;
    public const double LateralStart2 = 0.40;
    public const double LateralStop2 = 0.82;
    public const double AttitudeStart = 0.03;
    public const double AttitudeStop = 0.50;

    /// <summary>Length of a correction pulse, as progress.</summary>
    public const double CorrectionPulse = 0.016;

    // Small trims on the way in (progress, cross axis, +1 towards the axis / -1 away), between the main corrections.
    private static readonly (double At, int Axis, int Sign)[] TrimPuffs =
    [
        (0.33, 2, -1), (0.45, 1, 1), (0.52, 2, 1), (0.57, 1, -1), (0.75, 2, -1), (0.86, 1, 1), (0.95, 2, 1),
    ];

    private static readonly double FullDistance = Flown(1);

    /// <summary>Share 1..0 of the standoff still to close: 1 through the line-up, 0 at contact.</summary>
    public static double RemainingShare(double progress) => 1 - Flown(Math.Clamp(progress, 0, 1)) / FullDistance;

    /// <summary>Closing speed as a share of the coast speed (0 while lining up, 1 coasting).</summary>
    public static double SpeedShare(double progress)
    {
        var p = Math.Clamp(progress, 0, 1);
        if (p <= AlignEnd) return 0;
        if (p <= AccelEnd) return (p - AlignEnd) / (AccelEnd - AlignEnd);
        if (p <= BrakeStart) return 1;
        if (p <= BrakeEnd) return 1 + (FinalSpeedShare - 1) * (p - BrakeStart) / (BrakeEnd - BrakeStart);
        return FinalSpeedShare;
    }

    /// <summary>Remaining lateral error along the two cross axes, as a share 1..0 of the line-up error.</summary>
    public static (double Axis1, double Axis2) LateralShare(double progress) =>
        (1 - SmoothStep(LateralStart1, LateralStop1, progress), 1 - SmoothStep(LateralStart2, LateralStop2, progress));

    /// <summary>Remaining attitude error as a share 1..0 of the line-up error.</summary>
    public static double AttitudeShare(double progress) => 1 - SmoothStep(AttitudeStart, AttitudeStop, progress);

    /// <summary>The accelerating burn's jets are open somewhere in the progress span [from, to].</summary>
    public static bool AccelPulse(double from, double to) => BurnPulse(from, to, AlignEnd, AccelEnd);

    /// <summary>The braking burn's jets are open somewhere in [from, to].</summary>
    public static bool BrakePulse(double from, double to) => BurnPulse(from, to, BrakeStart, BrakeEnd);

    /// <summary>
    /// A lateral correction pulse or trim puff in [from, to] on cross axis 1 or 2: +1 pushes towards the axis, -1 away
    /// (stopping the drift), 0 none.
    /// </summary>
    public static int LateralPulse(int axis, double from, double to)
    {
        var (start, stop) = axis == 1 ? (LateralStart1, LateralStop1) : (LateralStart2, LateralStop2);
        if (Overlaps(from, to, start, start + CorrectionPulse)) return 1;
        if (Overlaps(from, to, stop - CorrectionPulse, stop)) return -1;
        foreach (var (at, trimAxis, sign) in TrimPuffs)
        {
            if (trimAxis == axis && Overlaps(from, to, at, at + CorrectionPulse * 0.6)) return sign;
        }
        return 0;
    }

    /// <summary>An attitude pulse in [from, to]: +1 starts the turn onto the axis, -1 stops it, 0 none.</summary>
    public static int AttitudePulse(double from, double to)
    {
        if (Overlaps(from, to, AttitudeStart, AttitudeStart + CorrectionPulse)) return 1;
        return Overlaps(from, to, AttitudeStop - CorrectionPulse, AttitudeStop) ? -1 : 0;
    }

    /// <summary>Closed spans overlap; a frame that did not move (frozen, or the first one) is the point <paramref name="to"/>.</summary>
    public static bool Overlaps(double from, double to, double start, double end)
    {
        var lo = Math.Min(from, to);
        var hi = Math.Max(from, to);
        return hi >= start && lo <= end;
    }

    private static bool BurnPulse(double from, double to, double burnStart, double burnEnd)
    {
        if (!Overlaps(from, to, burnStart, burnEnd)) return false;
        var slot = (burnEnd - burnStart) / PulsesPerBurn;
        for (var i = 0; i < PulsesPerBurn; i++)
        {
            var start = burnStart + i * slot;
            if (Overlaps(from, to, start, start + slot * PulseDuty)) return true;
        }
        return false;
    }

    // Distance closed by progress p, in coast-speed × progress units: the area under the trapezoid speed profile.
    private static double Flown(double p) =>
        Ramp(p, AlignEnd, AccelEnd, 0, 1) +
        Ramp(p, AccelEnd, BrakeStart, 1, 1) +
        Ramp(p, BrakeStart, BrakeEnd, 1, FinalSpeedShare) +
        Ramp(p, BrakeEnd, 1, FinalSpeedShare, FinalSpeedShare);

    private static double Ramp(double p, double from, double to, double v0, double v1)
    {
        if (p <= from) return 0;
        var x = Math.Min(p, to) - from;
        return v0 * x + (v1 - v0) * x * x / (2 * (to - from));
    }

    private static double SmoothStep(double from, double to, double x)
    {
        var t = Math.Clamp((x - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
