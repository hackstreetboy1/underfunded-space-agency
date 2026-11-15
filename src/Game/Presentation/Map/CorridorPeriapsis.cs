using Usa.Simulation.Flight;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Game.Presentation.Map;

/// <summary>
/// The periapsis that brings the ground's estimated orbit down through the entry interface at the corridor's centre
/// angle: the target of the Easy "Lower Pe into corridor" goal (UI 4.1). A retrograde burn keeps the burn radius as the
/// new apoapsis, so the target is solved on conics sharing the estimate's apoapsis; the proposer then finds the burn.
/// </summary>
public static class CorridorPeriapsis
{
    private const int Iterations = 60;

    /// <returns>Periapsis altitude in km (negative below the surface); null for an escape path or an apoapsis inside the interface.</returns>
    public static double? AltitudeKm(in Conic estimate, double bodyRadiusM, EntryCorridor corridor)
    {
        ArgumentNullException.ThrowIfNull(corridor);
        if (estimate.IsHyperbolic || !(bodyRadiusM > 0)) return null;
        var apoapsisM = estimate.ApoapsisM;
        var interfaceM = bodyRadiusM + corridor.InterfaceAltitudeM;
        if (!double.IsFinite(apoapsisM) || !(apoapsisM > interfaceM)) return null;

        var target = corridor.CentreFlightPathAngleDeg * Angles.DegToRad;
        var low = 1e-3 * bodyRadiusM;
        var high = interfaceM;
        if (AngleAtInterface(apoapsisM, low, interfaceM) > target) return null;
        // The descent angle at the interface steepens monotonically as the periapsis drops.
        for (var i = 0; i < Iterations; i++)
        {
            var mid = (low + high) / 2;
            if (AngleAtInterface(apoapsisM, mid, interfaceM) < target) low = mid;
            else high = mid;
        }
        return ((low + high) / 2 - bodyRadiusM) / 1000;
    }

    /// <summary>Flight-path angle (radians, negative descending) at <paramref name="radiusM"/>; μ cancels out.</summary>
    private static double AngleAtInterface(double apoapsisM, double periapsisM, double radiusM)
    {
        var semiMajorM = (apoapsisM + periapsisM) / 2;
        var semiLatusM = 2 * apoapsisM * periapsisM / (apoapsisM + periapsisM);
        var speedTerm = (2 / radiusM) - (1 / semiMajorM);
        var cosine = Math.Sqrt(semiLatusM) / (radiusM * Math.Sqrt(speedTerm));
        return -Math.Acos(Math.Clamp(cosine, -1, 1));
    }
}
