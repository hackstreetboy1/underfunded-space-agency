using Usa.Game.Bootstrap;
using Usa.Game.Flow.Backend;

namespace Usa.Game.Diagnostics.Overlay;

/// <summary>
/// The overlay's and repros' backend line from the running services: the newest heartbeat (not the menu's debounced status,
/// so a blip shows here) with its <c>/health</c> mode, plus the proxy launcher's state from <see cref="GameServices.Backend"/>.
/// </summary>
public static class BackendStatusSource
{
    public static string Describe(GameServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var launcher = services.Backend?.Current switch
        {
            { State: BackendState.Starting } => "starting",
            { LaunchFailed: true } => "failed to start",
            _ => null,
        };
        return BackendStatusText.Describe(services.Connection.LastReport, services.Connection.IsLost, launcher);
    }
}
