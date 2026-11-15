using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Attitude;

/// <summary>
/// Accepts <c>attitude.&lt;vehicleId&gt;.mode</c> (set, text), <c>.control_set</c> (set, text) and
/// <c>.target_deg</c> (set or add, number). A crew <c>rate_damp [system]</c> task maps to setting
/// control_set to the system and mode to 'rate_damp'.
/// </summary>
public sealed class AttitudeEffectHandler(AttitudeSystem system) : IEffectHandler
{
    public string Prefix => StatePaths.Attitude;

    public bool CanApply(string target, EffectOperation operation)
    {
        if (!AttitudeFields.TrySplit(target, out var vehicleId, out var field) || !system.TryGetVehicle(vehicleId, out _))
            return false;
        return field switch
        {
            AttitudeFields.Mode or AttitudeFields.ControlSet => operation == EffectOperation.Set,
            AttitudeFields.TargetDeg => operation is EffectOperation.Set or EffectOperation.Add,
            _ => false,
        };
    }

    /// <exception cref="InvalidOperationException">The value doesn't fit the field (unknown mode or control set, non-finite angle).</exception>
    public void Apply(string target, EffectOperation operation, StateValue value, MissionTime now)
    {
        if (!CanApply(target, operation)) throw new UnknownStatePathException(target);
        AttitudeFields.TrySplit(target, out var vehicleIdSpan, out var field);
        system.TryGetVehicle(vehicleIdSpan, out var vehicle);

        switch (field)
        {
            case AttitudeFields.Mode:
                var text = value.AsText();
                if (!AttitudeModeText.TryParse(text, out var mode))
                    throw new InvalidOperationException($"'{text}' is not an attitude mode (effect on {target}).");
                system.SetMode(vehicle.Id, mode, now);
                break;
            case AttitudeFields.ControlSet:
                var controlSet = value.AsText();
                if (!vehicle.Controller.HasControlSet(controlSet))
                    throw new InvalidOperationException($"Vehicle '{vehicle.Id}' has no control set '{controlSet}' (effect on {target}).");
                system.SetControlSet(vehicle.Id, controlSet, now);
                break;
            default:
                var degrees = value.AsNumber();
                if (!double.IsFinite(degrees))
                    throw new InvalidOperationException($"Effect on {target} must be a finite number, not {degrees}.");
                system.SetHoldTarget(vehicle.Id, operation == EffectOperation.Add ? vehicle.HoldTargetDeg + degrees : degrees);
                break;
        }
    }
}
