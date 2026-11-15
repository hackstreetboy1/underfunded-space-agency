using Usa.Simulation.Kernel;

namespace Usa.Simulation.Guidance;

/// <summary>What the player and crew are doing right now; set by the integration step whenever it changes.</summary>
/// <param name="CrewTaskRunning">A crew task occupies the slot (the player is waiting, not stuck).</param>
/// <param name="ReadingReference">The player has a report or encyclopedia entry open.</param>
/// <param name="ExecutingPlan">A valid multi-step plan is in progress (e.g. a queued burn sequence).</param>
/// <param name="UnresolvedCriticalAlarms">Critical alarms raised and not yet cleared.</param>
public readonly record struct GuidanceActivity(
    bool CrewTaskRunning,
    bool ReadingReference,
    bool ExecutingPlan,
    int UnresolvedCriticalAlarms)
{
    public static GuidanceActivity Idle => default;

    /// <summary>The stuck score holds still while the player is busy in a productive way (GDD 9.1).</summary>
    public bool SuspendsStuck => CrewTaskRunning || ReadingReference || ExecutingPlan;
}

public enum GuidanceSignalKind
{
    /// <summary>A command was refused or a crew task failed.</summary>
    FailedAction,

    /// <summary>New diagnostic information arrived (an observation, a test result).</summary>
    Diagnostic,

    Repair,

    /// <summary>A rate, resource or system was brought back under control.</summary>
    Stabilization,
}

/// <summary>A gameplay happening that moves stuck scores; the integration step derives these from simulation events.</summary>
/// <param name="Fact">For diagnostics, the fact learned; relief then applies only to problems that point at it.</param>
public readonly record struct GuidanceSignal(GuidanceSignalKind Kind, FactId? Fact = null);
