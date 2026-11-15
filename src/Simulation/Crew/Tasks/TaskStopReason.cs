namespace Usa.Simulation.Crew.Tasks;

public enum TaskStopReason
{
    /// <summary>The player's stop order; refused for tasks that are not interruptible.</summary>
    Player,

    AssigneeDied,

    /// <summary>Stopped by the simulation (e.g. mission end); always honoured.</summary>
    Aborted,

    /// <summary>The assignee was moved to a home the task's required locations do not include; always honoured.</summary>
    Relocated,
}
