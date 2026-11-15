using Usa.Game.Bootstrap;

namespace Usa.Game.UI.Frontend;

/// <summary>
/// Optional contract for the mission console scene (<c>res://ui/console/MissionConsole.tscn</c>): the shell instantiates it
/// when the router enters <c>Screen.Mission</c> and hands it the services; the console binds to
/// <c>services.Flow.Context</c> / <c>ContextChanged</c> itself.
/// </summary>
public interface IMissionScene
{
    void Initialize(GameServices services);
}
