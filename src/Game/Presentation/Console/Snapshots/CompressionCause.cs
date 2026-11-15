namespace Usa.Game.Presentation.Console.Snapshots;

/// <summary>
/// Why time compression sits at its current rate (GDD 3.2). Mirrors the simulation's <c>CompressionReason</c>
/// one to one plus <see cref="None"/>; the session adapter maps it.
/// </summary>
public enum CompressionCause
{
    None,
    CriticalAlarm,
    HighAngularRate,
    BurnInProgress,
    Docking,
    EntryBelowInterface,
    ParachutePhase,
    EventApproaching,
    WorseningCondition,
    CrewTaskRunning,
    StableCoast,
}
