using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Session;

/// <summary>One vehicle's attitude for presentation (<see cref="MissionSession.TryGetAttitude"/>): body-to-inertial orientation and body rates.</summary>
public readonly record struct AttitudeReading(Quatd Orientation, double RollDps, double PitchDps, double YawDps, double TotalDps, string? DockedWithId);
