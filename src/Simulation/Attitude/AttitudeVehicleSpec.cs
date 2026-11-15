namespace Usa.Simulation.Attitude;

/// <summary>Everything the attitude model needs from one vehicle definition.</summary>
public sealed record AttitudeVehicleSpec(string Id, MassProperties MassProperties, IReadOnlyList<ThrusterSpec> Thrusters);
