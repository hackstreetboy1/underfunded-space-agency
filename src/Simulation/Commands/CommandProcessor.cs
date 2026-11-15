using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Commands;

/// <summary>
/// The single input path into the simulation (SCRIPTING 11.3). Every request, including replays,
/// is validated, logged and then executed through the ports. Duplicates and requests without a command id are
/// neither logged nor executed.
/// </summary>
public sealed class CommandProcessor(
    CommandValidator validator,
    ExecutedCommands executed,
    CommandLog log,
    EventBus events,
    ICrewTaskStarter crewTasks,
    IRemoteControlExecutor remote,
    IGlanceReadExecutor glance,
    IMetaCommandExecutor meta)
{
    public ValidationResult Submit(CommandRequest request, MissionTime now)
    {
        var validation = validator.Validate(request, now);
        var result = validation.Result;
        if (result.Status == ValidationStatus.Duplicate) return result;
        if (result.ReasonCode == CommandReasons.InvalidCommandId)
        {
            // Not logged: a log entry without an id could neither dedupe nor replay.
            events.Publish(new CommandRefused(now, request.Id, request.Action, result.Status, result.ReasonCode, null));
            return result;
        }

        log.Append(now, request);
        switch (result.Status)
        {
            case ValidationStatus.Valid:
                return Execute(validation.Command!, result, now);
            case ValidationStatus.NeedsConfirmation:
                events.Publish(new CommandNeedsConfirmation(now, request.Id, request.Action, result.Impact!));
                return result;
            default:
                events.Publish(new CommandRefused(now, request.Id, request.Action, result.Status, result.ReasonCode!, result.Detail));
                return result;
        }
    }

    /// <summary>Re-submits every logged request at its MET; the caller advances the clock between entries.</summary>
    public ValidationResult Replay(CommandLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Submit(entry.Request, new MissionTime(entry.MetMs));
    }

    private ValidationResult Execute(ResolvedCommand command, ValidationResult result, MissionTime now)
    {
        var request = command.Request;
        var arguments = command.Args.Values;
        events.Publish(new CommandAccepted(now, request.Id, request.Action, command.Assignee) { Arguments = arguments });
        if (!TryExecute(command, now))
        {
            var failed = ValidationResult.Refused(CommandReasons.ExecutionFailed);
            events.Publish(new CommandRefused(now, request.Id, request.Action, failed.Status, CommandReasons.ExecutionFailed, null));
            return failed;
        }

        executed.Add(request.Id);
        events.Publish(new CommandExecuted(now, request.Id, request.Action, command.Action.Kind, command.Assignee) { Arguments = arguments });
        return result;
    }

    private bool TryExecute(ResolvedCommand command, MissionTime now)
    {
        var action = command.Action;
        var origin = command.Request.Id;
        switch (action.Kind)
        {
            case ActionKind.CrewTask:
                return crewTasks.TryStart(new CrewTaskRequest(origin, action.Definition.TaskId!.Value, command.Assignee!.Value, command.Args.Values, now));

            case ActionKind.Remote:
                return remote.ApplyAll(action.BuildEffects(command.Args), now);

            case ActionKind.GlanceRead:
                var delayMs = MissionTime.FromSeconds(action.Definition.Glance!.DelayS).Milliseconds;
                glance.Schedule(new GlanceRead(origin, command.Assignee!.Value, action.GlanceFacts(command.Args), now, now + delayMs));
                return true;

            default:
                meta.Execute(new MetaCommand(origin, action.Definition.Meta!.Value, command.Assignee, now, command.Args.Values));
                return true;
        }
    }
}
