using Godot;

namespace Usa.Game.Presentation.Spacecraft;

/// <summary>
/// External-view camera (UI 4.4, GDD 4.4): drag to orbit, wheel to zoom between limits, smooth focus and reset.
/// The camera is the player's: it moves only for their input and for an explicit <see cref="Focus(Vector3, float?)"/>,
/// <see cref="Focus(Node3D, float?)"/> or <see cref="ResetView"/> call. It lives in the display frame and never
/// turns with a rolling vehicle. A focused vehicle is followed in translation only, so it stays in frame while it
/// drifts; focusing a component pins the point where the component was when focus was asked for.
/// </summary>
public sealed partial class OrbitCameraRig : Node3D
{
    private float _yawDeg;
    private float _pitchDeg;
    private float _distanceM;
    private float _targetYawDeg;
    private float _targetPitchDeg;
    private float _targetDistanceM;
    private Vector3 _pivot;
    private Vector3 _targetPivot;
    private Node3D? _tracked;
    private bool _dragging;
    private float _minPitchLimitDeg;
    private float _maxPitchLimitDeg;
    private Vector2 _pressPosition;
    private float _bumpM;
    private double _bumpSec = -1;
    private float? _glideRate;

    [Export] public Camera3D? Camera { get; set; }

    [Export] public float MinDistanceM { get; set; } = 2.5f;
    [Export] public float MaxDistanceM { get; set; } = 45f;
    [Export] public float DefaultDistanceM { get; set; } = 9f;
    [Export] public float DefaultYawDeg { get; set; } = 35f;

    /// <summary>Negative looks down; the default keeps Earth's limb a little below the vehicle.</summary>
    [Export] public float DefaultPitchDeg { get; set; } = -14f;

    [Export] public float MinPitchDeg { get; set; } = -85f;
    [Export] public float MaxPitchDeg { get; set; } = 85f;
    [Export] public float DragDegPerPixel { get; set; } = 0.3f;

    /// <summary>Distance factor per wheel notch.</summary>
    [Export] public float ZoomStep { get; set; } = 1.12f;

    /// <summary>Pixels the pointer may move before a press becomes a drag (a shorter press is a pick).</summary>
    [Export] public float DragThresholdPx { get; set; } = 4f;

    /// <summary>
    /// Viewport pixels per UI unit (the External view renders at the screen's pixel density), so drag speed and the drag
    /// threshold feel the same at 1080p, 1440p and 4K.
    /// </summary>
    public float InputPixelScale { get; set; } = 1f;

    /// <summary>Exponential smoothing rate (1/s) for input and focus moves.</summary>
    [Export] public float Responsiveness { get; set; } = 10f;

    /// <summary>Length of a <see cref="Bump"/> (a docking latch's jolt), seconds.</summary>
    [Export] public double BumpSec { get; set; } = 0.45;

    /// <summary>Bumped by every drag or zoom of the player's, so a scripted move can tell whether the player took over.</summary>
    public int InputRevision { get; private set; }

    /// <summary>The node the pivot follows, or null when it is pinned to a point.</summary>
    public Node3D? Tracked => _tracked;

    /// <summary>The distance the camera is settling to (zoom and focus targets), metres.</summary>
    public float TargetDistanceM => _targetDistanceM;

    public override void _Ready()
    {
        _targetYawDeg = _yawDeg = DefaultYawDeg;
        _targetPitchDeg = _pitchDeg = DefaultPitchDeg;
        _targetDistanceM = _distanceM = DefaultDistanceM;
        ResetPitchLimits();
        if (Camera is not null) Camera.Current = true;
        Apply();
    }

    /// <summary>Moves the pivot to a fixed point; keeps the viewing angle. Distance is clamped to the zoom limits.</summary>
    public void Focus(Vector3 worldPoint, float? distanceM = null)
    {
        _glideRate = null;
        _tracked = null;
        _targetPivot = worldPoint;
        if (distanceM is { } distance) _targetDistanceM = ClampDistance(distance);
    }

    /// <summary>Moves the pivot to <paramref name="target"/> and follows its position (not its rotation) from then on.</summary>
    public void Focus(Node3D target, float? distanceM = null)
    {
        _glideRate = null;
        ArgumentNullException.ThrowIfNull(target);
        _tracked = target;
        _targetPivot = target.GlobalPosition;
        if (distanceM is { } distance) _targetDistanceM = ClampDistance(distance);
    }

    /// <summary>Back to the default angle and distance around <paramref name="home"/> (or the origin).</summary>
    public void ResetView(Node3D? home = null)
    {
        _glideRate = null;
        _tracked = home;
        _targetPivot = home?.GlobalPosition ?? Vector3.Zero;
        _targetYawDeg = DefaultYawDeg;
        _targetPitchDeg = ClampPitch(DefaultPitchDeg);
        _targetDistanceM = ClampDistance(DefaultDistanceM);
        // Take the short way round instead of unwinding every turn the player dragged.
        _yawDeg = _targetYawDeg + Mathf.Wrap(_yawDeg - _targetYawDeg, -180f, 180f);
    }

