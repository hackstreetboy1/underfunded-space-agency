using Godot;
using Usa.Game.Comms.Hosting;

namespace Usa.Game.Integration.Runtime.Hosting;

/// <summary>The comms panel binds to <see cref="Controller"/>; disposing frees the node (it disposes the orchestrator too).</summary>
public sealed class CommsControllerHost(CommsController controller) : ICommsHost
{
    public CommsController Controller { get; } = controller ?? throw new ArgumentNullException(nameof(controller));

    public void Dispose()
    {
        if (GodotObject.IsInstanceValid(Controller) && !Controller.IsQueuedForDeletion()) Controller.QueueFree();
    }
}
