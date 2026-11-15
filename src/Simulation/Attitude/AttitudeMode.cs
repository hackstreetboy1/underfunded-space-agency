namespace Usa.Simulation.Attitude;

/// <summary>Attitude control law of one vehicle. Serialized in data and state as snake_case text (<see cref="AttitudeModeText"/>).</summary>
public enum AttitudeMode
{
    Off,

    /// <summary>Drive body rates to zero (OAMS auto-hold, RCS rate damping, Lodestar ACS).</summary>
    RateDamp,

    /// <summary>Hold the body X axis on a target in-plane pointing angle (burns, retrofire).</summary>
    AttitudeHold,
}
