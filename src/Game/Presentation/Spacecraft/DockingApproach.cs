using Godot;

namespace Usa.Game.Presentation.Spacecraft;

/// <summary>
/// Flies the crewed vehicle (the chaser) in to the target while the crew's docking task runs
/// (<see cref="ISpacecraftViewSource.DockingApproach01"/>), so the view shows an active approach instead of a blend at the end.
/// The target is held at the pose it will have once mated; the chaser starts <see cref="StandoffM"/> back along the docking
/// axis with a small lateral and attitude error and closes on the profile of <see cref="DockingApproachProfile"/>. At
/// progress 1 the chaser sits exactly in the mated pose, so the hand-over to the docked stack does not move anything.
/// <para>
/// The chaser's jets show the approach it flies: aft-pointing translation jets pulse through the accelerating burn,
/// forward-pointing ones through the braking burn, and the cross jets give the lateral and attitude corrections. These are
/// commanded pulses (the plume rule's commanded exception, <see cref="ThrusterVfxDriver.SetCommanded"/>), chosen from the
/// sockets' geometry on the chaser's aft section; they never feed the stuck look.
/// </para>
/// </summary>
public sealed class DockingApproach
{
    /// <summary>Display metres between the docking ports at the start of the approach.</summary>
    public const float StandoffM = 18f;

    /// <summary>Lateral error at the line-up along the two cross axes, display metres.</summary>
    public const float LateralError1M = 0.9f;

    public const float LateralError2M = -0.6f;

    /// <summary>Attitude error at the line-up (roll about the docking axis, then about the two cross axes), degrees.</summary>
    public static readonly Vector3 AttitudeErrorDeg = new(3f, 2.5f, -3.5f);

    /// <summary>Real seconds of the capture jolt after contact (the latches closing).</summary>
    public const double LatchSec = 0.8;

    /// <summary>How far the chaser's ring rides in on the latches before it settles, display metres.</summary>
    public const float LatchTravelM = 0.07f;

    private const float TranslationThrottle = 1f;
    private const float CorrectionThrottle = 0.75f;
    private const float AftOfPortM = 1.5f;

    private readonly VehicleRig _chaser;
    private readonly Transform3D _chaserFrom;
    private readonly Transform3D _targetFrom;
    private readonly bool _blendIn;
    private readonly Vector3 _axis;
    private readonly Vector3 _cross1;
    private readonly Vector3 _cross2;
    private readonly Vector3 _port;
    private readonly List<int> _forwardJets = [];
    private readonly List<int> _brakeJets = [];
    private readonly List<(int Index, float Along1, float Along2)> _crossJets = [];
    private double _lastProgress;

    /// <param name="chaser">The crewed vehicle; it moves and fires.</param>
    /// <param name="target">The passive vehicle; it is held at its mated pose.</param>
    /// <param name="progress">Progress when the approach is first shown.</param>
    /// <param name="blendIn">Ease both vehicles from where they are shown now onto the approach during the line-up.</param>
    public DockingApproach(VehicleRig chaser, VehicleRig target, double progress, bool blendIn)
    {
        ArgumentNullException.ThrowIfNull(chaser);
        ArgumentNullException.ThrowIfNull(target);
        _chaser = chaser;
        _chaserFrom = chaser.Transform;
        _targetFrom = target.Transform;
        _blendIn = blendIn;
        _lastProgress = progress;

        var port = chaser.DockingPortLocal;
        _port = port.Origin;
        _axis = port.Basis.X.Normalized();
        var up = Mathf.Abs(_axis.Dot(Vector3.Up)) < 0.9f ? Vector3.Up : Vector3.Back;
        _cross1 = (up - _axis * _axis.Dot(up)).Normalized();
        _cross2 = _axis.Cross(_cross1).Normalized();
        ClassifyJets();
    }

    /// <summary>The progress shown last (the latch only follows an approach that got close).</summary>
    public double Progress => _lastProgress;

    /// <summary>The chaser's docking axis (out of its port) in the frame of <paramref name="chaserPose"/>'s parent.</summary>
    public Vector3 AxisIn(Transform3D chaserPose) => (chaserPose.Basis * _axis).Normalized();

    /// <summary>Plume indices the approach fires (diagnostics): accelerating, braking and cross jets.</summary>
    public (IReadOnlyList<int> Forward, IReadOnlyList<int> Brake, int Cross) Jets => (_forwardJets, _brakeJets, _crossJets.Count);

