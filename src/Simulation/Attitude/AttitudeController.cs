using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Attitude;

/// <summary>
/// One vehicle's attitude controller. Per axis it demands torque against the rate error with a
/// deadband and hysteresis, then picks thrusters bang-bang by the sign of each one's torque on the
/// demanded axes, scaling the valve fraction so one step doesn't overshoot. It only uses thrusters
/// in its enabled control set that can fire on command, and knows nothing about forced thrusters:
/// it simply fights whatever torque shows up in the rates.
/// </summary>
internal sealed class AttitudeController
{
    private const int RollBit = 1;
    private const int PitchBit = 2;
    private const int YawBit = 4;

    private readonly ControllerTuning _tuning;
    private readonly Dictionary<string, bool[]> _enabledBySet;
    private readonly string[] _controlSetIds;
    private readonly int[] _selectedAxes;
    private readonly bool[] _axisActive = new bool[3];
    private bool[] _enabled;

    public AttitudeController(
        ControllerTuning tuning,
        ThrusterSet thrusters,
        IReadOnlyList<ControlSetDefinition> controlSets,
        AttitudeMode initialMode,
        string initialControlSet)
    {
        _tuning = tuning;
        _selectedAxes = new int[thrusters.Count];
        _enabledBySet = new Dictionary<string, bool[]>(StringComparer.Ordinal) { [""] = new bool[thrusters.Count] };
        for (var s = 0; s < controlSets.Count; s++)
        {
            var set = controlSets[s];
            var mask = new bool[thrusters.Count];
            for (var i = 0; i < thrusters.Count; i++)
            {
                var spec = thrusters.Specs[i];
                mask[i] = Contains(set.Thrusters, spec.Id) || (spec.GroupId.Length > 0 && Contains(set.Groups, spec.GroupId));
            }
            _enabledBySet[set.Id] = mask;
        }

        _controlSetIds = [.. _enabledBySet.Keys.Where(id => id.Length > 0).Order(StringComparer.Ordinal)];
        _enabled = _enabledBySet[""];
        ControlSetId = "";
        Configure(initialMode, initialControlSet);
    }

    public AttitudeMode Mode { get; private set; }

    /// <summary>Active control set; empty when none is selected.</summary>
    public string ControlSetId { get; private set; }

    public IReadOnlyList<string> ControlSetIds => _controlSetIds;

    /// <summary>Inertial in-plane angle the hold mode points the vehicle X axis at, [0, 2π).</summary>
    public double HoldTargetRad { get; private set; }

    public bool HasControlSet(string controlSetId) => _enabledBySet.ContainsKey(controlSetId);

    public bool IsEnabled(int thrusterIndex) => _enabled[thrusterIndex];

    public void Configure(AttitudeMode mode, string controlSetId)
    {
        if (!_enabledBySet.TryGetValue(controlSetId, out var mask))
            throw new ArgumentException($"Unknown control set '{controlSetId}'.", nameof(controlSetId));
        Mode = mode;
        ControlSetId = controlSetId;
        _enabled = mask;
        Array.Clear(_axisActive);
    }

    public void SetHoldTarget(double targetRad)
    {
        if (!double.IsFinite(targetRad)) throw new ArgumentOutOfRangeException(nameof(targetRad), "Hold target must be finite.");
        HoldTargetRad = Angles.WrapPositive(targetRad);
    }

    /// <summary>Writes a valve command per thruster index into <paramref name="commands"/>.</summary>
    public void Compute(in ControlFrame frame, ThrusterSet thrusters, Span<double> commands, double dtS)
    {
        commands.Clear();
        if (Mode == AttitudeMode.Off) return;

        var law = Mode == AttitudeMode.AttitudeHold ? _tuning.AttitudeHold : _tuning.RateDamp;
        var desired = Mode == AttitudeMode.AttitudeHold ? DesiredHoldRate(frame, law) : Vec3d.Zero;
        var error = frame.AngularVelocityRadPerSec - desired;
        var demand = new Vec3d(
            Demand(ref _axisActive[0], error.X, law),
            Demand(ref _axisActive[1], error.Y, law),
            Demand(ref _axisActive[2], error.Z, law));
        if (demand == Vec3d.Zero) return;

        var timeConstantS = Math.Max(law.TimeConstantS, ControllerTuning.MinTimeConstantSteps * dtS);
        var neededRadPerS2 = new Vec3d(Math.Abs(error.X), Math.Abs(error.Y), Math.Abs(error.Z)) / timeConstantS;
        var inertia = frame.InertiaKgM2;
        var torques = thrusters.TorqueNm;
        var minAxisFraction = _tuning.MinAxisTorqueFraction;

        // Pass 1: thrusters that help a demanded axis and oppose none.
        var authority = Vec3d.Zero;
        for (var i = 0; i < commands.Length; i++)
        {
            _selectedAxes[i] = 0;
            if (!_enabled[i] || !thrusters.CanFireOnCommand(i)) continue;
            var helps = Classify(torques[i], demand, minAxisFraction, out var opposes);
            if (helps == 0 || opposes != 0) continue;
            _selectedAxes[i] = helps;
            authority += AxisAcceleration(torques[i], inertia, helps);
        }

        // Pass 2: a demanded axis no thruster could serve cleanly takes any thruster that helps it,
        // accepting cross-coupling that the other axes correct on later steps.
        for (var axis = 0; axis < 3; axis++)
        {
            if (Component(demand, axis) == 0 || Component(authority, axis) > 0) continue;
            var bit = 1 << axis;
            for (var i = 0; i < commands.Length; i++)
            {
                if (_selectedAxes[i] != 0 || !_enabled[i] || !thrusters.CanFireOnCommand(i)) continue;
                var helps = Classify(torques[i], demand, minAxisFraction, out _);
                if ((helps & bit) == 0) continue;
                _selectedAxes[i] = helps;
                authority += AxisAcceleration(torques[i], inertia, helps);
            }
        }

        for (var i = 0; i < commands.Length; i++)
        {
            var axes = _selectedAxes[i];
            if (axes == 0) continue;
            var fraction = 0.0;
            if ((axes & RollBit) != 0) fraction = Math.Max(fraction, neededRadPerS2.X / authority.X);
            if ((axes & PitchBit) != 0) fraction = Math.Max(fraction, neededRadPerS2.Y / authority.Y);
            if ((axes & YawBit) != 0) fraction = Math.Max(fraction, neededRadPerS2.Z / authority.Z);
            commands[i] = Math.Min(1.0, fraction);
        }
    }

