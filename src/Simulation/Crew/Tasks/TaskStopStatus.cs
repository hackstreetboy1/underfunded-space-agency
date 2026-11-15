namespace Usa.Simulation.Crew.Tasks;

public enum TaskStopStatus
{
    Stopped,
    NoActiveTask,

    /// <summary>The player asked to stop a task marked not interruptible (e.g. retrofire).</summary>
    NotInterruptible,
}
