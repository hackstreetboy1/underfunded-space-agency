namespace Usa.Simulation.Crew.Tasks;

public enum TaskStartStatus
{
    Started,

    /// <summary>Another task holds the single crew slot.</summary>
    SlotBusy,

    UnknownAssignee,

    /// <summary>The named member is dead, or nobody alive is free for a task addressed to "crew".</summary>
    AssigneeUnavailable,

    /// <summary>The named member is not at one of the task's <see cref="TaskDefinition.RequiredLocations"/>.</summary>
    WrongLocation,
}
