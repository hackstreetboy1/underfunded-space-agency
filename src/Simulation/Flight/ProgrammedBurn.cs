using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Flight;

/// <summary>
/// Executes a <see cref="ManeuverPlan"/>: thrust from the engine option, direction re-aimed at the local preset
/// every step (attitude hold), plus a pointing error. Stops when the engine's propellant is spent.
/// </summary>
public sealed class ProgrammedBurn : IThrustLaw
{
    private readonly double _localAngleRad;

    public ProgrammedBurn(ManeuverPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Plan = plan;
        _localAngleRad = plan.LocalAngleRad;
        PointingErrorRad = plan.AttitudeErrorDeg * Angles.DegToRad;
    }

    /// <summary>A burn picked up part-way, after <paramref name="propellantUsedKg"/> has already been drawn (<see cref="BurnLadder"/>).</summary>
    internal ProgrammedBurn(ManeuverPlan plan, double propellantUsedKg) : this(plan)
    {
        PropellantUsedKg = propellantUsedKg;
    }

    public ManeuverPlan Plan { get; }

    public MissionTime StartTime => Plan.StartTime;

    public MissionTime EndTime => Plan.EndTime;

    /// <summary>Pointing error applied from the next step on; the host updates it from the attitude model.</summary>
    public double PointingErrorRad
    {
        get;
        set => field = double.IsFinite(value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Pointing error must be finite.");
    }

    public double PropellantUsedKg { get; private set; }

    public bool PropellantExhausted => PropellantUsedKg >= Plan.Engine.AvailablePropellantKg;

    public ThrustSample Sample(MissionTime time, double stepSeconds, in StateVector state, double massKg)
    {
        if (time < StartTime || time >= EndTime) return ThrustSample.None;
        var level = Plan.Engine.Sample((time.Milliseconds - StartTime.Milliseconds) / 1000.0);
        if (level.ThrustN <= 0 || level.MassFlowKgPerS <= 0) return ThrustSample.None;

        var remaining = Plan.Engine.AvailablePropellantKg - PropellantUsedKg;
        if (remaining <= 0) return ThrustSample.None;
        var fraction = Math.Min(1, remaining / (level.MassFlowKgPerS * stepSeconds));
        var massFlow = level.MassFlowKgPerS * fraction;
        PropellantUsedKg += massFlow * stepSeconds;
        var direction = BurnFrames.InertialAngleRad(_localAngleRad, state) + PointingErrorRad;
        return new ThrustSample(level.ThrustN * fraction, massFlow, direction, massFlow);
    }
}
