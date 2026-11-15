using Usa.Simulation.Kernel;

namespace Usa.Simulation.Commands;

/// <summary>A validated request to occupy the crew task slot.</summary>
/// <param name="Args">Parsed arguments in name order (e.g. which breaker to open).</param>
public sealed record CrewTaskRequest(CommandId Origin, TaskId Task, CrewId Assignee, IReadOnlyDictionary<string, CommandArg> Args, MissionTime Now);

/// <summary>Starts crew tasks. Implemented over the Crew area's task runner.</summary>
/// <remarks>
/// <see cref="Check"/> is the single source of truth for whether a task can start: the task's own
/// preconditions, parts and argument checks (for example that the named repair assembly is ready).
/// The validator calls it before an order is confirmed or accepted, so the crew refuses up front instead
/// of failing after <c>CommandAccepted</c>.
/// </remarks>
public interface ICrewTaskStarter
{
    /// <summary>Reads state only. Must agree with <see cref="TryStart"/> for the same request and state.</summary>
    /// <returns>Null when the task can start now; otherwise the reason code the crew explains (e.g. <c>assembly_not_ready</c>).</returns>
    string? Check(CrewTaskRequest request);

    /// <returns>False when the task could not start (the slot was taken or state changed after validation).</returns>
    bool TryStart(CrewTaskRequest request);
}
