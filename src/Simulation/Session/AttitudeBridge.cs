using Usa.Simulation.Attitude;
using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;
using Usa.Simulation.Vehicle;

namespace Usa.Simulation.Session;

/// <summary>
/// Couples the attitude model to vehicle truth every 50 ms tick. Before the step it copies thruster availability, shorted-driver
/// forced fractions and tank contents from the vehicle; after it, the vehicle fires every thruster at the fraction the attitude
/// model used (<see cref="Vehicle.Propulsion.PropulsionModel.FireThrusters"/>), which draws the propellant and records valve
/// activity. The vehicle is the single owner of propellant; the attitude model's own propellant tally is ignored.
/// </summary>
internal sealed class AttitudeBridge : IAttitudeBridge, IHashContributor
{
    private readonly VehicleSet _vehicles;
    private readonly Dictionary<string, Link> _links = new(StringComparer.Ordinal);

    public AttitudeBridge(VehicleSet vehicles)
    {
        _vehicles = vehicles;
    }

    /// <summary>Propellant drawn because a shorted driver held a valve open, kg since load (decision quality input).</summary>
    public double ForcedPropellantKg { get; private set; }

    /// <summary>Builds the index maps once the attitude system exists (its thruster order is its own).</summary>
    public void Attach(AttitudeSystem system)
    {
        foreach (var vehicle in system.Vehicles)
        {
            var index = _vehicles.IndexOf(vehicle.Id);
            var state = _vehicles[index];
            var layout = state.Layout;
            var propulsionThrusters = layout.Thrusters;
            var set = vehicle.Thrusters;

            var toPropulsion = new int[set.Count];
            var toComponent = new int[set.Count];
            for (var i = 0; i < set.Count; i++)
            {
                var component = layout.IndexOf(set.Specs[i].Id);
                toComponent[i] = component;
                toPropulsion[i] = propulsionThrusters.IndexOf(component);
            }

            var tanks = new int[set.TankIds.Count];
            for (var t = 0; t < tanks.Length; t++) tanks[t] = layout.IndexOf(set.TankIds[t]);
            _links.Add(vehicle.Id, new Link(index, toPropulsion, toComponent, tanks, new double[propulsionThrusters.Length]));
        }
    }

    public void BeforeStep(AttitudeVehicle vehicle, in StepContext context)
    {
        var link = _links[vehicle.Id];
        var state = _vehicles[link.Vehicle];
        var propulsion = _vehicles.Propulsion(link.Vehicle);
        var set = vehicle.Thrusters;
        for (var i = 0; i < link.ToComponent.Length; i++)
        {
            var component = link.ToComponent[i];
            set.SetCommandable(i, propulsion.IsAvailable(component));
            set.SetForcedFraction(i, propulsion.ForcedFraction(component));
        }
        for (var t = 0; t < link.Tanks.Length; t++)
        {
            var tank = link.Tanks[t];
            set.SetTankHasPropellant(t, state.IsAttached(tank) && state[tank].Condition != ComponentCondition.Isolated && state[tank].QuantityKg > 0);
        }
    }

    public void AfterStep(AttitudeVehicle vehicle, in StepContext context)
    {
        var link = _links[vehicle.Id];
        var set = vehicle.Thrusters;
        var fractions = set.EffectiveFraction;
        var commands = link.Commands;
        Array.Clear(commands);
        for (var i = 0; i < fractions.Length; i++)
        {
            commands[link.ToPropulsion[i]] = fractions[i];
            var forced = set.GetForcedFraction(i);
            if (forced > 0 && fractions[i] > 0)
            {
                var spec = set.Specs[i];
                ForcedPropellantKg += spec.ThrustN / (spec.IspS * PhysicalConstants.StandardGravity) * Math.Min(forced, fractions[i]) * context.DeltaSeconds;
            }
        }
        // Coasting with every valve shut is ~all ticks; firing then would only re-derive what is already true.
        var propulsion = _vehicles.Propulsion(link.Vehicle);
        if (!propulsion.IsIdle(commands)) propulsion.FireThrusters(commands, context.DeltaSeconds, context.Now, out _, out _);
    }

    public void WriteHash(StateHashWriter writer) => writer.Write(ForcedPropellantKg);

    private sealed record Link(int Vehicle, int[] ToPropulsion, int[] ToComponent, int[] Tanks, double[] Commands);
}
