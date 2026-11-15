using Godot;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Game.Presentation.Spacecraft;

/// <summary>
/// The capsule's pose through atmospheric entry. The simulation has no aerodynamics: after retrofire the capsule keeps the
/// inertial attitude it was released in (or whatever it tumbles to), which drifts off the velocity vector as the capsule
/// goes round the Earth and the flight path steepens. A real blunt capsule weathervanes heat shield first, trimmed a
/// little off the airflow by its offset centre of mass and rolled to steer its lift. So from retro-pack jettison (or the
/// first heating) the view swings the drawn capsule to that pose and holds it until the chutes take over
/// (<see cref="ChuteHang"/> receives this pose as its free attitude, so the handover is continuous).
/// <para>
/// The direction of flight comes only from the vehicle's visible motion: successive samples of the nadir and altitude the
/// backdrop already shows. Their ratio gives the flight path angle (the sample interval cancels), the nadir's turn the
/// direction along the orbit.
/// </para>
/// </summary>
public sealed class EntryAttitude
{
    private const double EarthRadiusM = 6_371_000;

    private double _lastAngleRad;
    private double _lastAltitudeM;
    private bool _hasSample;
    private double _pathAngleRad;
    private double _measuredPathAngleRad;
    private float _direction = 1f;
    private double _blendSec;
    private double _weaveSec;
    private bool _active;

    /// <summary>Angle between the heat shield's axis and the airflow (Gemini flew trimmed roughly 10–15° off).</summary>
    public float TrimDeg { get; set; } = 12f;

    /// <summary>Peak bank either side of lift-up while the capsule rolls to steer (0 under reduced motion).</summary>
    public float BankWeaveDeg { get; set; } = 25f;

    /// <summary>Real seconds per full roll-steering cycle.</summary>
    public float BankWeavePeriodSec { get; set; } = 26f;

    /// <summary>Seconds to swing from the free-flight attitude to the entry pose.</summary>
    public double SwingSec { get; set; } = 3.0;

    /// <summary>Time constant of the flight-path-angle smoothing, real seconds (the flight track moves in steps).</summary>
    public double PathSmoothingSec { get; set; } = 0.5;

    public bool IsActive => _active;

    /// <summary>The smoothed direction of flight in the display frame (up +Y, prograde −Z).</summary>
    public Vector3 VelocityDisplay => FlightDirection(_pathAngleRad, _direction);

    public void Reset()
    {
        _active = false;
        _blendSec = 0;
        _weaveSec = 0;
    }

    /// <summary>Forgets the motion history too (a new source, a loaded save).</summary>
    public void ResetMotion()
    {
        Reset();
        _hasSample = false;
        _pathAngleRad = 0;
        _measuredPathAngleRad = 0;
        _direction = 1f;
    }

    /// <summary>
    /// Feeds the backdrop's nadir (inertial, orbital plane X-Y) and altitude every frame. A sample that has not moved since
    /// the last one (the flight track steps slower than frames, or the app is frozen) changes nothing.
    /// </summary>
    public void Observe(Vec3d nadirInertial, double altitudeKm, double deltaSec)
    {
        if (nadirInertial.X * nadirInertial.X + nadirInertial.Y * nadirInertial.Y < 1e-12) return;
        var angleRad = Math.Atan2(-nadirInertial.Y, -nadirInertial.X);
        var altitudeM = altitudeKm * 1000.0;
        if (_hasSample)
        {
            var turnRad = Math.IEEERemainder(angleRad - _lastAngleRad, Math.Tau);
            var climbM = altitudeM - _lastAltitudeM;
            if (Math.Abs(turnRad) > 1e-12 || Math.Abs(climbM) > 1e-6)
            {
                var alongM = Math.Abs(turnRad) * (EarthRadiusM + Math.Max(altitudeM, 0));
                _measuredPathAngleRad = Math.Atan2(climbM, alongM);
                if (Math.Abs(turnRad) > 1e-12) _direction = turnRad > 0 ? 1f : -1f;
            }
        }
        _lastAngleRad = angleRad;
        _lastAltitudeM = altitudeM;
        if (!_hasSample)
        {
            _hasSample = true;
            _pathAngleRad = _measuredPathAngleRad;
            return;
        }
        var follow = PathSmoothingSec <= 0 ? 1 : 1 - Math.Exp(-Math.Max(deltaSec, 0) / PathSmoothingSec);
        _pathAngleRad += (_measuredPathAngleRad - _pathAngleRad) * follow;
    }

