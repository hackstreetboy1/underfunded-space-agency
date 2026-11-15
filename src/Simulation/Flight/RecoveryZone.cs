using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Flight;

/// <summary>
/// A longitude arc on the rotating Earth's rim, from <see cref="WestLongitudeDeg"/> eastward (counter-clockwise)
/// to <see cref="EastLongitudeDeg"/>. Arcs may cross ±180°.
/// </summary>
public sealed record RecoveryZone(string Id, double WestLongitudeDeg, double EastLongitudeDeg, double WaitHours)
{
    public double WidthRad => Angles.WrapPositive((EastLongitudeDeg - WestLongitudeDeg) * Angles.DegToRad);

    public double CentreLongitudeRad => Angles.Wrap(WestLongitudeDeg * Angles.DegToRad + WidthRad / 2);

    public bool Contains(double longitudeRad) =>
        Angles.WrapPositive(longitudeRad - WestLongitudeDeg * Angles.DegToRad) <= WidthRad;

    /// <summary>The whole arc of half-width <paramref name="halfWidthRad"/> around <paramref name="centreLongitudeRad"/> lies in the zone.</summary>
    public bool ContainsArc(double centreLongitudeRad, double halfWidthRad) =>
        2 * halfWidthRad <= WidthRad
        && Angles.WrapPositive(centreLongitudeRad - halfWidthRad - WestLongitudeDeg * Angles.DegToRad) + 2 * halfWidthRad <= WidthRad;
}

/// <summary>Where a landing (or predicted landing) falls.</summary>
/// <param name="ZoneId">Null when outside every recovery zone.</param>
public readonly record struct ZoneEvaluation(double LandingLongitudeDeg, string? ZoneId, double RecoveryWaitHours)
{
    public bool InZone => ZoneId is not null;
}
