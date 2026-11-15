using Usa.Simulation.Kernel;

namespace Usa.Simulation.Attitude;

/// <summary>
/// Connects the attitude model to vehicle state. Implemented by the integration step: before each step
/// it writes thruster availability, forced (shorted-driver) fractions and tank status; after it, it takes
/// the propellant used and the firing fractions.
/// </summary>
public interface IAttitudeBridge
{
    /// <summary>
    /// Sets <see cref="ThrusterSet.SetCommandable"/>, <see cref="ThrusterSet.SetForcedFraction"/>,
    /// <see cref="ThrusterSet.SetTankHasPropellant"/> and optionally <see cref="AttitudeVehicle.ExternalTorqueNm"/>.
    /// </summary>
    void BeforeStep(AttitudeVehicle vehicle, in StepContext context);

    /// <summary>Reads <see cref="ThrusterSet.PropellantUsedKg"/> (indexed like <see cref="ThrusterSet.TankIds"/>).</summary>
    void AfterStep(AttitudeVehicle vehicle, in StepContext context);
}
