using Usa.Game.Comms;
using Usa.Game.Hosting;
using Usa.Simulation.Kernel;
using Usa.Simulation.Session;

namespace Usa.Game.Integration.Runtime;

/// <summary>
/// The one place that composes a started mission's runtime modules (console, comms, tips, advisor, 3D view source, orbit
/// map and planner, diagram, workspace, entry HUD, crew feeds, audio cues) over its session and event marshaller. The
/// game's <c>MissionFlowHost</c> calls the <c>MissionRuntime</c> overload when a run starts; tests call this one directly.
/// </summary>
public static partial class MissionRuntimeComposer
{
    /// <param name="events">The mission's event marshaller; the host flushes it each frame, then calls <see cref="MissionRuntimeContext.Tick"/>.</param>
    /// <param name="nextCommandId">The session's single command-id provider; null uses <see cref="MissionSession.NextCommandId"/>.</param>
    /// <param name="commsIds">Comms ids (<c>LoadedMission.CreateCommsIds()</c>); null draws command ids from <paramref name="nextCommandId"/>.</param>
    /// <param name="history">A resume's replayed events, already taken off the session; null for a fresh launch.</param>
    public static MissionRuntimeContext Compose(
        MissionSession session,
        EventMarshaller events,
        MissionRuntimeInputs inputs,
        Func<CommandId>? nextCommandId = null,
        CommandIdAllocator? commsIds = null,
        MissionHistory? history = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(inputs);
        return new MissionRuntimeContext(session, events, inputs, nextCommandId, commsIds, history);
    }
}
