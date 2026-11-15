using Godot;
using Usa.Game.Hosting.Nodes;

namespace Usa.Game.Integration.Runtime.Hosting;

/// <summary>
/// Calls <see cref="MissionRuntimeContext.Tick"/> every frame. Its process priority puts it after the flow host's
/// <see cref="SimulationHost"/> (<see cref="SimulationHost.ProcessOrder"/>), so each tick sees the events that frame's flush delivered.
/// </summary>
public sealed partial class MissionRuntimeTicker : Node
{
    public const int AfterSimulationHost = SimulationHost.ProcessOrder + 100;

    private MissionRuntimeContext? _context;

    public void Initialize(MissionRuntimeContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        Name = nameof(MissionRuntimeTicker);
        ProcessPriority = AfterSimulationHost;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Process(double delta) => _context?.Tick(delta);

    public void Detach()
    {
        _context = null;
        if (IsInstanceValid(this) && !IsQueuedForDeletion()) QueueFree();
    }
}
