using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Session;

/// <summary>
/// Read-only orbit picture for presentation (map, ground track, relative view), so the host never rebuilds orbits from
/// <c>flight.*</c> paths. Values are copies; building one changes no state.
/// </summary>
/// <param name="EarthRotationRad">Body rotation angle at <paramref name="Now"/> (inertial longitude of the prime meridian).</param>
/// <param name="Target">The mission's target vehicle while it flies its own trajectory; null when docked or when the mission has none.</param>
/// <param name="TargetRelativeM">Target minus crewed position (inertial, metres) while both fly free; null when docked or absent.</param>
/// <param name="CrewedEstimate">The ground's estimate of the crewed vehicle (X8: state and conic for the map marker); null without tracking.</param>
/// <param name="TargetEstimate">The ground's estimate of the target while it flies free; null when docked, absent or untracked.</param>
public sealed record FlightTrackView(
    MissionTime Now,
    string BodyId,
    double BodyRadiusM,
    double EarthRotationRad,
    bool Docked,
    FlightBodyView Crewed,
    FlightBodyView? Target,
    Vec2d? TargetRelativeM,
    NavEstimate? CrewedEstimate = null,
    NavEstimate? TargetEstimate = null)
{
    /// <summary>Deprecated alias of <see cref="Target"/> for the game adapters; switch to <see cref="Target"/>.</summary>
    public FlightBodyView? Lodestar => Target;

    /// <summary>Deprecated alias of <see cref="TargetRelativeM"/>; switch to <see cref="TargetRelativeM"/>.</summary>
    public Vec2d? LodestarRelativeM => TargetRelativeM;

    internal static FlightTrackView Build(FlightSystem flight, MissionTime now, GroundTracking? tracking = null)
    {
        var crewed = FlightBodyView.Of(flight.Crewed, now);
        var target = flight.Target is { } propagator ? FlightBodyView.Of(propagator, now) : null;
        Vec2d? relative = !flight.Docked && target is not null ? target.State.PositionM - crewed.State.PositionM : null;
        var body = flight.Crewed.Body;
        return new FlightTrackView(
            now, body.Id, body.RadiusM, flight.Mission.Zones.RotationAngleRad(now), flight.Docked, crewed, target, relative,
            tracking?.Estimate(flight.CrewedId, now), flight.TargetId is { } targetId ? tracking?.Estimate(targetId, now) : null);
    }
}

/// <summary>One vehicle's flight state: the propagated state vector at the view time and its osculating conic.</summary>
/// <param name="State">At the view time; a coasting vehicle is moved along its conic when its propagator lags (a target steps once a second).</param>
public sealed record FlightBodyView(string VehicleId, FlightPhase Phase, StateVector State, Conic Conic, double AltitudeM, LandingSite? Landing)
{
    internal static FlightBodyView Of(OrbitPropagator propagator, MissionTime now)
    {
        var conic = propagator.Conic;
        var state = propagator.Phase == FlightPhase.Coast && propagator.Time != now ? conic.StateAt(now) : propagator.State;
        return new FlightBodyView(propagator.VehicleId, propagator.Phase, state, conic, state.RadiusM - propagator.Body.RadiusM, propagator.Landing);
    }
}
