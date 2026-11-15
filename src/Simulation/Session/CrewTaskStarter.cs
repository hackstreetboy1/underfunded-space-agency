using Usa.Simulation.Commands;
using Usa.Simulation.Crew.Tasks;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Session;

/// <summary>
/// Starts crew tasks for the command pipeline: binds the action's arguments to the task (argument names are shared; typed
/// values convert one to one), checks the task's own preconditions and part requirements, then takes the slot.
/// <see cref="Check"/> runs the same checks without starting; the validator calls it before confirmation (review X9).
/// </summary>
internal sealed class CrewTaskStarter(TaskCatalog tasks, TaskBinder binder, CrewTaskRunner runner, IStateQuery state) : ICrewTaskStarter
{
    public const string UnknownTask = "unknown_task";

    public string? Check(CrewTaskRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Bind(request.Task, request.Args, out _);
    }

    public bool TryStart(CrewTaskRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Bind(request.Task, request.Args, out var bound) is not null) return false;
        return runner.TryStart(bound!, request.Assignee, request.Origin, request.Now) == TaskStartStatus.Started;
    }

    private string? Bind(TaskId taskId, IReadOnlyDictionary<string, CommandArg> args, out BoundTask? bound)
    {
        bound = null;
        if (!tasks.TryGet(taskId, out var task)) return UnknownTask;
        var arguments = new TaskArguments(args.Select(a => new KeyValuePair<string, StateValue>(a.Key, a.Value.ToStateValue())));
        var result = binder.Bind(task, arguments);
        if (!result.IsSuccess) return result.ErrorReason;
        bound = result.Task;
        return bound.FirstUnmetPrecondition(state);
    }
}
