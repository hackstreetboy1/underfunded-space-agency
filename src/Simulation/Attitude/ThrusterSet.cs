using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Attitude;

/// <summary>
/// The thrusters of one vehicle with precomputed full-thrust torque (r × F), force and mass flow in the
/// frame of the body the vehicle belongs to (see <see cref="Mount"/>). Inputs per step: the controller's
/// valve command, plus availability, forced fractions and tank status written by the caller.
/// </summary>
/// <remarks>
/// Effective valve fraction: zero when the thruster's tank is empty; otherwise the forced fraction
/// (a shorted driver keeps firing whatever the controller wants), raised to the command when the
/// thruster is commandable (powered, armed, not isolated).
/// </remarks>
public sealed class ThrusterSet : IHashContributor
{
    private readonly ThrusterSpec[] _specs;
    private readonly Vec3d[] _directionUnit;
    private readonly string[] _tankIds;
    private readonly int[] _tankIndex;
    private readonly double[] _massFlowKgPerS;
    private readonly Vec3d[] _torqueNm;
    private readonly Vec3d[] _forceN;
    private readonly bool[] _commandable;
    private readonly double[] _forcedFraction;
    private readonly bool[] _tankHasPropellant;
    private readonly double[] _effectiveFraction;
    private readonly double[] _propellantUsedKg;
    private readonly double[] _propellantUsedTotalKg;
    private readonly Dictionary<string, int> _indexById = new(StringComparer.Ordinal);

    public ThrusterSet(IReadOnlyList<ThrusterSpec> specs)
    {
        ArgumentNullException.ThrowIfNull(specs);
        var count = specs.Count;
        _specs = new ThrusterSpec[count];
        _directionUnit = new Vec3d[count];
        _tankIndex = new int[count];
        _massFlowKgPerS = new double[count];
        _torqueNm = new Vec3d[count];
        _forceN = new Vec3d[count];
        _commandable = new bool[count];
        _forcedFraction = new double[count];
        _effectiveFraction = new double[count];

        var tanks = new SortedSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            var spec = specs[i];
            var problem = FindProblem(spec);
            if (problem is not null) throw new ArgumentException($"Thruster '{spec?.Id}': {problem}", nameof(specs));
            if (!_indexById.TryAdd(spec!.Id, i)) throw new ArgumentException($"Thruster '{spec.Id}' is listed twice.", nameof(specs));
            _specs[i] = spec;
            _directionUnit[i] = spec.DirectionUnit.Normalized();
            _massFlowKgPerS[i] = spec.ThrustN / (spec.IspS * PhysicalConstants.StandardGravity);
            _commandable[i] = true;
            tanks.Add(spec.TankId);
        }

