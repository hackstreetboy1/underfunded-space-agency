using Godot;

namespace Usa.Game.Presentation.Spacecraft;

/// <summary>
/// Shows a separating vehicle backing straight out along the docking axis from where it was mated, then hands over to
/// the simulation's relative pose once it is clear.
/// <para>
/// The simulation separates the stack from one point (both vehicles start at the stack's position, the springs push
/// prograde), while the view mates the models port to port a few metres apart. Blending straight from the mated pose to
/// the simulated one would pull the target back through the primary's hull, so the view instead keeps the mated pose
/// and adds the distance the simulation has opened since separation along the docking axis. Past
/// <see cref="HoldM"/> the direction and attitude ease over to the simulated ones. Offsets are kept in inertial axes so
/// the display frame turning with the orbit does not drag them along.
/// </para>
/// </summary>
public sealed class UndockDeparture
{
    /// <summary>Display metres of separation kept on the docking axis: clear of both hulls (Lodestar ≈ 6 m, Tandem ≈ 4 m).</summary>
    public const float HoldM = 12f;

    /// <summary>Display metres over which the pose eases from the docking axis to the simulated one.</summary>
    public const float HandoverM = 24f;

    private readonly Vector3 _matedOriginInertial;
    private readonly Vector3 _simStartInertial;
    private readonly Vector3 _axisInertial;
    private readonly Quaternion _attitudeCorrectionInertial;

    /// <param name="displayFrame">Inertial → display rotation at separation.</param>
    /// <param name="mated">The target's mated pose in the display frame.</param>
    /// <param name="dockingAxis">Out of the primary's docking port, display frame.</param>
    /// <param name="simulated">The target's simulated pose in the display frame at separation.</param>
    public UndockDeparture(Basis displayFrame, Transform3D mated, Vector3 dockingAxis, Transform3D simulated)
    {
        var toInertial = displayFrame.Transposed();
        _matedOriginInertial = toInertial * mated.Origin;
        _simStartInertial = toInertial * simulated.Origin;
        _axisInertial = (toInertial * dockingAxis).Normalized();
        var correction = mated.Basis.Orthonormalized() * simulated.Basis.Orthonormalized().Inverse();
        _attitudeCorrectionInertial = (toInertial * correction * displayFrame).GetRotationQuaternion().Normalized();
    }

    /// <summary>True once the handover is complete; the caller can drop this and use the simulated pose.</summary>
    public bool Finished { get; private set; }

    /// <summary>The pose to show for the target's current simulated pose (display frame).</summary>
    public Transform3D Resolve(Basis displayFrame, Transform3D simulated)
    {
        var travelled = (displayFrame.Transposed() * simulated.Origin - _simStartInertial).Length();
        var onAxis = displayFrame * (_matedOriginInertial + _axisInertial * travelled);
        var w = Mathf.SmoothStep(0f, 1f, (travelled - HoldM) / HandoverM);
        if (w >= 1f)
        {
            Finished = true;
            return simulated;
        }

        var correction = displayFrame * new Basis(Quaternion.Identity.Slerp(_attitudeCorrectionInertial, 1f - w)) * displayFrame.Transposed();
        var basis = correction * simulated.Basis;
        if (w <= 0f || simulated.Origin.LengthSquared() < 1e-6f) return new Transform3D(basis, onAxis);

        // Swing the direction rather than lerping points, so the path never cuts back towards the primary.
        var direction = onAxis.Normalized().Slerp(simulated.Origin.Normalized(), w);
        var distance = Mathf.Lerp(onAxis.Length(), simulated.Origin.Length(), w);
        return new Transform3D(basis, direction * distance);
    }
}
