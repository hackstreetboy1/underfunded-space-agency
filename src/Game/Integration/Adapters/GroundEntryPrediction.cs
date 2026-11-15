using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Game.Integration.Adapters;

/// <summary>
/// The ground's prediction of the entry state (X8 stage 2), derived from its orbit estimate (<see cref="NavEstimate"/>: the last
/// radar fix propagated with the commanded burns), never from the simulated <c>flight.*</c> readouts. Always shown as EST with a
/// band. SI units; heating is the raw √ρ·v³ proxy; every value is NaN when the ground has no estimate.
/// </summary>
/// <param name="AltitudeSigmaM">1σ of the altitude: the estimate's radial σ.</param>
public readonly record struct GroundEntryPrediction(
    MissionTime Time, double AltitudeM, double AltitudeSigmaM, double SpeedMps, double FlightPathAngleDeg, double HeatingProxy)
{
    public static GroundEntryPrediction None { get; } = new(default, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);

    public bool Known => double.IsFinite(AltitudeM);

    /// <summary>
    /// The estimate's altitude, speed (relative to the air once in the atmosphere, as the crew and recovery planners quote it),
    /// flight-path angle and heating rate over <paramref name="body"/>.
    /// </summary>
    public static GroundEntryPrediction Of(NavEstimate? estimate, CelestialBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (estimate is null) return None;
        var state = estimate.State;
        var atmospheric = estimate.Phase is FlightPhase.Atmospheric or FlightPhase.Landed;
        return new GroundEntryPrediction(
            estimate.Time,
            state.PositionM.Length - body.RadiusM,
            estimate.SigmaRadialM,
            atmospheric ? body.AirVelocityOf(state).Length : state.SpeedMps,
            state.FlightPathAngleRad * Angles.RadToDeg,
            HeatingProxyOf(body, state));
    }

    /// <summary>The flight model's Sutton-Graves proxy √ρ·|v_air|³ (nose radius and constant omitted).</summary>
    private static double HeatingProxyOf(CelestialBody body, in StateVector state)
    {
        if (body.Atmosphere is not { } atmosphere) return 0;
        var density = atmosphere.DensityAt(state.PositionM.Length - body.RadiusM);
        if (!(density > 0)) return 0;
        var airspeed = body.AirVelocityOf(state).Length;
        return Math.Sqrt(density) * airspeed * airspeed * airspeed;
    }
}
