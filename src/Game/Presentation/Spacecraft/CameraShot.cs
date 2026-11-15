using Godot;

namespace Usa.Game.Presentation.Spacecraft;

/// <summary>A camera framing of <see cref="OrbitCameraRig"/>: the followed node (or a fixed pivot), angles and distance.</summary>
public readonly record struct CameraShot(Node3D? Tracked, Vector3 Pivot, float YawDeg, float PitchDeg, float DistanceM);