    /// <summary>
    /// The poses to show at <paramref name="progress"/>: the chaser relative to <paramref name="chaserBase"/> (its attitude at
    /// the origin, where the docked stack will be) and the target at <paramref name="targetMated"/>, its mated pose there.
    /// </summary>
    public (Transform3D Chaser, Transform3D Target) Resolve(Transform3D chaserBase, Transform3D targetMated, double progress)
    {
        var p = Math.Clamp(progress, 0, 1);
        var distance = StandoffM * (float)DockingApproachProfile.RemainingShare(p);
        var (lateral1, lateral2) = DockingApproachProfile.LateralShare(p);
        var attitude = (float)DockingApproachProfile.AttitudeShare(p);
        var offset = -_axis * distance + _cross1 * (LateralError1M * (float)lateral1) + _cross2 * (LateralError2M * (float)lateral2);
        var error = AttitudeErrorDeg * attitude;
        var turn = new Basis(_axis, Mathf.DegToRad(error.X)) * new Basis(_cross1, Mathf.DegToRad(error.Y)) *
            new Basis(_cross2, Mathf.DegToRad(error.Z));
        // Turn about the docking port, so the attitude error never swings the port off the axis.
        var local = new Transform3D(Basis.Identity, offset + _port) * new Transform3D(turn, Vector3.Zero) *
            new Transform3D(Basis.Identity, -_port);
        var chaser = chaserBase * local;
        var target = targetMated;
        if (_blendIn && p < DockingApproachProfile.AlignEnd)
        {
            var w = (float)SmoothStep(p / DockingApproachProfile.AlignEnd);
            chaser = _chaserFrom.InterpolateWith(chaser, w);
            target = _targetFrom.InterpolateWith(target, w);
        }
        return (chaser, target);
    }

    /// <summary>
    /// Opens the jets the approach has commanded between the previous call's progress and <paramref name="progress"/>, so a
    /// pulse shorter than a frame under time compression still shows; a frame that did not move keeps its pulse lit.
    /// </summary>
    public void Fire(double progress)
    {
        var from = _lastProgress;
        _lastProgress = progress;
        if (_chaser.Thrusters is not { } thrusters) return;
        thrusters.ClearCommanded();

        if (DockingApproachProfile.AccelPulse(from, progress)) Open(thrusters, _forwardJets, TranslationThrottle);
        if (DockingApproachProfile.BrakePulse(from, progress)) Open(thrusters, _brakeJets, TranslationThrottle);

        // Thrust towards the axis starts a correction (the plume points away from it); thrust away stops it.
        var lateral1 = DockingApproachProfile.LateralPulse(1, from, progress);
        var lateral2 = DockingApproachProfile.LateralPulse(2, from, progress);
        var attitude = DockingApproachProfile.AttitudePulse(from, progress);
        foreach (var (index, along1, along2) in _crossJets)
        {
            var radial1 = Mathf.Abs(along1) > 0.9f;
            var radial2 = Mathf.Abs(along2) > 0.9f;
            var fires =
                (radial1 && lateral1 != 0 && Mathf.Sign(along1) == lateral1 * Mathf.Sign(LateralError1M)) ||
                (radial2 && lateral2 != 0 && Mathf.Sign(along2) == lateral2 * Mathf.Sign(LateralError2M)) ||
                (!radial1 && !radial2 && attitude != 0 && along1 * attitude > 0.5f && along2 * attitude > 0.5f);
            if (fires) thrusters.SetCommanded(index, CorrectionThrottle);
        }
    }

    /// <summary>Drops every commanded pulse (the approach ended).</summary>
    public void Release() => _chaser.Thrusters?.ClearCommanded();

    /// <summary>
    /// The chaser's travel along the docking axis <paramref name="sinceContactSec"/> after contact: in on the latches, a
    /// small rebound and settled by <see cref="LatchSec"/>.
    /// </summary>
    public static float LatchOffsetM(double sinceContactSec)
    {
        if (sinceContactSec < 0 || sinceContactSec >= LatchSec) return 0f;
        var t = (float)(sinceContactSec / LatchSec);
        return LatchTravelM * Mathf.Sin(t * Mathf.Tau * 1.25f) * (1f - t) * (1f - t);
    }

    private static void Open(ThrusterVfxDriver thrusters, List<int> jets, float throttle)
    {
        foreach (var index in jets) thrusters.SetCommanded(index, throttle);
    }

    // Translation and attitude jets of the chaser sit on its aft section, clear of the docking port; plumes point out
    // along the socket's +X. Only pulse thrusters take part (never the retro rockets).
    private void ClassifyJets()
    {
        if (_chaser.Thrusters is not { } thrusters) return;
        var portAlong = _port.Dot(_axis);
        for (var i = 0; i < thrusters.PlumeCount; i++)
        {
            if (thrusters.PlumeKindAt(i) != PlumeKind.AttitudeThruster) continue;
            var socket = thrusters.PlumeTransform(i, _chaser);
            if (socket.Origin.Dot(_axis) > portAlong - AftOfPortM) continue;
            var plume = socket.Basis.X.Normalized();
            var along = plume.Dot(_axis);
            if (along < -0.8f) _forwardJets.Add(i);
            else if (along > 0.8f) _brakeJets.Add(i);
            else if (Mathf.Abs(along) < 0.3f) _crossJets.Add((i, plume.Dot(_cross1), plume.Dot(_cross2)));
        }
    }

    private static double SmoothStep(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }
}
