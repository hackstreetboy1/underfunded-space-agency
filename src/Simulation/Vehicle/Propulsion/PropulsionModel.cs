using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Vehicle.Propulsion;

/// <summary>
/// Thruster, engine and retro-rocket firing for the attitude and flight systems. Firing draws propellant and records
/// valve activity; it does not integrate motion.
/// </summary>
/// <remarks>
/// A driver with fault <c>short_on</c> holds its thruster valve open at <see cref="ComponentState.StuckFraction"/>
/// whenever the driver is energized, whatever the command; opening the driver's breaker (or OAMS master, or losing
/// the bus) stops it. Callers must therefore call <see cref="Fire"/> (or <see cref="FireThrusters"/>) for every thruster
/// on every attitude tick, including those commanded to zero.
/// </remarks>
public sealed class PropulsionModel
{
    private readonly VehicleState _state;
    private readonly VehicleLayout _layout;

    public PropulsionModel(VehicleState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        _layout = state.Layout;
    }

    /// <summary>Attitude/translation thrusters in id order; <see cref="FireThrusters"/> commands use this order.</summary>
    public ReadOnlySpan<int> Thrusters => _layout.Thrusters;

    public ReadOnlySpan<int> Engines => _layout.Engines;

    public ReadOnlySpan<int> RetroRockets => _layout.RetroRockets;

    /// <summary>
    /// True when a command would open the valve: attached and operable, driver (and own load, if any) powered,
    /// group armed and operable, propellant in an attached tank.
    /// </summary>
    public bool IsAvailable(int thruster)
    {
        if (!_state.IsOperable(thruster)) return false;
        if (_layout[thruster].Load is not null && !_state[thruster].Powered) return false;

        var driver = _layout.DriverOf(thruster);
        if (driver >= 0 && !_state[driver].Powered) return false;

        var group = _layout.GroupOf(thruster);
        if (group >= 0 && (!_state.IsOperable(group) || !_state[group].Armed)) return false;

        var tank = _layout.TankOf(thruster);
        return _state.IsAttached(tank) && _state[tank].Condition != ComponentCondition.Isolated && _state[tank].QuantityKg > 0;
    }

    /// <summary>
    /// Valve fraction forced open by a shorted, energized driver; zero otherwise. Isolating the thruster or its driver,
    /// de-energizing the driver, or disarming (or losing) the thruster's group stops it.
    /// </summary>
    public double ForcedFraction(int thruster)
    {
        if (!_state.IsAttached(thruster) || _state[thruster].Condition == ComponentCondition.Isolated) return 0;
        var driver = _layout.DriverOf(thruster);
        if (driver < 0) return 0;
        ref readonly var driverState = ref _state[driver];
        // Energized is refreshed once per electrical step; checking Isolated here makes isolation take effect at once.
        if (!driverState.Energized || driverState.Condition == ComponentCondition.Isolated
            || !string.Equals(driverState.Fault, ComponentFields.ShortOnFault, StringComparison.Ordinal)) return 0;

        // A disarmed group keeps its propellant valves shut, so a shorted driver has nothing to open.
        var group = _layout.GroupOf(thruster);
        if (group >= 0 && (!_state.IsOperable(group) || !_state[group].Armed)) return 0;
        return _state[thruster].StuckFraction;
    }

    /// <summary>Fires one thruster or engine for <paramref name="deltaSeconds"/>.</summary>
    /// <param name="commandedFraction">Requested valve fraction in [0, 1]; ignored when unavailable.</param>
    /// <returns>Thrust delivered in newtons along <see cref="VehicleLayout.DirectionUnit"/>, reduced when propellant runs out.</returns>
    public double Fire(int thruster, double commandedFraction, double deltaSeconds, MissionTime now)
    {
        var spec = _layout.ThrusterSpecOf(thruster)
                   ?? throw new InvalidOperationException($"'{_layout.IdOf(thruster)}' is not a thruster or engine.");
        ref var state = ref _state.Mutable(thruster);

        var commanded = IsAvailable(thruster) ? Math.Clamp(commandedFraction, 0, 1) : 0;
        var fraction = Math.Max(commanded, ForcedFraction(thruster));
        if (fraction <= 0 || deltaSeconds <= 0)
        {
            state.ValveOpen = 0;
            return 0;
        }

        var wantedKg = spec.ThrustN / (spec.IspS * PhysicalConstants.StandardGravity) * fraction * deltaSeconds;
        var drawnKg = _state.DrawFromTank(_layout.TankOf(thruster), wantedKg, now);
        var delivered = fraction * (drawnKg / wantedKg);
        state.ValveOpen = delivered;
        state.FiringSeconds += delivered * deltaSeconds;
        return spec.ThrustN * delivered;
    }

