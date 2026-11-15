using Usa.Simulation.Data;
using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Attitude;

/// <summary>
/// Attitude dynamics and control for every vehicle, every 50 ms tick. Per body: the one member whose controller
/// is on commands its thrusters, every member's thrusters fire (forced ones too), and the summed torque drives
/// the rigid body. Docking merges two vehicles into one body; undocking splits it again.
/// Vehicle ids, control sets and docking geometry all come from data: the supplied vehicles are the mission's, and
/// <c>attitude.json</c> entries (vehicles, docking pairs) naming vehicles outside the mission are ignored.
/// </summary>
public sealed class AttitudeSystem : ISimSystem, IHashContributor
{
    private readonly AttitudeVehicle[] _vehicles;
    private readonly Dictionary<string, AttitudeVehicle> _vehiclesById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AttitudeVehicle>.AlternateLookup<ReadOnlySpan<char>> _vehiclesBySpan;
    private readonly List<AttitudeBody> _bodies = [];
    private readonly IReadOnlyList<DockingDefinition> _docking;
    private readonly double[] _thresholdsDegPerSec;
    private readonly double _thresholdHysteresisDegPerSec;
    private readonly double _dutyTimeConstantS;
    private readonly EventBus _events;
    private readonly IAttitudeBridge? _bridge;
    private MissionTime _now;

