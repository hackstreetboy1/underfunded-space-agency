using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Snapshots;

/// <summary>The single crew task slot (GDD 5.3).</summary>
public sealed record ActiveTaskSnapshot(
    TaskId Id,
    string Name,
    CrewId AssigneeId,
    string AssigneeName,
    MissionTime StartedAt,
    MissionTime EstimatedEnd)
{
    /// <summary>0-1; null derives it from the start and estimated end.</summary>
    public double? Progress { get; init; }

    /// <summary>1-5 complexity pips.</summary>
    public int Complexity { get; init; } = 1;

    /// <summary>The task can be stopped at all.</summary>
    public bool Interruptible { get; init; } = true;

    /// <summary>Stopping is allowed but unsafe, so Stop asks for confirmation.</summary>
    public bool UnsafeToInterrupt { get; init; }
}
