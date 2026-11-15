namespace Usa.Simulation.Time;

/// <summary>Why the compression rate is what it is, for the UI. Listed in rule priority order.</summary>
public enum CompressionReason
{
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
