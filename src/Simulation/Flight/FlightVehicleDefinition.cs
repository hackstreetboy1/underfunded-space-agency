namespace Usa.Simulation.Flight;

/// <summary>What flight does with a vehicle of the mission.</summary>
public enum FlightVehicleRole
{
    /// <summary>The vehicle the crew flies; the <c>flight.</c> paths describe it (or the stack it heads).</summary>
    Crewed,

    /// <summary>A second vehicle co-orbiting the crewed one, e.g. a docking target.</summary>
    Target,
}

/// <summary>One entry of <c>flight.json</c> <c>vehicles</c>.</summary>
/// <param name="Id">Vehicle id as in <c>data/vehicles/&lt;id&gt;.json</c>.</param>
/// <param name="LeadM">Target only: along-track distance it leads the crewed vehicle at mission start.</param>
public sealed record FlightVehicleDefinition(string Id, FlightVehicleRole Role, double LeadM = 0);
