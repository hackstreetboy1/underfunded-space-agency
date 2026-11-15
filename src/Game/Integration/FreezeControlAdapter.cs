using Usa.Game.Flow;
using Usa.Game.Hosting;
using Usa.Simulation.Kernel;

namespace Usa.Game.Integration;

/// <summary>Lets the screen router set freeze reasons on the app-wide <see cref="FreezeController"/>.</summary>
public sealed class FreezeControlAdapter(FreezeController freeze) : IFreezeControl
{
    public void Freeze(FreezeReason reason) => freeze.Freeze(reason);

    public void Unfreeze(FreezeReason reason) => freeze.Unfreeze(reason);
}
