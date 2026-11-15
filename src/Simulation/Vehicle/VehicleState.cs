using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Vehicle;

/// <summary>
/// All mutable state of one vehicle: a dense, id-ordered <see cref="ComponentState"/> array plus section attachment.
/// Changes that other systems must notice go through the Set/Draw methods, which publish
/// <see cref="ComponentStateChanged"/> and bump <see cref="TopologyVersion"/> when electrical connectivity changes.
/// </summary>
public sealed class VehicleState : IHashContributor
{
    private const double JoulesPerKwh = 3.6e6;

    private readonly ComponentState[] _components;
    private readonly bool[] _sectionAttached;

    public VehicleState(VehicleLayout layout, EventBus events)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(events);
        Layout = layout;
        Events = events;
        _sectionAttached = new bool[layout.Sections.Count];
        Array.Fill(_sectionAttached, true);
        _components = new ComponentState[layout.Count];
        for (var i = 0; i < layout.Count; i++) _components[i] = Initial(layout, i);
    }

    public VehicleLayout Layout { get; }

    public EventBus Events { get; }

    public string VehicleId => Layout.VehicleId;

    /// <summary>Incremented whenever electrical connectivity may have changed.</summary>
    public long TopologyVersion { get; private set; }

    /// <summary>Fuel-cell output plus battery discharge during the last electrical step.</summary>
    public double PowerGenerationW { get; internal set; }

    /// <summary>Power drawn by loads during the last electrical step.</summary>
    public double PowerLoadW { get; internal set; }

    /// <summary>Read-only view of a component; mutation goes through the Set/Draw methods so events and topology stay in step.</summary>
    public ref readonly ComponentState this[int index] => ref _components[index];

    /// <summary>Writable component state for this area's solvers, which publish their own events.</summary>
    internal ref ComponentState Mutable(int index) => ref _components[index];

    public bool IsSectionAttached(int section) => _sectionAttached[section];

    public bool IsAttached(int index) => _sectionAttached[Layout.SectionOf(index)];

    /// <summary>Attached and neither failed nor isolated.</summary>
    public bool IsOperable(int index) =>
        IsAttached(index) && _components[index].Condition is ComponentCondition.Nominal or ComponentCondition.Degraded;

    public void MarkTopologyChanged() => TopologyVersion++;

    public void SetCondition(int index, ComponentCondition condition, MissionTime now)
    {
        ref var state = ref _components[index];
        if (state.Condition == condition) return;
        state.Condition = condition;
        MarkTopologyChanged();
        Changed(now, index, ComponentFields.Condition, ComponentFields.ConditionName(condition));
    }

    public void SetFault(int index, string fault, MissionTime now)
    {
        ArgumentNullException.ThrowIfNull(fault);
        ref var state = ref _components[index];
        if (string.Equals(state.Fault, fault, StringComparison.Ordinal)) return;
        state.Fault = fault;
        Changed(now, index, ComponentFields.Fault, fault);
    }

    public void SetClosed(int index, bool closed, MissionTime now)
    {
        ref var state = ref _components[index];
        if (state.Closed == closed) return;
        state.Closed = closed;
        MarkTopologyChanged();
        Changed(now, index, ComponentFields.Closed, closed);
    }

    public void SetArmed(int group, bool armed, MissionTime now)
    {
        ref var state = ref _components[group];
        if (state.Armed == armed) return;
        state.Armed = armed;
        Changed(now, group, ComponentFields.Armed, armed);
    }

    public void SetOn(int load, bool on, MissionTime now)
    {
        ref var state = ref _components[load];
        if (state.On == on) return;
        state.On = on;
        Changed(now, load, ComponentFields.On, on);
    }

    public void SetOnline(int source, bool online, MissionTime now)
    {
        ref var state = ref _components[source];
        if (state.Online == online) return;
        state.Online = online;
        MarkTopologyChanged();
        Changed(now, source, ComponentFields.Online, online);
    }

    /// <summary>Feeds <paramref name="load"/> from <paramref name="source"/> (a bus or outlet) through a repair jumper; -1 removes it.</summary>
    public void SetJumper(int load, int source, MissionTime now)
    {
        ref var state = ref _components[load];
        if (state.Jumper == source) return;
        state.Jumper = source;
        MarkTopologyChanged();
        Changed(now, load, ComponentFields.Jumper, source < 0 ? "" : Layout.IdOf(source).Value);
    }

    public void SetLatched(int port, bool latched, MissionTime now)
    {
        ref var state = ref _components[port];
        if (state.Latched == latched) return;
        state.Latched = latched;
        Changed(now, port, ComponentFields.Latched, latched);
    }

    /// <summary>Sets the fraction a <c>short_on</c> driver holds the thruster valve open, clamped to [0, 1].</summary>
    public void SetStuckFraction(int thruster, double fraction) =>
        _components[thruster].StuckFraction = Math.Clamp(Finite.Require(fraction, nameof(fraction)), 0, 1);

    public void SetQuantityKg(int tank, double kg, MissionTime now)
    {
        ref var state = ref _components[tank];
        state.QuantityKg = Math.Clamp(Finite.Require(kg, nameof(kg)), 0, Layout[tank].Tank!.CapacityKg);
        UpdateDepleted(tank, now);
    }

    /// <summary>Removes up to <paramref name="kg"/> from a tank.</summary>
    /// <returns>Mass actually drawn: less when the tank runs dry, zero when it is detached or isolated.</returns>
    public double DrawFromTank(int tank, double kg, MissionTime now)
    {
        Finite.Require(kg, nameof(kg));
        if (kg <= 0 || !IsAttached(tank)) return 0;
        ref var state = ref _components[tank];
        if (state.Condition == ComponentCondition.Isolated) return 0;
        var drawn = Math.Min(kg, state.QuantityKg);
        state.QuantityKg -= drawn;
        UpdateDepleted(tank, now);
        return drawn;
    }

    /// <summary>Adds up to <paramref name="kg"/> to a tank, limited by its capacity.</summary>
    /// <returns>Mass actually stored.</returns>
    public double AddToTank(int tank, double kg, MissionTime now)
    {
        Finite.Require(kg, nameof(kg));
        if (kg <= 0 || !IsAttached(tank)) return 0;
        ref var state = ref _components[tank];
        var added = Math.Min(kg, Layout[tank].Tank!.CapacityKg - state.QuantityKg);
        state.QuantityKg += added;
        UpdateDepleted(tank, now);
        return added;
    }

    public void SetEnergyKwh(int battery, double kwh) =>
        _components[battery].EnergyKwh = Math.Clamp(Finite.Require(kwh, nameof(kwh)), 0, Layout[battery].Battery!.CapacityKwh);

    public void SetEfficiency(int fuelCell, double efficiency) =>
        _components[fuelCell].Efficiency = Math.Max(0, Finite.Require(efficiency, nameof(efficiency)));

    public void SetBias(int sensor, double bias) => _components[sensor].Bias = Finite.Require(bias, nameof(bias));

    public void SetTemperatureC(int index, double temperatureC) =>
        _components[index].TemperatureC = Finite.Require(temperatureC, nameof(temperatureC));

    /// <returns>False when the parachute is detached, failed, isolated or already out.</returns>
    public bool SetParachute(int parachute, ParachuteStatus status, MissionTime now)
    {
        ref var state = ref _components[parachute];
        if (state.Parachute == status) return true;
        if (!IsOperable(parachute) && status == ParachuteStatus.Deployed) return false;
        if (state.Parachute == ParachuteStatus.Cut || (state.Parachute == ParachuteStatus.Deployed && status == ParachuteStatus.Stowed)) return false;
        state.Parachute = status;
        Changed(now, parachute, ComponentFields.State, ComponentFields.ParachuteName(status));
        return true;
    }

    /// <summary>Ignites a solid retro rocket, which then burns for its full burn time.</summary>
    /// <returns>False when it is detached, failed, isolated or already fired.</returns>
    public bool IgniteRetro(int rocket, MissionTime now)
    {
        ref var state = ref _components[rocket];
        if (state.Fired || !IsOperable(rocket)) return false;
        state.Fired = true;
        state.BurnRemainingS = Layout[rocket].RetroRocket!.BurnS;
        Changed(now, rocket, ComponentFields.Fired, true);
        return true;
    }

    /// <summary>Separates the section whose jettison id is <paramref name="eventId"/> (staging).</summary>
    /// <returns>False when no attached section has that jettison id.</returns>
    public bool Jettison(string eventId, MissionTime now)
    {
        var section = Layout.SectionWithJettison(eventId);
        if (section < 0 || !_sectionAttached[section]) return false;
        _sectionAttached[section] = false;
        MarkTopologyChanged();
        Events.Publish(new SectionJettisoned(now, VehicleId, Layout.Sections[section].Id, eventId));
        return true;
    }

    /// <summary>Mass, centre of mass and principal inertia of attached sections, tank contents and unburnt retro propellant.</summary>
    public MassProperties ComputeMassProperties()
    {
        var massKg = 0.0;
        var moment = Vec3d.Zero;
        var inertia = Vec3d.Zero;

        for (var s = 0; s < _sectionAttached.Length; s++)
        {
            if (!_sectionAttached[s]) continue;
            var section = Layout.Sections[s];
            var position = Layout.SectionCenterOfMassM(s);
            var own = new Vec3d(section.InertiaKgM2.Roll, section.InertiaKgM2.Pitch, section.InertiaKgM2.Yaw);
            Accumulate(section.DryMassKg, position, own, ref massKg, ref moment, ref inertia);
        }

        foreach (var tank in Layout.Tanks)
        {
            if (IsAttached(tank)) Accumulate(_components[tank].QuantityKg, Layout.PositionM(tank), Vec3d.Zero, ref massKg, ref moment, ref inertia);
        }

        foreach (var rocket in Layout.RetroRockets)
        {
            if (!IsAttached(rocket)) continue;
            var spec = Layout[rocket].RetroRocket!;
            var remaining = _components[rocket].Fired ? spec.PropellantKg * _components[rocket].BurnRemainingS / spec.BurnS : spec.PropellantKg;
            Accumulate(remaining, Layout.PositionM(rocket), Vec3d.Zero, ref massKg, ref moment, ref inertia);
        }

        if (massKg <= 0) return new MassProperties(0, Vec3d.Zero, Vec3d.Zero);
        var center = moment / massKg;
        return new MassProperties(massKg, center, inertia - ParallelAxis(massKg, center));
    }

    public void WriteHash(StateHashWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(VehicleId);
        foreach (var attached in _sectionAttached) writer.Write(attached);
        writer.Write(PowerGenerationW);
        writer.Write(PowerLoadW);
        for (var i = 0; i < _components.Length; i++)
        {
            ref readonly var s = ref _components[i];
            writer.Write((long)s.Condition);
            writer.Write(s.Fault);
            writer.Write(s.Closed);
            writer.Write(s.Armed);
            writer.Write(s.On);
            writer.Write(s.Online);
            writer.Write(s.Energized);
            writer.Write(s.Powered);
            writer.Write(s.Brownout);
            writer.Write(s.VoltageV);
            writer.Write(s.PowerW);
            writer.Write(s.HeatW);
            writer.Write(s.QuantityKg);
            writer.Write(s.Depleted);
            writer.Write(s.EnergyKwh);
            writer.Write(s.Efficiency);
            writer.Write(s.ValveOpen);
            writer.Write(s.StuckFraction);
            writer.Write(s.TemperatureC);
            writer.Write(s.FiringSeconds);
            writer.Write(s.Activity);
            writer.Write(s.Bias);
            writer.Write(s.Latched);
            writer.Write(s.LoadFraction);
            writer.Write(s.OverstressS);
            writer.Write(s.OverstressAlarm);
            writer.Write(s.Fired);
            writer.Write(s.BurnRemainingS);
            writer.Write((long)s.Parachute);
            writer.Write(s.Jumper);
        }
    }

    internal static double KwhFromJoules(double joules) => joules / JoulesPerKwh;

    internal static double JoulesFromKwh(double kwh) => kwh * JoulesPerKwh;

    internal void Changed(MissionTime now, int index, string field, StateValue value) =>
        Events.Publish(new ComponentStateChanged(now, Layout.IdOf(index), field, value));

    private void UpdateDepleted(int tank, MissionTime now)
    {
        ref var state = ref _components[tank];
        var empty = state.QuantityKg <= 0;
        if (empty == state.Depleted) return;
        state.Depleted = empty;
        if (empty) Events.Publish(new TankDepleted(now, Layout.IdOf(tank), Layout[tank].Tank!.Resource));
    }

    private static void Accumulate(double massKg, Vec3d position, Vec3d ownInertia, ref double totalKg, ref Vec3d moment, ref Vec3d inertia)
    {
        if (massKg <= 0) return;
        totalKg += massKg;
        moment += position * massKg;
        inertia += ownInertia + ParallelAxis(massKg, position);
    }

    private static Vec3d ParallelAxis(double massKg, Vec3d r) =>
        new(massKg * (r.Y * r.Y + r.Z * r.Z), massKg * (r.X * r.X + r.Z * r.Z), massKg * (r.X * r.X + r.Y * r.Y));

    private static ComponentState Initial(VehicleLayout layout, int index)
    {
        var c = layout[index];
        return new ComponentState
        {
            Condition = ComponentCondition.Nominal,
            Fault = "",
            Closed = (c.Breaker ?? c.Switch)?.InitiallyClosed
                     ?? (c.Family == Definitions.ComponentFamily.Hatch || c.Valve is { InitiallyOpen: false }),
            Armed = c.ThrusterGroup?.InitiallyArmed ?? false,
            On = c.Load?.InitiallyOn ?? false,
            Online = c.FuelCell?.InitiallyOnline ?? c.Battery?.InitiallyOnline ?? false,
            QuantityKg = c.Tank?.InitialKg ?? 0,
            Depleted = c.Tank is { InitialKg: <= 0 },
            EnergyKwh = c.Battery?.InitialKwh ?? 0,
            Efficiency = 1,
            TemperatureC = layout.ThrusterSpecOf(index)?.ColdC ?? 0,
            Bias = c.Sensor?.InitialBias ?? 0,
            Latched = c.Docking?.LatchedInitially ?? false,
            Parachute = ParachuteStatus.Stowed,
            Jumper = -1,
        };
    }
}
