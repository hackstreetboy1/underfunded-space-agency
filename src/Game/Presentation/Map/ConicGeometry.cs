using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Game.Presentation.Map;

/// <summary>Position-only conic helpers (cheaper than <see cref="Conic.StateAtTrueAnomaly"/> for drawing).</summary>
public static class ConicGeometry
{
    public static Vec2d PositionAt(in Conic conic, double trueAnomalyRad)
    {
        var radius = conic.SemiLatusRectumM / (1 + conic.Eccentricity * Math.Cos(trueAnomalyRad));
        return Vec2d.FromPolar(radius, conic.ArgPeriapsisRad + conic.Direction * trueAnomalyRad);
    }

    /// <summary>The true anomaly of the conic point at the same polar angle as <paramref name="pointM"/> (radial projection).</summary>
    public static double AnomalyTowards(in Conic conic, Vec2d pointM) =>
        Angles.Wrap(conic.Direction * (pointM.Angle - conic.ArgPeriapsisRad));

    /// <summary>The conic's true anomaly at <paramref name="time"/>, in (-π, π].</summary>
    public static double AnomalyAt(in Conic conic, MissionTime time) =>
        conic.IsHyperbolic ? conic.TrueAnomalyAt(time) : Angles.Wrap(conic.TrueAnomalyAt(time));
}
