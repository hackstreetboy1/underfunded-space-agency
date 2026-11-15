using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Commands;

/// <summary>Applies ground-commanded state changes. <see cref="EffectRouterRemoteControl"/> routes them through <see cref="EffectRouter"/>.</summary>
public interface IRemoteControlExecutor
{
    /// <summary>Whether the target exists and accepts the operation (physical possibility).</summary>
    bool CanApply(EffectDefinition effect);

    void Apply(EffectDefinition effect, MissionTime now);

    /// <summary>Applies every effect or none (see <see cref="EffectRouter.ApplyAll"/>).</summary>
    /// <returns>False when nothing was applied.</returns>
    bool ApplyAll(IReadOnlyList<EffectDefinition> effects, MissionTime now)
    {
        ArgumentNullException.ThrowIfNull(effects);
        foreach (var effect in effects) Apply(effect, now);
        return true;
    }
}