    /// <summary>
    /// The display orientation for this frame. <paramref name="free"/> is the simulated attitude in the display frame;
    /// <paramref name="entering"/> is true from retro-pack jettison or first heating until splashdown (keep it true while the
    /// chutes are out, so <see cref="ChuteHang"/> swings from this pose); <paramref name="snap"/> skips the swing.
    /// </summary>
    public Basis Resolve(Basis free, bool entering, double deltaSec, bool snap, bool reducedMotion)
    {
        if (!entering)
        {
            Reset();
            return free;
        }
        if (!_active)
        {
            _active = true;
            _blendSec = snap ? SwingSec : 0;
            _weaveSec = 0;
        }
        var step = Math.Max(0, deltaSec);
        _blendSec += step;
        _weaveSec += step;

        var bankRad = reducedMotion || BankWeavePeriodSec <= 0
            ? 0f
            : Mathf.DegToRad(BankWeaveDeg) * Mathf.Sin(Mathf.Tau * (float)(_weaveSec / BankWeavePeriodSec));
        var entry = ShieldFirst(VelocityDisplay, Vector3.Up, Mathf.DegToRad(TrimDeg), bankRad);

        var weight = SwingSec <= 0 ? 1f : (float)Math.Clamp(_blendSec / SwingSec, 0, 1);
        if (weight >= 1f) return entry;
        weight = weight * weight * (3f - 2f * weight);
        var from = free.Orthonormalized().GetRotationQuaternion();
        return new Basis(from.Slerp(entry.GetRotationQuaternion(), weight));
    }

    /// <summary>Unit direction of flight in the display frame for a flight path angle (climb positive) and orbit sense.</summary>
    public static Vector3 FlightDirection(double pathAngleRad, float direction) =>
        new Vector3(0f, (float)Math.Sin(pathAngleRad), -direction * (float)Math.Cos(pathAngleRad)).Normalized();

    /// <summary>
    /// The capsule pose with its heat shield (body −X, model −X) into <paramref name="velocity"/>, tilted
    /// <paramref name="trimRad"/> so the shield's force has a lift component, and the lift (and the model top, body +Z →
    /// model +Y) turned <paramref name="bankRad"/> about the velocity from straight up. Columns are the model axes.
    /// </summary>
    public static Basis ShieldFirst(Vector3 velocity, Vector3 up, float trimRad, float bankRad)
    {
        var v = velocity.LengthSquared() > 1e-12f ? velocity.Normalized() : Vector3.Forward;
        var liftUp = up - up.Dot(v) * v;
        if (liftUp.LengthSquared() < 1e-6f)
        {
            // Falling straight down: any horizontal reference keeps the pose defined.
            liftUp = Vector3.Forward - Vector3.Forward.Dot(v) * v;
            if (liftUp.LengthSquared() < 1e-6f) liftUp = Vector3.Right - Vector3.Right.Dot(v) * v;
        }
        var lift = liftUp.Normalized().Rotated(v, bankRad);
        // The shield's normal force pushes along the nose; tilting the shield away from the lift side gives lift towards it.
        var shield = (Mathf.Cos(trimRad) * v - Mathf.Sin(trimRad) * lift).Normalized();
        var nose = -shield;
        var top = (lift - lift.Dot(nose) * nose).Normalized();
        return new Basis(nose, top, nose.Cross(top)).Orthonormalized();
    }
}