        _tankIds = [.. tanks];
        for (var i = 0; i < count; i++) _tankIndex[i] = Array.BinarySearch(_tankIds, _specs[i].TankId, StringComparer.Ordinal);
        _tankHasPropellant = new bool[_tankIds.Length];
        Array.Fill(_tankHasPropellant, true);
        _propellantUsedKg = new double[_tankIds.Length];
        _propellantUsedTotalKg = new double[_tankIds.Length];
        SetMount(StackMount.Identity);
    }

    public int Count => _specs.Length;

    public IReadOnlyList<ThrusterSpec> Specs => _specs;

    /// <summary>Distinct tank ids, sorted ordinally; propellant outputs use the same indices.</summary>
    public IReadOnlyList<string> TankIds => _tankIds;

    public StackMount Mount { get; private set; }

    /// <summary>Full-thrust torque per thruster about the body centre of mass, body frame.</summary>
    public ReadOnlySpan<Vec3d> TorqueNm => _torqueNm;

    /// <summary>Full-thrust force per thruster, body frame.</summary>
    public ReadOnlySpan<Vec3d> ForceN => _forceN;

    public ReadOnlySpan<double> MassFlowKgPerS => _massFlowKgPerS;

    /// <summary>Valve fraction each thruster actually fired at in the last <see cref="Fire"/>.</summary>
    public ReadOnlySpan<double> EffectiveFraction => _effectiveFraction;

    /// <summary>Propellant drawn in the last <see cref="Fire"/>, per tank index.</summary>
    public ReadOnlySpan<double> PropellantUsedKg => _propellantUsedKg;

    /// <summary>Propellant drawn since construction, per tank index.</summary>
    public ReadOnlySpan<double> PropellantUsedTotalKg => _propellantUsedTotalKg;

    public Vec3d NetTorqueNm { get; private set; }

    public Vec3d NetForceN { get; private set; }

    /// <returns>The thruster index, or -1.</returns>
    public int IndexOf(string thrusterId) => _indexById.TryGetValue(thrusterId, out var index) ? index : -1;

    /// <returns>The tank index, or -1.</returns>
    public int IndexOfTank(string tankId) => Array.BinarySearch(_tankIds, tankId, StringComparer.Ordinal) is var index and >= 0 ? index : -1;

    public int GetTankIndex(int thrusterIndex) => _tankIndex[thrusterIndex];

    public bool IsCommandable(int thrusterIndex) => _commandable[thrusterIndex];

    /// <summary>Whether the controller can open this valve: powered, armed and not isolated.</summary>
    public void SetCommandable(int thrusterIndex, bool commandable) => _commandable[thrusterIndex] = commandable;

    public double GetForcedFraction(int thrusterIndex) => _forcedFraction[thrusterIndex];

    /// <summary>Valve fraction held open regardless of commands (e.g. a shorted driver), 0..1.</summary>
    public void SetForcedFraction(int thrusterIndex, double fraction)
    {
        if (!double.IsFinite(fraction)) throw new ArgumentOutOfRangeException(nameof(fraction), fraction, "Forced fraction must be finite.");
        _forcedFraction[thrusterIndex] = Math.Clamp(fraction, 0.0, 1.0);
    }

    public bool TankHasPropellant(int tankIndex) => _tankHasPropellant[tankIndex];

    public void SetTankHasPropellant(int tankIndex, bool hasPropellant) => _tankHasPropellant[tankIndex] = hasPropellant;

    /// <summary>True when a command to this thruster would produce thrust.</summary>
    public bool CanFireOnCommand(int thrusterIndex) =>
        _commandable[thrusterIndex] && _tankHasPropellant[_tankIndex[thrusterIndex]];

    /// <summary>Moves the thrusters into a body frame and recomputes their torque about its centre of mass.</summary>
    public void SetMount(StackMount mount)
    {
        Mount = mount;
        for (var i = 0; i < _specs.Length; i++)
        {
            var force = mount.ToStack(_directionUnit[i]) * _specs[i].ThrustN;
            _forceN[i] = force;
            _torqueNm[i] = mount.PointToStack(_specs[i].PositionM).Cross(force);
        }
    }

    /// <summary>Fires every thruster for <paramref name="dtS"/> and updates the net outputs.</summary>
    /// <param name="commandFraction">Controller valve command per thruster index, 0..1.</param>
    public void Fire(ReadOnlySpan<double> commandFraction, double dtS)
    {
        if (commandFraction.Length != _specs.Length)
            throw new ArgumentException($"Expected {_specs.Length} commands but got {commandFraction.Length}.", nameof(commandFraction));

        Array.Clear(_propellantUsedKg);
        var torque = Vec3d.Zero;
        var force = Vec3d.Zero;
        for (var i = 0; i < _specs.Length; i++)
        {
            var fraction = 0.0;
            var tank = _tankIndex[i];
            if (_tankHasPropellant[tank])
            {
                fraction = _forcedFraction[i];
                if (_commandable[i])
                {
                    var command = Math.Clamp(commandFraction[i], 0.0, 1.0);
                    if (command > fraction) fraction = command;
                }
            }

            _effectiveFraction[i] = fraction;
            if (fraction <= 0) continue;

            torque += _torqueNm[i] * fraction;
            force += _forceN[i] * fraction;
            var usedKg = _massFlowKgPerS[i] * fraction * dtS;
            _propellantUsedKg[tank] += usedKg;
            _propellantUsedTotalKg[tank] += usedKg;
        }

        NetTorqueNm = torque;
        NetForceN = force;
    }

    public void WriteHash(StateHashWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        for (var i = 0; i < _specs.Length; i++)
        {
            writer.Write(_commandable[i]);
            writer.Write(_forcedFraction[i]);
            writer.Write(_effectiveFraction[i]);
        }
        for (var t = 0; t < _tankIds.Length; t++)
        {
            writer.Write(_tankHasPropellant[t]);
            writer.Write(_propellantUsedTotalKg[t]);
        }
    }

    /// <summary>Describes what is wrong with a spec, or null when it is usable.</summary>
    internal static string? FindProblem(ThrusterSpec? spec)
    {
        if (spec is null) return "is null.";
        if (string.IsNullOrEmpty(spec.Id)) return "has an empty id.";
        if (string.IsNullOrEmpty(spec.TankId)) return "has no tank id.";
        if (!(double.IsFinite(spec.ThrustN) && spec.ThrustN > 0)) return "thrustN must be positive.";
        if (!(double.IsFinite(spec.IspS) && spec.IspS > 0)) return "ispS must be positive.";
        if (!IsFinite(spec.PositionM)) return "positionM is not finite.";
        if (!IsFinite(spec.DirectionUnit) || spec.DirectionUnit.LengthSquared < 1e-12) return "directionUnit must be a non-zero vector.";
        return null;
    }

    private static bool IsFinite(Vec3d v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);
}
