using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;

namespace Usa.Game.Comms;

/// <summary>Something the crew talks about, raised by the session through <see cref="ICommsSessionPort.EventRaised"/>.</summary>
public abstract record CommsSessionEvent(MissionTime Time);

public enum CommsTaskEventKind
{
    Started,
    Completed,
    Failed,
    Stopped,
}

/// <summary>A crew task changed state. <paramref name="Origin"/> is the command that started it.</summary>
/// <param name="Detail">Stop reason or other short machine detail; never shown verbatim.</param>
public sealed record CommsTaskEvent(
    MissionTime Time, CommsTaskEventKind Kind, CommandId Origin, TaskId Task, CrewId Assignee, string? Detail = null)
    : CommsSessionEvent(Time);

/// <summary>
/// One batch of observations a crew member made together (a glance read or a task result). Observations announced
/// through a <see cref="CommsCrewReport"/> are not raised again here.
/// </summary>
/// <param name="Origin">The command that asked for them, when known.</param>
public sealed record CommsObservationsRecorded(
    MissionTime Time, CrewId Observer, IReadOnlyList<Observation> Observations, CommandId? Origin = null)
    : CommsSessionEvent(Time);

/// <summary>A significant observation the crew reports on their own (GDD 5.1: new hazard, changing rate).</summary>
/// <param name="Summary">Short machine description of what changed, e.g. <c>roll rate rising</c>; sent to narration, not shown.</param>
/// <param name="Critical">Interrupts idle chatter.</param>
public sealed record CommsCrewReport(
    MissionTime Time, CrewId Crew, string Summary, IReadOnlyList<FactId> Facts, bool Critical = false)
    : CommsSessionEvent(Time);
