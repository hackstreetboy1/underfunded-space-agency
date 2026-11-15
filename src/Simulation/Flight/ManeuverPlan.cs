using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Flight;

/// <summary>A burn as the player (or the proposer) sets it up on the map.</summary>
/// <param name="DurationS">Ignored for engines with a fixed duration.</param>
/// <param name="CustomAngleDeg">Used with <see cref="BurnDirection.Custom"/>: degrees from prograde towards radial-out.</param>
/// <param name="AttitudeErrorDeg">Assumed constant pointing error, e.g. when attitude hold is unavailable.</param>
public sealed record ManeuverPlan(
    BurnEngine Engine,
    BurnDirection Direction,
    double DurationS,
    MissionTime StartTime,
    double CustomAngleDeg = 0,
    double AttitudeErrorDeg = 0)
{
    public double EffectiveDurationS => Engine.FixedDurationS ?? Math.Max(DurationS, 0);

    public MissionTime EndTime => StartTime + (long)Math.Round(EffectiveDurationS * 1000.0);

    public double LocalAngleRad => BurnFrames.LocalAngleRad(Direction, CustomAngleDeg * Angles.DegToRad);

    /// <exception cref="ArgumentOutOfRangeException">A number in the plan is NaN or infinite.</exception>
    internal void RequireFinite()
    {
        if (!double.IsFinite(DurationS)) throw new ArgumentOutOfRangeException(nameof(DurationS), DurationS, "Burn duration must be finite.");
        if (!double.IsFinite(CustomAngleDeg)) throw new ArgumentOutOfRangeException(nameof(CustomAngleDeg), CustomAngleDeg, "Burn angle must be finite.");
        if (!double.IsFinite(AttitudeErrorDeg)) throw new ArgumentOutOfRangeException(nameof(AttitudeErrorDeg), AttitudeErrorDeg, "Attitude error must be finite.");
    }
}
