namespace Usa.Game.Presentation.Map;

/// <summary>Tuning for the orbit map's geometry. Defaults suit a 1000 × 600 view of low Earth orbit.</summary>
/// <param name="DefaultViewRadiusFactor">The reset view shows a circle of this many body radii.</param>
/// <param name="MinViewRadiusM">Closest zoom: the shorter viewport side spans twice this.</param>
/// <param name="MaxViewRadiusFactor">Farthest zoom, in body radii.</param>
/// <param name="SampleTolerancePx">Largest gap between a drawn chord and the true conic.</param>
/// <param name="ZoneRimOffsetPx">Recovery-zone arcs are drawn this far outside the body's surface.</param>
/// <param name="ZoneArcStepRad">Angular spacing of zone arc points.</param>
/// <param name="ZonePassScanStepS">Time step when searching for the next pass over a zone.</param>
/// <param name="ZonePassHorizonOrbits">How many orbital periods ahead the zone pass search looks.</param>
/// <param name="CorridorBottomAltitudeM">The entry corridor band ends at this altitude (or the limit path's periapsis).</param>
/// <param name="HandleLengthPx">Length of a maneuver node's direction handles.</param>
/// <param name="MinApsisEccentricity">Below this the orbit is drawn as circular and gets no apsis markers.</param>
public sealed record OrbitMapSettings(
    double DefaultViewRadiusFactor = 1.4,
    double MinViewRadiusM = 20_000,
    double MaxViewRadiusFactor = 80,
    double SampleTolerancePx = 0.5,
    double ZoneRimOffsetPx = 4,
    double ZoneArcStepRad = 0.02,
    double ZonePassScanStepS = 20,
    double ZonePassHorizonOrbits = 2,
    double CorridorBottomAltitudeM = 40_000,
    double HandleLengthPx = 36,
    double MinApsisEccentricity = 1e-4)
{
    public static OrbitMapSettings Default { get; } = new();
}
