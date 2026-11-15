namespace Usa.Simulation.Crew;

/// <summary>
/// A place a crew member can be (<c>mission.json</c> <c>locations</c>): inside a vehicle's cabin, or suited outside.
/// </summary>
/// <param name="Id">snake_case id served at <c>crew.&lt;id&gt;.location</c>, e.g. <c>ferry</c>, <c>workshop</c>, <c>eva</c>.</param>
/// <param name="Cabin">Vehicle whose cabin air the member breathes; null for a suited location, where cabin exposures read suit values.</param>
/// <param name="Vehicle">Vehicle whose motion the member feels; defaults to <paramref name="Cabin"/>.</param>
/// <param name="Default">Where members start and return to after a task. Exactly one location is the default.</param>
/// <param name="AngularRateFactor">Multiplies the felt rotation rate (tethered outside a tumbling craft it feels far worse).</param>
public sealed record CrewLocationDefinition(
    string Id,
    string? Cabin = null,
    string? Vehicle = null,
    bool Default = false,
    double AngularRateFactor = 1.0)
{
    public bool IsSuited => Cabin is null;

    /// <summary>The vehicle whose rotation the member feels, or null.</summary>
    public string? MotionVehicle => Vehicle ?? Cabin;
}
