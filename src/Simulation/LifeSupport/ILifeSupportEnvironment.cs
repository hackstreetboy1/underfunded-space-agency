using Usa.Simulation.Kernel;

namespace Usa.Simulation.LifeSupport;

/// <summary>
/// The integration adapter between life support and the other areas (vehicle, electrical, attitude, crew).
/// Life support never reads their types; it only sees what the adapter writes into the frame.
/// </summary>
public interface ILifeSupportEnvironment
{
    /// <summary>
    /// Writes this step's inputs: load heat, heater power, radiator effectiveness, sunlit fraction, fan airflow,
    /// available O2, regulator state, crew activity/location/alive, fuel-cell water. Frame values persist between steps.
    /// </summary>
    void ReadInputs(MissionTime now, LifeSupportFrame frame);

    /// <summary>
    /// Takes this step's outputs back to their owners, e.g. draws <see cref="Atmosphere.CabinStepResult.O2DrawnKg"/>
    /// from the vehicle O2 tank.
    /// </summary>
    void ApplyOutputs(MissionTime now, LifeSupportFrame frame);
}
