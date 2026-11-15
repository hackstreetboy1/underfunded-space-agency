using Usa.Game.Flow.Backend;
using Usa.Game.Services.Backend;

namespace Usa.Game.Flow.Nodes;

/// <summary>The flow's <see cref="IBackendStarter"/> port over the OS process launcher.</summary>
public sealed class BackendStarterAdapter(BackendLauncher launcher) : IBackendStarter
{
    private readonly BackendLauncher _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));

    public bool CanStart => _launcher.CanStart;

    public bool IsRunning => _launcher.IsRunning;

    public bool TryStart() => _launcher.TryStart();
}
