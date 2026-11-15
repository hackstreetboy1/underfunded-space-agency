using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Commands;

/// <summary>Remote controls applied through the shared internal mutation path.</summary>
public sealed class EffectRouterRemoteControl(EffectRouter router) : IRemoteControlExecutor
{
    public bool CanApply(EffectDefinition effect) => router.CanApply(effect);

    public void Apply(EffectDefinition effect, MissionTime now) => router.Apply(effect, now);

    public bool ApplyAll(IReadOnlyList<EffectDefinition> effects, MissionTime now) => router.ApplyAll(effects, now).IsApplied;
}
