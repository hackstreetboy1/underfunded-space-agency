using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Snapshots;

/// <summary>One event-log line, appended as simulation events arrive.</summary>
/// <param name="Sequence">Increasing per session; orders entries with the same MET.</param>
/// <param name="Source">Who or what produced it, e.g. <c>Okafor</c>, <c>Ground</c>, <c>Annunciator</c>.</param>
public sealed record LogEntry(long Sequence, MissionTime At, LogCategory Category, string Text, string Source)
{
    /// <summary>Alarm rows only: colours the row by priority.</summary>
    public AlarmPriority? Priority { get; init; }
}
