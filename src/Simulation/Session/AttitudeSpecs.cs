using Usa.Simulation.Attitude;
using Usa.Simulation.Vehicle;
using AttitudeMass = Usa.Simulation.Attitude.MassProperties;
using AttitudeThruster = Usa.Simulation.Attitude.ThrusterSpec;

namespace Usa.Simulation.Session;

/// <summary>Maps vehicle data onto what the attitude model needs.</summary>
internal static class AttitudeSpecs
{
    /// <summary>
    /// Every thruster of the vehicle (engines excluded: they are flown by flight's programmed burns), positioned relative
    /// to the centre of mass at load. Later centre-of-mass shifts (propellant use, staging) only update the inertia.
    /// </summary>
    public static AttitudeVehicleSpec Create(VehicleState vehicle)
    {
        var layout = vehicle.Layout;
        var mass = vehicle.ComputeMassProperties();
        var thrusters = new List<AttitudeThruster>(layout.Thrusters.Length);
        foreach (var index in layout.Thrusters)
        {
            var spec = layout.ThrusterSpecOf(index)!;
            var group = layout.GroupOf(index);
            thrusters.Add(new AttitudeThruster(
                layout.IdOf(index).Value,
                group >= 0 ? layout.IdOf(group).Value : "",
                layout.IdOf(layout.TankOf(index)).Value,
                layout.PositionM(index) - mass.CenterOfMassM,
                layout.DirectionUnit(index),
                spec.ThrustN,
                spec.IspS));
        }
        return new AttitudeVehicleSpec(vehicle.VehicleId, MassOf(vehicle), thrusters);
    }

    public static AttitudeMass MassOf(VehicleState vehicle)
    {
        var mass = vehicle.ComputeMassProperties();
        return new AttitudeMass(mass.MassKg, mass.InertiaKgM2, mass.CenterOfMassM.X);
    }
}
