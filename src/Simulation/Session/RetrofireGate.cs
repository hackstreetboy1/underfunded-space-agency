using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Session;

/// <summary>
/// Handles <c>flight.retrofire</c> ahead of Flight's own handler (longer prefix) so the capsule turns to retro attitude before
/// the sequence starts when it has attitude control (<see cref="BurnController.RequestRetrofire"/>).
/// </summary>
internal sealed class RetrofireGate(BurnController burns) : IEffectHandler
{
    public string Prefix => FlightEffectHandler.Retrofire;

    public bool CanApply(string target, EffectOperation operation) => target == FlightEffectHandler.Retrofire && operation == EffectOperation.Set;

    public void Apply(string target, EffectOperation operation, StateValue value, MissionTime now)
    {
        if (!CanApply(target, operation)) throw new UnknownStatePathException(target);
        if (value.AsBool()) burns.RequestRetrofire(now);
    }
}
