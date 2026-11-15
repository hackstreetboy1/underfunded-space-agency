using Usa.Simulation.Kernel;
using Usa.Simulation.Session;

namespace Usa.Game.Integration.Runtime;

/// <summary>
/// The events a resumed session raised while its command log was replayed up to the saved MET. They are taken off the
/// session before anything is composed, so the live runtime (alarms, comms, tips, cues, crew feeds) only ever sees events
/// from <see cref="ResumedAt"/> on. The debrief recorder takes all of them; the runtime only restores state from them
/// (<see cref="MissionRuntimeContext"/>: event log rows, entry chute results, the crew feeds' alarm set), raising nothing.
/// </summary>
public sealed class MissionHistory
{
    private MissionHistory(IReadOnlyList<ISimEvent> events, MissionTime resumedAt)
    {
        Events = events;
        ResumedAt = resumedAt;
    }

    /// <summary>A fresh launch: nothing happened before the runtime was composed.</summary>
    public static MissionHistory None { get; } = new([], MissionTime.Zero);

    /// <summary>In the order the session raised them.</summary>
    public IReadOnlyList<ISimEvent> Events { get; }

    public MissionTime ResumedAt { get; }

    /// <summary>Takes the replay's events off <paramref name="session"/> (call right after <c>MissionSession.Replay</c>).</summary>
    public static MissionHistory TakeFrom(MissionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new MissionHistory(session.DrainReplayHistory(), session.Now);
    }
}