    /// <exception cref="DefinitionException">The config is invalid or doesn't match the vehicles; lists every problem.</exception>
    public AttitudeSystem(
        AttitudeConfigDefinition config,
        IReadOnlyList<AttitudeVehicleSpec> vehicles,
        EventBus events,
        IAttitudeBridge? bridge = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(vehicles);
        ArgumentNullException.ThrowIfNull(events);

        var errors = new DefinitionErrors();
        AttitudeConfigLoader.Validate(config, AttitudeConfigLoader.RelativePath, errors);
        ValidateVehicles(config, vehicles, errors);
        errors.ThrowIfAny();

        _events = events;
        _bridge = bridge;
        _docking = [.. config.Docking.Where(d => vehicles.Any(v => v.Id == d.Primary) && vehicles.Any(v => v.Id == d.Secondary))];
        _thresholdsDegPerSec = [.. config.RateThresholdsDegPerSec];
        _thresholdHysteresisDegPerSec = config.ThresholdHysteresisDegPerSec;
        _dutyTimeConstantS = config.FiringDutyTimeConstantS;

        var tuning = ControllerTuning.FromDefinition(config);
        _vehicles = new AttitudeVehicle[vehicles.Count];
        var sorted = vehicles.OrderBy(v => v.Id, StringComparer.Ordinal).ToArray();
        for (var i = 0; i < sorted.Length; i++)
        {
            var definition = config.Vehicles.FirstOrDefault(d => d.Id == sorted[i].Id);
            var vehicle = new AttitudeVehicle(sorted[i], definition, tuning, _thresholdsDegPerSec.Length);
            _vehicles[i] = vehicle;
            _vehiclesById.Add(vehicle.Id, vehicle);
            _bodies.Add(vehicle.OwnBody);
            vehicle.UpdateOutputs(0);
        }
        _vehiclesBySpan = _vehiclesById.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    public int PeriodTicks => 1;

    /// <summary>Sorted by id.</summary>
    public IReadOnlyList<AttitudeVehicle> Vehicles => _vehicles;

    public AttitudeVehicle GetVehicle(string vehicleId) =>
        _vehiclesById.TryGetValue(vehicleId, out var vehicle)
            ? vehicle
            : throw new ArgumentException($"Unknown vehicle '{vehicleId}'.", nameof(vehicleId));

    public bool TryGetVehicle(ReadOnlySpan<char> vehicleId, out AttitudeVehicle vehicle) =>
        _vehiclesBySpan.TryGetValue(vehicleId, out vehicle!);

    public void SetMode(string vehicleId, AttitudeMode mode, MissionTime now) =>
        SetControl(vehicleId, mode, GetVehicle(vehicleId).ControlSetId, now);

    public void SetControlSet(string vehicleId, string controlSetId, MissionTime now) =>
        SetControl(vehicleId, GetVehicle(vehicleId).Mode, controlSetId, now);

    /// <summary>
    /// Selects the control law and its thruster set; publishes <see cref="AttitudeModeChanged"/> on a change.
    /// Only one controller may act on a rigid body: switching a docked vehicle's controller on switches every other
    /// member's controller off (each sized its command for the whole stack, so two would double the authority).
    /// </summary>
    public void SetControl(string vehicleId, AttitudeMode mode, string controlSetId, MissionTime now)
    {
        var vehicle = GetVehicle(vehicleId);
        var previousMode = vehicle.Mode;
        var previousSet = vehicle.ControlSetId;
        if (previousMode == mode && previousSet == controlSetId) return;
        vehicle.Controller.Configure(mode, controlSetId);
        _events.Publish(new AttitudeModeChanged(now, vehicle.Id, previousMode, mode, previousSet, controlSetId));
        if (mode != AttitudeMode.Off) SwitchOffOthers(vehicle, now);
    }

    /// <summary>In-plane inertial angle the X axis is held at in <see cref="AttitudeMode.AttitudeHold"/>.</summary>
    public void SetHoldTarget(string vehicleId, double targetDeg) =>
        GetVehicle(vehicleId).Controller.SetHoldTarget(targetDeg * Angles.DegToRad);

    /// <summary>Sets a vehicle's orientation (vehicle → inertial) and body rates (vehicle frame); moves its whole stack when docked.</summary>
    public void SetState(string vehicleId, Quatd orientation, Vec3d angularVelocityRadPerSec)
    {
        var vehicle = GetVehicle(vehicleId);
        var mount = vehicle.Mount;
        vehicle.Body.Rigid.SetState(orientation * mount.Rotation.Conjugate, mount.ToStack(angularVelocityRadPerSec));
        RefreshOutputs(vehicle.Body);
    }

    /// <summary>
    /// Changes a vehicle's mass properties (jettison, propellant use); body rates are kept. A centre-of-mass shift
    /// (<see cref="MassProperties.ComXM"/>) moves the thrusters' lever arms and, when docked, the stack geometry.
    /// </summary>
    public void SetMassProperties(string vehicleId, MassProperties massProperties)
    {
        if (!massProperties.IsValid) throw new ArgumentOutOfRangeException(nameof(massProperties), massProperties, "Mass properties must be positive and finite.");
        var vehicle = GetVehicle(vehicleId);
        vehicle.MassProperties = massProperties;
        if (vehicle.DockedWithId is null)
        {
            vehicle.Body.Rigid.SetInertia(massProperties.InertiaKgM2);
            if (vehicle.Mount != vehicle.OwnMount) vehicle.Thrusters.SetMount(vehicle.OwnMount);
            return;
        }

        var members = vehicle.Body.Members;
        var docking = FindDocking(members[0].Id, members[1].Id)!;
        var merged = MergeStack(members[0], members[1], docking);
        vehicle.Body.Rigid.SetInertia(merged.Combined.InertiaKgM2);
        AttachStack(vehicle.Body, members[0], members[1], docking, merged.ComXM);
    }

    public bool CanDock(string vehicleA, string vehicleB) =>
        FindDocking(vehicleA, vehicleB) is not null &&
        GetVehicle(vehicleA).DockedWithId is null &&
        GetVehicle(vehicleB).DockedWithId is null;

    /// <summary>
    /// Joins two free vehicles into one rigid body using the configured geometry. The stack takes the
    /// primary's orientation and the rates that conserve the pair's angular momentum. If both controllers are on,
    /// the secondary (the docking target, whose attitude system holds the stack) keeps control and
    /// the primary's is switched off (one controller per body, see <see cref="SetControl"/>).
    /// </summary>
    /// <param name="now">Time stamped on a resulting <see cref="AttitudeModeChanged"/>; defaults to the last step's time.</param>
    public void Dock(string vehicleA, string vehicleB, MissionTime? now = null)
    {
        var docking = FindDocking(vehicleA, vehicleB)
                      ?? throw new InvalidOperationException($"No docking geometry for {vehicleA}/{vehicleB}.");
        var primary = GetVehicle(docking.Primary);
        var secondary = GetVehicle(docking.Secondary);
        if (primary.DockedWithId is not null || secondary.DockedWithId is not null)
            throw new InvalidOperationException($"{primary.Id} or {secondary.Id} is already docked.");

        var merged = MergeStack(primary, secondary, docking);
        var primaryRigid = primary.OwnBody.Rigid;
        var momentumInertial = primaryRigid.AngularMomentumInertialNms + secondary.OwnBody.Rigid.AngularMomentumInertialNms;
        var momentumStack = primaryRigid.Orientation.Conjugate.Rotate(momentumInertial);
        var inertia = merged.Combined.InertiaKgM2;
        var rigid = new RigidBodyAttitude(inertia);
        rigid.SetState(
            primaryRigid.Orientation,
            new Vec3d(momentumStack.X / inertia.X, momentumStack.Y / inertia.Y, momentumStack.Z / inertia.Z));

        var stack = new AttitudeBody(rigid, [primary, secondary]);
        AttachStack(stack, primary, secondary, docking, merged.ComXM);
        _bodies.Remove(primary.OwnBody);
        _bodies.Remove(secondary.OwnBody);
        _bodies.Add(stack);
        SortBodies();
        if (secondary.Mode != AttitudeMode.Off) SwitchOffOthers(secondary, now ?? _now);
        else if (primary.Mode != AttitudeMode.Off) SwitchOffOthers(primary, now ?? _now);
        RefreshOutputs(stack);
    }

    /// <summary>Splits the stack containing <paramref name="vehicleId"/>; each vehicle keeps the stack's angular velocity.</summary>
    public void Undock(string vehicleId)
    {
        var stack = GetVehicle(vehicleId).Body;
        if (stack.Members.Length < 2) throw new InvalidOperationException($"{vehicleId} is not docked.");

        var orientation = stack.Rigid.Orientation;
        var omega = stack.Rigid.AngularVelocityRadPerSec;
        _bodies.Remove(stack);
        foreach (var member in stack.Members)
        {
            var mount = member.Mount;
            var own = member.OwnBody.Rigid;
            own.SetInertia(member.MassProperties.InertiaKgM2);
            own.SetState(orientation * mount.Rotation, mount.FromStack(omega));
            member.Detach();
            _bodies.Add(member.OwnBody);
            RefreshOutputs(member.OwnBody);
        }
        SortBodies();
    }

    public void Step(in StepContext context)
    {
        var dtS = context.DeltaSeconds;
        _now = context.Now;
        if (_bridge is not null)
        {
            for (var i = 0; i < _vehicles.Length; i++) _bridge.BeforeStep(_vehicles[i], context);
        }

        for (var b = 0; b < _bodies.Count; b++)
        {
            var body = _bodies[b];
            var rigid = body.Rigid;
            var torqueNm = Vec3d.Zero;
            var forceN = Vec3d.Zero;
            var members = body.Members;
            for (var m = 0; m < members.Length; m++)
            {
                var vehicle = members[m];
                var frame = new ControlFrame(rigid.AngularVelocityRadPerSec, rigid.InertiaKgM2, rigid.Orientation, vehicle.Mount);
                vehicle.Controller.Compute(frame, vehicle.Thrusters, vehicle.Commands, dtS);
                vehicle.Thrusters.Fire(vehicle.Commands, dtS);
                torqueNm += vehicle.Thrusters.NetTorqueNm + vehicle.Mount.ToStack(vehicle.ExternalTorqueNm);
                forceN += vehicle.Thrusters.NetForceN;
            }
            rigid.Step(torqueNm, dtS);
            body.NetForceN = forceN;
        }

        var dutyBlend = 1.0 - Math.Exp(-dtS / _dutyTimeConstantS);
        for (var i = 0; i < _vehicles.Length; i++)
        {
            _vehicles[i].UpdateOutputs(dutyBlend);
            PublishThresholdCrossings(_vehicles[i], context.Now);
        }

        if (_bridge is not null)
        {
            for (var i = 0; i < _vehicles.Length; i++) _bridge.AfterStep(_vehicles[i], context);
        }
    }

    public void WriteHash(StateHashWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        for (var i = 0; i < _vehicles.Length; i++) _vehicles[i].WriteHash(writer);
        for (var b = 0; b < _bodies.Count; b++)
        {
            writer.Write(_bodies[b].Key);
            _bodies[b].Rigid.WriteHash(writer);
        }
    }

    private void PublishThresholdCrossings(AttitudeVehicle vehicle, MissionTime now)
    {
        var rate = vehicle.RateTotalDegPerSec;
        var above = vehicle.AboveThreshold;
        for (var k = 0; k < _thresholdsDegPerSec.Length; k++)
        {
            if (above[k] || rate <= _thresholdsDegPerSec[k]) continue;
            above[k] = true;
            _events.Publish(new RateThresholdCrossed(now, vehicle.Id, _thresholdsDegPerSec[k], rate, Rising: true));
        }
        for (var k = _thresholdsDegPerSec.Length - 1; k >= 0; k--)
        {
            if (!above[k] || rate >= _thresholdsDegPerSec[k] - _thresholdHysteresisDegPerSec) continue;
            above[k] = false;
            _events.Publish(new RateThresholdCrossed(now, vehicle.Id, _thresholdsDegPerSec[k], rate, Rising: false));
        }
    }

    private static void RefreshOutputs(AttitudeBody body)
    {
        foreach (var member in body.Members) member.UpdateOutputs(0);
    }

    private DockingDefinition? FindDocking(string vehicleA, string vehicleB)
    {
        for (var i = 0; i < _docking.Count; i++)
        {
            var docking = _docking[i];
            if ((docking.Primary == vehicleA && docking.Secondary == vehicleB) ||
                (docking.Primary == vehicleB && docking.Secondary == vehicleA))
                return docking;
        }
        return null;
    }

    /// <summary>Stack mass properties from both members' current centres of mass (primary frame, primary reference point at 0).</summary>
    private static MergedMassProperties MergeStack(AttitudeVehicle primary, AttitudeVehicle secondary, DockingDefinition docking)
    {
        var secondaryComXM = docking.SecondaryComOffsetXM +
                             (docking.SecondaryReversed ? -secondary.MassProperties.ComXM : secondary.MassProperties.ComXM);
        return MassProperties.Merge(primary.MassProperties, primary.MassProperties.ComXM, secondary.MassProperties, secondaryComXM);
    }

    /// <summary>Mounts measure each member's reference point (where its thruster positions are anchored) from the stack centre of mass.</summary>
    private static void AttachStack(
        AttitudeBody stack, AttitudeVehicle primary, AttitudeVehicle secondary, DockingDefinition docking, double stackComXM)
    {
        primary.AttachTo(stack, new StackMount(-stackComXM, false));
        secondary.AttachTo(stack, new StackMount(docking.SecondaryComOffsetXM - stackComXM, docking.SecondaryReversed));
    }

    private void SwitchOffOthers(AttitudeVehicle active, MissionTime now)
    {
        var members = active.Body.Members;
        for (var i = 0; i < members.Length; i++)
        {
            var other = members[i];
            if (ReferenceEquals(other, active) || other.Mode == AttitudeMode.Off) continue;
            var previousMode = other.Mode;
            other.Controller.Configure(AttitudeMode.Off, other.ControlSetId);
            _events.Publish(new AttitudeModeChanged(now, other.Id, previousMode, AttitudeMode.Off, other.ControlSetId, other.ControlSetId));
        }
    }

    private void SortBodies() => _bodies.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));

    private static void ValidateVehicles(AttitudeConfigDefinition config, IReadOnlyList<AttitudeVehicleSpec> vehicles, DefinitionErrors errors)
    {
        var specsById = new Dictionary<string, AttitudeVehicleSpec>(StringComparer.Ordinal);
        foreach (var spec in vehicles)
        {
            var file = $"vehicle {spec.Id}";
            if (string.IsNullOrEmpty(spec.Id)) errors.Add(file, "$.id", "Vehicle id is empty.");
            else if (!specsById.TryAdd(spec.Id, spec)) errors.Add(file, "$.id", "Vehicle is listed twice.");
            if (!spec.MassProperties.IsValid) errors.Add(file, "$.massKg", "Mass and inertia must be positive and finite.");

            var thrusterIds = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < spec.Thrusters.Count; i++)
            {
                var problem = ThrusterSet.FindProblem(spec.Thrusters[i]);
                if (problem is not null) errors.Add(file, $"$.thrusters[{i}]", problem);
                else if (!thrusterIds.Add(spec.Thrusters[i].Id)) errors.Add(file, $"$.thrusters[{i}]", $"Thruster '{spec.Thrusters[i].Id}' is listed twice.");
            }
        }

        var configFile = AttitudeConfigLoader.RelativePath;
        for (var v = 0; v < config.Vehicles.Count; v++)
        {
            var definition = config.Vehicles[v];
            // The config is shared by every mission; entries for other missions' vehicles don't apply here.
            if (!specsById.TryGetValue(definition.Id, out var spec)) continue;

            for (var s = 0; s < definition.ControlSets.Count; s++)
            {
                var set = definition.ControlSets[s];
                var path = $"$.vehicles[{v}].controlSets[{s}]";
                var thrusters = set.Thrusters ?? [];
                for (var t = 0; t < thrusters.Count; t++)
                {
                    if (!spec.Thrusters.Any(x => x?.Id == thrusters[t]))
                        errors.Add(configFile, $"{path}.thrusters[{t}]", $"Vehicle '{spec.Id}' has no thruster '{thrusters[t]}'.");
                }
                var groups = set.Groups ?? [];
                for (var g = 0; g < groups.Count; g++)
                {
                    if (!spec.Thrusters.Any(x => x?.GroupId == groups[g]))
                        errors.Add(configFile, $"{path}.groups[{g}]", $"Vehicle '{spec.Id}' has no thruster in group '{groups[g]}'.");
                }
            }
        }
    }
}
