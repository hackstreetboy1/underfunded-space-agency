using Godot;

namespace Usa.Game.Presentation.Spacecraft;

/// <summary>
/// The capsule's pose under a parachute. The simulation keeps the free-flight attitude through descent, so drawn as-is the
/// capsule could hang upside down with the canopy below it. The canopies trail from the nose (rig +X, where the
/// <c>Chutes</c> node sits), so under a chute the view swings the nose to straight up, keeps the heading the capsule had
/// when the chute came out, and adds a slow pendulum sway and drift.
/// </summary>
public sealed class ChuteHang
{
    private Basis _hang0;
    private double _blendSec;
    private double _swaySec;
    private bool _active;

    /// <summary>Seconds to swing from the free-flight attitude to hanging under the canopy.</summary>
    public double SwingSec { get; set; } = 2.5;

    /// <summary>Pendulum sway amplitude in degrees (0 under reduced motion).</summary>
    public float SwayDeg { get; set; } = 5f;

    /// <summary>Slow turn about the vertical while hanging, degrees per second.</summary>
    public float DriftDegPerSec { get; set; } = 4f;

    public bool IsActive => _active;

    public void Reset()
    {
        _active = false;
        _blendSec = 0;
        _swaySec = 0;
    }

    /// <summary>
    /// The display orientation for this frame. <paramref name="free"/> is the simulated attitude in the display frame
    /// (up = display +Y); <paramref name="snap"/> skips the swing (loading a save, first frame).
    /// </summary>
    public Basis Resolve(Basis free, bool chutesOut, double deltaSec, bool snap, bool reducedMotion)
    {
        if (!chutesOut)
        {
            Reset();
            return free;
        }

        if (!_active)
        {
            _active = true;
            _hang0 = NoseUp(free);
            _blendSec = snap ? SwingSec : 0;
            _swaySec = 0;
        }

        var step = Math.Max(0, deltaSec);
        _blendSec += step;
        _swaySec += step;

        var hang = _hang0;
        if (!reducedMotion)
        {
            var drift = new Basis(Vector3.Up, Mathf.DegToRad(DriftDegPerSec) * (float)_swaySec);
            var amplitude = Mathf.DegToRad(SwayDeg);
            var t = (float)_swaySec;
            var swayX = amplitude * Mathf.Sin(Mathf.Tau * t / 4.2f);
            var swayZ = amplitude * 0.6f * Mathf.Sin(Mathf.Tau * t / 5.3f + 1f);
            var sway = new Basis(Vector3.Right, swayX) * new Basis(Vector3.Back, swayZ);
            hang = sway * drift * _hang0;
        }

        var weight = SwingSec <= 0 ? 1f : (float)Math.Clamp(_blendSec / SwingSec, 0, 1);
        if (weight >= 1f) return hang.Orthonormalized();
        weight = weight * weight * (3f - 2f * weight);
        var from = free.Orthonormalized().GetRotationQuaternion();
        var to = hang.Orthonormalized().GetRotationQuaternion();
        return new Basis(from.Slerp(to, weight));
    }

    /// <summary>
    /// <paramref name="free"/> turned by the smallest rotation that points its +X (the nose, where the risers attach)
    /// straight up. When the nose points straight down that rotation is a half turn about the capsule's own pitch axis.
    /// </summary>
    public static Basis NoseUp(Basis free)
    {
        var orthonormal = free.Orthonormalized();
        var nose = orthonormal.X.Normalized();
        var dot = Math.Clamp(nose.Dot(Vector3.Up), -1f, 1f);
        if (dot > 0.9999f) return orthonormal;

        Vector3 axis;
        float angle;
        if (dot < -0.9999f)
        {
            axis = orthonormal.Z.Normalized();
            if (Math.Abs(axis.Dot(Vector3.Up)) > 0.9f) axis = Vector3.Right;
            angle = Mathf.Pi;
        }
        else
        {
            axis = nose.Cross(Vector3.Up).Normalized();
            angle = Mathf.Acos(dot);
        }
        return (new Basis(axis, angle) * orthonormal).Orthonormalized();
    }
}
