using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Flight;

/// <summary>
/// Recovery zones on a rotating body. The prime meridian's inertial angle is θ(t) = θ0 + ω t (t = MET), so a
/// landing at inertial angle φ has longitude λ = φ - θ(t).
/// </summary>
public sealed class RecoveryZones
{
    private readonly RecoveryZone[] _zones;

    public RecoveryZones(
        string bodyId, double rotationAngleAtMetZeroRad, double rotationRateRadPerS,
        IEnumerable<RecoveryZone> zones, double outsideZonesWaitHours)
    {
        BodyId = bodyId;
        RotationAngleAtMetZeroRad = rotationAngleAtMetZeroRad;
        RotationRateRadPerS = rotationRateRadPerS;
        OutsideZonesWaitHours = outsideZonesWaitHours;
        _zones = zones.OrderBy(z => z.Id, StringComparer.Ordinal).ToArray();
    }

    public string BodyId { get; }

    public double RotationAngleAtMetZeroRad { get; }

    public double RotationRateRadPerS { get; }

    public double OutsideZonesWaitHours { get; }

    /// <summary>Zones sorted by id.</summary>
    public IReadOnlyList<RecoveryZone> Zones => _zones;

    public double RotationAngleRad(MissionTime time) => RotationAngleAtMetZeroRad + RotationRateRadPerS * time.Seconds;

    public double LandingLongitudeRad(double impactAngleRad, MissionTime time) => Angles.Wrap(impactAngleRad - RotationAngleRad(time));

    /// <summary>The zone containing <paramref name="longitudeRad"/>; overlapping zones resolve to the shortest wait.</summary>
    public ZoneEvaluation Evaluate(double longitudeRad)
    {
        RecoveryZone? best = null;
        for (var i = 0; i < _zones.Length; i++)
        {
            var zone = _zones[i];
            if (zone.Contains(longitudeRad) && (best is null || zone.WaitHours < best.WaitHours)) best = zone;
        }
        var longitudeDeg = Angles.Wrap(longitudeRad) * Angles.RadToDeg;
        return best is null
            ? new ZoneEvaluation(longitudeDeg, null, OutsideZonesWaitHours)
            : new ZoneEvaluation(longitudeDeg, best.Id, best.WaitHours);
    }

    public ZoneEvaluation EvaluateImpact(double impactAngleRad, MissionTime time) => Evaluate(LandingLongitudeRad(impactAngleRad, time));

    /// <summary>
    /// The longest recovery wait anywhere on the arc of half-width <paramref name="halfWidthRad"/> around
    /// <paramref name="centreLongitudeRad"/> (<see cref="Evaluate"/> at every point; open sea counts).
    /// </summary>
    public double WorstWaitHours(double centreLongitudeRad, double halfWidthRad)
    {
        var worst = Evaluate(centreLongitudeRad).RecoveryWaitHours;
        if (!(halfWidthRad > 0)) return worst;
        if (halfWidthRad >= Math.PI) halfWidthRad = Math.PI;
        var west = centreLongitudeRad - halfWidthRad;
        // The wait only changes at zone edges: test both ends and either side of every edge on the arc.
        worst = Math.Max(worst, Evaluate(west).RecoveryWaitHours);
        worst = Math.Max(worst, Evaluate(centreLongitudeRad + halfWidthRad).RecoveryWaitHours);
        const double Nudge = 1e-9;
        for (var i = 0; i < _zones.Length; i++)
        {
            foreach (var edgeDeg in (ReadOnlySpan<double>)[_zones[i].WestLongitudeDeg, _zones[i].EastLongitudeDeg])
            {
                var offset = Angles.WrapPositive(edgeDeg * Angles.DegToRad - west);
                if (offset > 2 * halfWidthRad) continue;
                worst = Math.Max(worst, Evaluate(west + Math.Max(0, offset - Nudge)).RecoveryWaitHours);
                worst = Math.Max(worst, Evaluate(west + Math.Min(2 * halfWidthRad, offset + Nudge)).RecoveryWaitHours);
            }
        }
        return worst;
    }

    public RecoveryZone? Find(string zoneId)
    {
        for (var i = 0; i < _zones.Length; i++)
        {
            if (_zones[i].Id == zoneId) return _zones[i];
        }
        return null;
    }
}
