using Usa.Simulation.Kernel;

namespace Usa.Game.Flow;

/// <summary>
/// Independent freeze reasons for the mission session (GDD 3.2): the router sets <see cref="FreezeReason.Menu"/>,
/// <see cref="FreezeReason.ServiceOutage"/> and <see cref="FreezeReason.Loading"/>, each once when it starts and once
/// when it ends. Reasons may be set while no session exists; the implementation applies them when one attaches.
/// </summary>
public interface IFreezeControl
{
    void Freeze(FreezeReason reason);

    void Unfreeze(FreezeReason reason);
}