    public void WriteHash(StateHashWriter writer)
    {
        writer.Write((long)Mode);
        writer.Write(ControlSetId);
        writer.Write(HoldTargetRad);
        for (var axis = 0; axis < _axisActive.Length; axis++) writer.Write(_axisActive[axis]);
    }

    /// <summary>
    /// Outer pointing loop: a slew-rate command about the shortest rotation that brings the vehicle X axis
    /// onto the target direction. Its component along X is zero, so roll is simply damped.
    /// </summary>
    private Vec3d DesiredHoldRate(in ControlFrame frame, in RateLaw law)
    {
        var xInertial = frame.Orientation.Rotate(frame.Mount.ToStack(Vec3d.UnitX));
        var target = PointingMath.InPlaneDirection(HoldTargetRad);
        var cross = xInertial.Cross(target);
        var sinAngle = cross.Length;
        var angleRad = Math.Atan2(sinAngle, xInertial.Dot(target));
        if (angleRad <= 0) return Vec3d.Zero;

        // Pointing exactly away from an in-plane target: turn about the orbit normal.
        var axisInertial = sinAngle > 1e-9 ? cross / sinAngle : Vec3d.UnitZ;
        var rateRadPerS = Math.Min(law.PointingGainPerS * angleRad, law.MaxSlewRadPerSec);
        return frame.Orientation.Conjugate.Rotate(axisInertial) * rateRadPerS;
    }

    private static double Demand(ref bool active, double errorRadPerS, in RateLaw law)
    {
        var magnitude = Math.Abs(errorRadPerS);
        if (active)
        {
            if (magnitude <= law.SettleRadPerSec) active = false;
        }
        else if (magnitude > law.DeadbandRadPerSec)
        {
            active = true;
        }
        return active ? (errorRadPerS > 0 ? -1.0 : 1.0) : 0.0;
    }

    /// <returns>Bit mask of demanded axes the torque helps; <paramref name="opposes"/> gets those it works against.</returns>
    private static int Classify(Vec3d torqueNm, Vec3d demand, double minAxisFraction, out int opposes)
    {
        var threshold = minAxisFraction * torqueNm.Length;
        var helps = 0;
        opposes = 0;
        Check(torqueNm.X, demand.X, threshold, RollBit, ref helps, ref opposes);
        Check(torqueNm.Y, demand.Y, threshold, PitchBit, ref helps, ref opposes);
        Check(torqueNm.Z, demand.Z, threshold, YawBit, ref helps, ref opposes);
        return helps;
    }

    private static void Check(double torqueNm, double demand, double threshold, int bit, ref int helps, ref int opposes)
    {
        if (demand == 0 || Math.Abs(torqueNm) <= threshold) return;
        if (torqueNm * demand > 0) helps |= bit;
        else opposes |= bit;
    }

    private static Vec3d AxisAcceleration(Vec3d torqueNm, Vec3d inertiaKgM2, int axes) => new(
        (axes & RollBit) != 0 ? Math.Abs(torqueNm.X) / inertiaKgM2.X : 0,
        (axes & PitchBit) != 0 ? Math.Abs(torqueNm.Y) / inertiaKgM2.Y : 0,
        (axes & YawBit) != 0 ? Math.Abs(torqueNm.Z) / inertiaKgM2.Z : 0);

    private static double Component(Vec3d v, int axis) => axis switch
    {
        0 => v.X,
        1 => v.Y,
        _ => v.Z,
    };

    private static bool Contains(IReadOnlyList<string>? ids, string id)
    {
        if (ids is null) return false;
        for (var i = 0; i < ids.Count; i++)
        {
            if (string.Equals(ids[i], id, StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
