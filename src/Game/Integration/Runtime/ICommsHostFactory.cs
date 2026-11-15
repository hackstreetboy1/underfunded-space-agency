using Usa.Game.Comms;

namespace Usa.Game.Integration.Runtime;

/// <summary>
/// Attaches the comms pipeline to the client services (the game's <c>CommsController</c> node: push-to-talk, connection
/// freeze, speech). Disposing the returned host detaches and frees it; the composer disposes the orchestrator afterwards.
/// </summary>
public interface ICommsHostFactory
{
    ICommsHost Attach(CommsOrchestrator comms);
}
