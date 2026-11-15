using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Game.Presentation.Map;

/// <summary>
/// The entry corridor drawn where the path descends through the entry interface: the region between the steepest
/// and shallowest acceptable trajectories from that point (green fill, amber edges), plus the path's own
/// flight-path angle and quality.
/// </summary>
/// <param name="Polygon">Steep edge followed by the reversed shallow edge, for <c>draw_colored_polygon</c>.</param>
/// <param name="InterfaceAngleRad">World angle of the interface crossing.</param>
/// <param name="Label">e.g. <c>Corridor −2.5° to −1.0°</c>.</param>
/// <param name="EntryText">The path's own angle, e.g. <c>entry −1.3°</c>.</param>
public sealed record EntryCorridorBand(
    IReadOnlyList<MapPoint> Polygon,
    IReadOnlyList<MapPoint> SteepEdge,
    IReadOnlyList<MapPoint> ShallowEdge,
    MapPoint InterfacePoint,
    Vec2d InterfacePointM,
    double InterfaceAngleRad,
    double FlightPathAngleDeg,
    CorridorQuality Quality,
    string Label,
    string EntryText)
{
    /// <summary>
    /// The band for <paramref name="conic"/>, or null when its drawn arc never descends through the interface
    /// (periapsis at or above <see cref="EntryCorridor.InterfaceAltitudeM"/>).
    /// </summary>
    public static EntryCorridorBand? For(
        in Conic conic, ConicArc arc, double bodyRadiusM, EntryCorridor corridor, MapTransform transform,
        ConicPathSampler sampler, double toleranceM, double bottomAltitudeM, MapText text)
    {
        ArgumentNullException.ThrowIfNull(corridor);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentNullException.ThrowIfNull(sampler);
        if (arc.IsEmpty) return null;
        var interfaceRadius = bodyRadiusM + corridor.InterfaceAltitudeM;
        if (conic.PeriapsisM >= interfaceRadius) return null;
        var crossing = conic.TrueAnomalyAtRadius(interfaceRadius);
        if (double.IsNaN(crossing) || !arc.Contains(-crossing)) return null;

        var state = conic.StateAtTrueAnomaly(-crossing);
        var flightPathDeg = conic.FlightPathAngleAtTrueAnomaly(-crossing) * Angles.RadToDeg;
        var steep = LimitEdge(conic, state, corridor.SteepestFlightPathAngleDeg, bodyRadiusM + bottomAltitudeM, sampler, toleranceM, transform);
        var shallow = LimitEdge(conic, state, corridor.ShallowestFlightPathAngleDeg, bodyRadiusM + bottomAltitudeM, sampler, toleranceM, transform);
        var polygon = new List<MapPoint>(steep.Count + shallow.Count);
        polygon.AddRange(steep);
        for (var i = shallow.Count - 1; i >= 0; i--) polygon.Add(shallow[i]);

        var label = text.CorridorBand(corridor.SteepestFlightPathAngleDeg, corridor.ShallowestFlightPathAngleDeg);
        return new EntryCorridorBand(
            polygon, steep, shallow, transform.WorldToScreen(state.PositionM), state.PositionM, state.PositionM.Angle,
            flightPathDeg, corridor.Classify(flightPathDeg), label, text.CorridorEntry(flightPathDeg));
    }

    /// <summary>The trajectory leaving the interface point at <paramref name="flightPathDeg"/>, down to the bottom radius or its periapsis.</summary>
    private static List<MapPoint> LimitEdge(
        in Conic path, in StateVector atInterface, double flightPathDeg, double bottomRadiusM,
        ConicPathSampler sampler, double toleranceM, MapTransform transform)
    {
        var radial = atInterface.PositionM.Normalized();
        var horizontal = radial.Perpendicular * path.Direction;
        var gamma = flightPathDeg * Angles.DegToRad;
        var velocity = atInterface.SpeedMps * (Math.Cos(gamma) * horizontal + Math.Sin(gamma) * radial);
        var limit = Conic.FromState(new StateVector(atInterface.PositionM, velocity), path.MuM3PerS2, MissionTime.Zero);

        var start = ConicGeometry.AnomalyTowards(limit, atInterface.PositionM);
        var bottom = limit.TrueAnomalyAtRadius(bottomRadiusM);
        var end = double.IsNaN(bottom) ? 0 : Math.Min(0, -bottom);
        if (end <= start) return [transform.WorldToScreen(atInterface.PositionM)];
        var samples = sampler.Sample(limit, new ConicArc(start, end, ArcEnd.Impact), toleranceM).PointsM;
        var points = new List<MapPoint>(samples.Count);
        for (var i = 0; i < samples.Count; i++) points.Add(transform.WorldToScreen(samples[i]));
        return points;
    }
}
