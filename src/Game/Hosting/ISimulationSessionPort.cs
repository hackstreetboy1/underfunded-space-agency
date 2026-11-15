using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;
using Usa.Simulation.Time;

namespace Usa.Game.Hosting;

/// <summary>
/// What the presentation host needs from a running mission session (SCRIPTING 11.9, 12.2). The session
/// facade satisfies it through a small adapter, so hosting code never depends on the session's full API.
/// </summary>
public interface ISimulationSessionPort
{
    MissionTime Now { get; }

    bool IsFrozen { get; }

    /// <summary>The reasons the session currently holds; <see cref="FreezeController.Attach"/> reconciles them.</summary>
    FreezeReason FreezeReasons { get; }

    void Freeze(FreezeReason reason);

    void Unfreeze(FreezeReason reason);

    /// <summary>Runs whole ticks up to <paramref name="target"/>, at most <paramref name="maxTicks"/>.</summary>
    /// <returns>Ticks run; fewer than <paramref name="maxTicks"/> means the target was reached or the session is frozen.</returns>
    int Advance(MissionTime target, int maxTicks);

    /// <summary>Hands buffered events to <paramref name="sink"/> in publish order and clears the buffer.</summary>
    void DrainEvents(Action<ISimEvent> sink);

    IStateQuery State { get; }

    /// <summary>Current inputs for <see cref="TimeCompressionDirector"/>; cheap enough to call every 16 ticks.</summary>
    CompressionInputs ReadCompressionInputs();
}
