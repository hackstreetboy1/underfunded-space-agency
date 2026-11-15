using Usa.Simulation.Flight;
using Usa.Simulation.Vehicle.Definitions;

namespace Usa.Simulation.Session;

/// <summary>The vehicle definitions' retro rockets as flight needs them (the vehicle data is their single source).</summary>
public static class VehicleRetroRockets
{
    /// <summary>Every retro rocket of <paramref name="vehicles"/>, in vehicle then component order.</summary>
    public static IReadOnlyList<RetroRocket> From(IEnumerable<VehicleDefinition> vehicles)
    {
        ArgumentNullException.ThrowIfNull(vehicles);
        var rockets = new List<RetroRocket>();
        foreach (var vehicle in vehicles)
        {
            foreach (var component in vehicle.Components)
            {
                if (component.RetroRocket is not { } spec) continue;
                rockets.Add(RetroRocket.FromVehicleSpec(component.Id, spec.IgnitionOffsetS, spec.ThrustN, spec.BurnS, spec.PropellantKg));
            }
        }
        return rockets;
    }
}