    /// <summary>Fires every thruster with the command at the same position in <paramref name="commandedFractions"/>.</summary>
    /// <param name="commandedFractions">One entry per <see cref="Thrusters"/> element.</param>
    /// <param name="forceN">Total force in the body frame.</param>
    /// <param name="torqueNm">Total torque about the current centre of mass.</param>
    public void FireThrusters(ReadOnlySpan<double> commandedFractions, double deltaSeconds, MissionTime now, out Vec3d forceN, out Vec3d torqueNm)
    {
        var thrusters = _layout.Thrusters;
        if (commandedFractions.Length != thrusters.Length)
            throw new ArgumentException($"Expected {thrusters.Length} commands, got {commandedFractions.Length}.", nameof(commandedFractions));

        var center = _state.ComputeMassProperties().CenterOfMassM;
        forceN = Vec3d.Zero;
        torqueNm = Vec3d.Zero;
        for (var i = 0; i < thrusters.Length; i++)
        {
            var thruster = thrusters[i];
            var thrustN = Fire(thruster, commandedFractions[i], deltaSeconds, now);
            if (thrustN <= 0) continue;
            var force = _layout.DirectionUnit(thruster) * thrustN;
            forceN += force;
            torqueNm += (_layout.PositionM(thruster) - center).Cross(force);
        }
    }

    /// <summary>
    /// True when <see cref="FireThrusters"/> with these commands would change nothing: no command above zero, every valve
    /// already shut and no shorted driver forcing one open (the coast case). Callers may then skip the call.
    /// </summary>
    public bool IsIdle(ReadOnlySpan<double> commandedFractions)
    {
        var thrusters = _layout.Thrusters;
        if (commandedFractions.Length != thrusters.Length)
            throw new ArgumentException($"Expected {thrusters.Length} commands, got {commandedFractions.Length}.", nameof(commandedFractions));
        for (var i = 0; i < thrusters.Length; i++)
        {
            if (!(commandedFractions[i] <= 0) || _state[thrusters[i]].ValveOpen != 0) return false;
        }
        for (var i = 0; i < thrusters.Length; i++)
        {
            if (ForcedFraction(thrusters[i]) > 0) return false;
        }
        return true;
    }

    /// <summary>Draws propellant (or any tank contents) directly.</summary>
    /// <returns>Mass actually drawn.</returns>
    public double DrawPropellant(int tank, double kg, MissionTime now) => _state.DrawFromTank(tank, kg, now);

    public double DrawPropellant(ComponentId tank, double kg, MissionTime now) => _state.DrawFromTank(_layout.IndexOf(tank), kg, now);

    public bool IgniteRetro(int rocket, MissionTime now) => _state.IgniteRetro(rocket, now);

    /// <summary>Advances a burning retro rocket by <paramref name="deltaSeconds"/>.</summary>
    /// <returns>Mean thrust in newtons over the interval; zero before ignition, after burnout or once detached.</returns>
    public double BurnRetro(int rocket, double deltaSeconds)
    {
        ref var state = ref _state.Mutable(rocket);
        if (!state.Fired || state.BurnRemainingS <= 0 || deltaSeconds <= 0 || !_state.IsAttached(rocket)) return 0;
        var burnS = Math.Min(deltaSeconds, state.BurnRemainingS);
        state.BurnRemainingS -= burnS;
        return _layout[rocket].RetroRocket!.ThrustN * burnS / deltaSeconds;
    }
}
