using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Attitude;

/// <summary>
/// One vehicle in the attitude model: its thrusters, controller and the outputs other areas read.
/// Outputs are in the vehicle's own frame even while it is part of a docked stack.
/// </summary>
public sealed class AttitudeVehicle
{
    private readonly double[] _commands;
    private readonly bool[] _aboveThreshold;

    internal AttitudeVehicle(AttitudeVehicleSpec spec, AttitudeVehicleDefinition? definition, ControllerTuning tuning, int thresholdCount)
    {
        Spec = spec;
        MassProperties = spec.MassProperties;
        Thrusters = new ThrusterSet(spec.Thrusters);
        Controller = new AttitudeController(
            tuning,
            Thrusters,
            definition?.ControlSets ?? [],
            definition?.InitialMode ?? AttitudeMode.Off,
            definition?.InitialControlSet ?? "");
        _commands = new double[Thrusters.Count];
        _aboveThreshold = new bool[thresholdCount];
        OwnBody = new AttitudeBody(new RigidBodyAttitude(spec.MassProperties.InertiaKgM2), [this]);
        Thrusters.SetMount(OwnMount);
        Body = OwnBody;
        Orientation = Quatd.Identity;
    }

    public string Id => Spec.Id;

    public AttitudeVehicleSpec Spec { get; }

    public MassProperties MassProperties { get; internal set; }

    public ThrusterSet Thrusters { get; }

    public AttitudeMode Mode => Controller.Mode;

    public string ControlSetId => Controller.ControlSetId;

    public IReadOnlyList<string> ControlSetIds => Controller.ControlSetIds;

    public double HoldTargetDeg => Controller.HoldTargetRad * Angles.RadToDeg;

    /// <summary>Whether the active control set lets the controller use this thruster.</summary>
    public bool IsEnabledForControl(int thrusterIndex) => Controller.IsEnabled(thrusterIndex);

    /// <summary>Extra torque on the vehicle, vehicle frame (e.g. retro-rocket misalignment); persists until changed.</summary>
    public Vec3d ExternalTorqueNm
    {
        get;
        set => field = double.IsFinite(value.LengthSquared)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, $"External torque on {Id} must be finite.");
    }

    /// <summary>The other vehicle of the docked stack, or null when free.</summary>
    public string? DockedWithId
    {
        get
        {
            var members = Body.Members;
            if (members.Length < 2) return null;
            return ReferenceEquals(members[0], this) ? members[1].Id : members[0].Id;
        }
    }

    public StackMount Mount => Thrusters.Mount;

    /// <summary>Mount of the vehicle flying alone: its thrusters measured from its current centre of mass.</summary>
    public StackMount OwnMount => new(-MassProperties.ComXM, false);

    /// <summary>Vehicle frame, rad/s.</summary>
    public Vec3d AngularVelocityRadPerSec { get; private set; }

    /// <summary>Vehicle → inertial, for the 3D view.</summary>
    public Quatd Orientation { get; private set; }

    public double RateRollDegPerSec => AngularVelocityRadPerSec.X * Angles.RadToDeg;

    public double RatePitchDegPerSec => AngularVelocityRadPerSec.Y * Angles.RadToDeg;

    public double RateYawDegPerSec => AngularVelocityRadPerSec.Z * Angles.RadToDeg;

    public double RateTotalDegPerSec => AngularVelocityRadPerSec.Length * Angles.RadToDeg;

    /// <summary>X axis projected onto the orbital plane, inertial, [0, 360).</summary>
    public double PointingDeg { get; private set; }

    public double OutOfPlaneDeg { get; private set; }

    /// <summary>Angle between the X axis and the hold target in attitude hold; 0 otherwise.</summary>
    public double AttitudeErrorDeg { get; private set; }

    /// <summary>
    /// Signed in-plane error of the X axis from the hold target, (−180, 180], positive counter-clockwise; 0 outside
    /// attitude hold. This is what flight needs as the programmed-burn pointing error (see <c>IFlightInputs</c>).
    /// </summary>
    public double SignedErrorDeg { get; private set; }

    /// <summary>Smoothed mean valve fraction over this vehicle's thrusters, 0..1.</summary>
    public double FiringDuty { get; private set; }

    /// <summary>Net thruster force on the whole body this vehicle belongs to over the last step, vehicle frame.</summary>
    public Vec3d BodyThrusterForceN { get; private set; }

    internal AttitudeController Controller { get; }

    internal AttitudeBody OwnBody { get; }

    internal AttitudeBody Body { get; private set; }

    internal Span<double> Commands => _commands;

    internal Span<bool> AboveThreshold => _aboveThreshold;

    internal void AttachTo(AttitudeBody body, StackMount mount)
    {
        Body = body;
        Thrusters.SetMount(mount);
    }

    internal void Detach()
    {
        Body = OwnBody;
        Thrusters.SetMount(OwnMount);
    }

    internal void UpdateOutputs(double dutyBlend)
    {
        var rigid = Body.Rigid;
        var mount = Mount;
        AngularVelocityRadPerSec = mount.FromStack(rigid.AngularVelocityRadPerSec);
        Orientation = rigid.Orientation * mount.Rotation;
        BodyThrusterForceN = mount.FromStack(Body.NetForceN);

        var xInertial = Orientation.Rotate(Vec3d.UnitX);
        var pointingRad = PointingMath.InPlaneAngleRad(xInertial);
        PointingDeg = pointingRad * Angles.RadToDeg;
        OutOfPlaneDeg = PointingMath.OutOfPlaneRad(xInertial) * Angles.RadToDeg;
        var holding = Mode == AttitudeMode.AttitudeHold;
        AttitudeErrorDeg = holding
            ? PointingMath.AngleBetweenRad(xInertial, PointingMath.InPlaneDirection(Controller.HoldTargetRad)) * Angles.RadToDeg
            : 0;
        SignedErrorDeg = holding ? Angles.Wrap(pointingRad - Controller.HoldTargetRad) * Angles.RadToDeg : 0;

        var fractions = Thrusters.EffectiveFraction;
        var sum = 0.0;
        for (var i = 0; i < fractions.Length; i++) sum += fractions[i];
        var mean = fractions.Length > 0 ? sum / fractions.Length : 0;
        FiringDuty += (mean - FiringDuty) * dutyBlend;
    }

    internal void WriteHash(StateHashWriter writer)
    {
        writer.Write(Id);
        Controller.WriteHash(writer);
        Thrusters.WriteHash(writer);
        writer.Write(Mount.OffsetXM);
        writer.Write(Mount.Reversed);
        writer.Write(MassProperties.MassKg);
        writer.Write(ExternalTorqueNm.X);
        writer.Write(ExternalTorqueNm.Y);
        writer.Write(ExternalTorqueNm.Z);
        writer.Write(FiringDuty);
        for (var i = 0; i < _aboveThreshold.Length; i++) writer.Write(_aboveThreshold[i]);
    }
}