    /// <summary>Turns to an explicit viewing angle (review captures, scripted shots); keeps the pivot.</summary>
    public void SetAngles(float yawDeg, float pitchDeg, float? distanceM = null)
    {
        _glideRate = null;
        _targetYawDeg = yawDeg;
        _targetPitchDeg = ClampPitch(pitchDeg);
        if (distanceM is { } distance) _targetDistanceM = ClampDistance(distance);
    }

    /// <summary>
    /// Narrows the pitch range (at sea the camera stays above the water: <c>maxPitchDeg</c> below 0 keeps it looking
    /// down). The current angle is clamped into it; <see cref="ResetPitchLimits"/> restores the exported limits.
    /// </summary>
    public void LimitPitch(float minPitchDeg, float maxPitchDeg)
    {
        _minPitchLimitDeg = Math.Max(minPitchDeg, MinPitchDeg);
        _maxPitchLimitDeg = Math.Min(maxPitchDeg, MaxPitchDeg);
        _targetPitchDeg = ClampPitch(_targetPitchDeg);
    }

    /// <summary>Where the camera is settling to: what <see cref="Glide"/> can return to later.</summary>
    public CameraShot Capture() => new(_tracked, _targetPivot, _targetYawDeg, _targetPitchDeg, _targetDistanceM);

    /// <summary>
    /// Eases to <paramref name="shot"/> at <paramref name="rate"/> (1/s, slower than input moves) instead of the usual
    /// responsiveness, turning the short way round. The distance may exceed <see cref="MaxDistanceM"/> (a docking pair needs
    /// the room); the player's next drag or zoom ends the glide and the zoom limits apply again.
    /// </summary>
    public void Glide(CameraShot shot, float rate)
    {
        _tracked = shot.Tracked is { } node && IsInstanceValid(node) ? node : null;
        _targetPivot = _tracked?.GlobalPosition ?? shot.Pivot;
        _targetYawDeg = _yawDeg + Mathf.Wrap(shot.YawDeg - _yawDeg, -180f, 180f);
        _targetPitchDeg = ClampPitch(shot.PitchDeg);
        _targetDistanceM = Math.Max(shot.DistanceM, MinDistanceM);
        _glideRate = rate;
    }

    /// <summary>
    /// A short, decaying shake of the camera along its view axis (a docking latch); never moves the pivot or the angle.
    /// The caller leaves it out under reduced motion.
    /// </summary>
    public void Bump(float amplitudeM)
    {
        _bumpM = amplitudeM;
        _bumpSec = 0;
    }

    public void ResetPitchLimits()
    {
        _minPitchLimitDeg = MinPitchDeg;
        _maxPitchLimitDeg = MaxPitchDeg;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _targetDistanceM = ClampDistance(_targetDistanceM / ZoomStep);
                TookOver();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _targetDistanceM = ClampDistance(_targetDistanceM * ZoomStep);
                TookOver();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Right } button:
                if (button.Pressed) _pressPosition = button.Position;
                else _dragging = false;
                break;
            case InputEventMouseMotion motion when (motion.ButtonMask & (MouseButtonMask.Left | MouseButtonMask.Right)) != 0:
                var pixelScale = InputPixelScale > 0 ? InputPixelScale : 1f;
                if (!_dragging && motion.Position.DistanceTo(_pressPosition) < DragThresholdPx * pixelScale) break;
                _dragging = true;
                _targetYawDeg -= motion.Relative.X / pixelScale * DragDegPerPixel;
                _targetPitchDeg = ClampPitch(_targetPitchDeg - motion.Relative.Y / pixelScale * DragDegPerPixel);
                TookOver();
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (_tracked is not null)
        {
            if (IsInstanceValid(_tracked)) _targetPivot = _tracked.GlobalPosition;
            else _tracked = null;
        }

        var blend = 1f - Mathf.Exp(-(_glideRate ?? Responsiveness) * (float)delta);
        _yawDeg = Mathf.Lerp(_yawDeg, _targetYawDeg, blend);
        _pitchDeg = Mathf.Lerp(_pitchDeg, _targetPitchDeg, blend);
        _distanceM = Mathf.Lerp(_distanceM, _targetDistanceM, blend);
        _pivot = _pivot.Lerp(_targetPivot, blend);
        Apply();
        if (_bumpSec < 0 || Camera is null) return;
        _bumpSec += delta;
        var t = (float)(_bumpSec / Math.Max(BumpSec, 1e-3));
        if (t >= 1f)
        {
            _bumpSec = -1;
            return;
        }
        var shake = _bumpM * (1f - t) * (1f - t) * Mathf.Sin(t * Mathf.Tau * 2.5f);
        Camera.Position = new Vector3(0f, shake * 0.35f, _distanceM + shake);
    }

    private void TookOver()
    {
        InputRevision++;
        _glideRate = null;
    }

    private float ClampDistance(float distanceM) => Mathf.Clamp(distanceM, MinDistanceM, MaxDistanceM);

    private float ClampPitch(float pitchDeg) => Mathf.Clamp(pitchDeg, _minPitchLimitDeg, _maxPitchLimitDeg);

    private void Apply()
    {
        GlobalTransform = new Transform3D(
            Basis.FromEuler(new Vector3(Mathf.DegToRad(_pitchDeg), Mathf.DegToRad(_yawDeg), 0f)),
            _pivot);
        if (Camera is not null) Camera.Position = new Vector3(0f, 0f, _distanceM);
    }
}
