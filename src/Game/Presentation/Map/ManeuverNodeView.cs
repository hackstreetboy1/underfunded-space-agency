using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Game.Presentation.Map;

/// <summary>One direction handle of a maneuver node (an arrow from the node).</summary>
/// <param name="UnitScreen">Unit vector on screen (Y down).</param>
/// <param name="Tip">Arrow tip in screen pixels.</param>
/// <param name="Selected">This is the plan's current direction.</param>
public readonly record struct DirectionHandle(
    BurnDirection Direction, double LocalAngleDeg, MapPoint UnitScreen, MapPoint Tip, double ScreenAngleRad, bool Selected);

/// <summary>A maneuver node on the orbit with its prograde/retrograde/radial handles and the free-angle ring.</summary>
/// <param name="TrueAnomalyRad">Anomaly of the node on the conic.</param>
/// <param name="Handles">Prograde, retrograde, radial in, radial out, then custom (the custom handle sits on the ring).</param>
public sealed record ManeuverNodeView(
    MissionTime Time,
    double TrueAnomalyRad,
    StateVector State,
    MapPoint Position,
    IReadOnlyList<DirectionHandle> Handles,
    double RingRadiusPx,
    string TimeLabel)
{
    /// <summary>The node at the vehicle's position at <paramref name="time"/>.</summary>
    public static ManeuverNodeView At(
        in Conic conic, MissionTime time, MissionTime now, MapTransform transform, double handleLengthPx,
        BurnDirection selected, double customAngleDeg, MapText text)
    {
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentNullException.ThrowIfNull(text);
        var anomaly = ConicGeometry.AnomalyAt(conic, time);
        return Build(conic, time, anomaly, now, transform, handleLengthPx, selected, customAngleDeg, text);
    }

    /// <summary>The node at the next pass through <paramref name="trueAnomalyRad"/>; null when a hyperbola never gets there again.</summary>
    public static ManeuverNodeView? AtTrueAnomaly(
        in Conic conic, double trueAnomalyRad, MissionTime now, MapTransform transform, double handleLengthPx,
        BurnDirection selected, double customAngleDeg, MapText text)
    {
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentNullException.ThrowIfNull(text);
        return conic.NextTimeAtTrueAnomaly(trueAnomalyRad, now) is { } time
            ? Build(conic, time, trueAnomalyRad, now, transform, handleLengthPx, selected, customAngleDeg, text)
            : null;
    }

    /// <summary>
    /// Dragging along the orbit: the anomaly under <paramref name="cursor"/> (radial projection onto the conic,
    /// clamped to the drawn arc) and the next time the vehicle reaches it.
    /// </summary>
    public static (double TrueAnomalyRad, MissionTime? Time) ProjectPointer(
        in Conic conic, ConicArc arc, MapTransform transform, MapPoint cursor, MissionTime now)
    {
        ArgumentNullException.ThrowIfNull(transform);
        var anomaly = ConicGeometry.AnomalyTowards(conic, transform.ScreenToWorld(cursor));
        if (!arc.IsClosed && !arc.IsEmpty) anomaly = Angles.Wrap(arc.Normalize(anomaly));
        return (anomaly, conic.NextTimeAtTrueAnomaly(anomaly, now));
    }

    /// <summary>Custom burn angle (degrees from prograde towards radial-out) pointing from the node at <paramref name="cursor"/>.</summary>
    public double CustomAngleTowards(MapPoint cursor)
    {
        var screen = cursor - Position;
        if (screen.Length == 0) return 0;
        var worldAngle = Math.Atan2(-screen.Y, screen.X);
        var sense = State.PositionM.Cross(State.VelocityMps) >= 0 ? 1 : -1;
        return Angles.Wrap(sense * (State.VelocityMps.Angle - worldAngle)) * Angles.RadToDeg;
    }

    private static ManeuverNodeView Build(
        in Conic conic, MissionTime time, double anomaly, MissionTime now, MapTransform transform, double handleLengthPx,
        BurnDirection selected, double customAngleDeg, MapText text)
    {
        var state = conic.StateAtTrueAnomaly(anomaly);
        var position = transform.WorldToScreen(state.PositionM);
        var handles = new DirectionHandle[5];
        handles[0] = Handle(BurnDirection.Prograde, 0, state, position, handleLengthPx, selected);
        handles[1] = Handle(BurnDirection.Retrograde, 0, state, position, handleLengthPx, selected);
        handles[2] = Handle(BurnDirection.RadialIn, 0, state, position, handleLengthPx, selected);
        handles[3] = Handle(BurnDirection.RadialOut, 0, state, position, handleLengthPx, selected);
        handles[4] = Handle(BurnDirection.Custom, customAngleDeg, state, position, handleLengthPx, selected);
        var label = $"{text.Met(time)} · {text.Relative(now, time)}";
        return new ManeuverNodeView(time, anomaly, state, position, handles, handleLengthPx, label);
    }

    private static DirectionHandle Handle(
        BurnDirection direction, double customAngleDeg, in StateVector state, MapPoint origin, double lengthPx, BurnDirection selected)
    {
        var localRad = BurnFrames.LocalAngleRad(direction, customAngleDeg * Angles.DegToRad);
        var worldAngle = BurnFrames.InertialAngleRad(localRad, state);
        var unit = MapTransform.DirectionToScreen(Vec2d.FromPolar(1, worldAngle));
        return new DirectionHandle(
            direction, localRad * Angles.RadToDeg, unit, origin + unit * lengthPx, MapTransform.ScreenAngle(worldAngle), direction == selected);
    }
}
