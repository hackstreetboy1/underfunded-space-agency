using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Commands;

/// <summary>
/// SCRIPTING 11.3 steps 1-4: reject an empty command id, dedupe, resolve action and assignee, validate
/// (phase, arguments, preconditions, physical possibility, task slot and the task's own start checks) and
/// gate high-impact actions on confirmation. A meta executor, when given, checks meta commands
/// (e.g. a repair design) before confirmation as well.
/// Reads state only; never mutates.
/// </summary>
public sealed class CommandValidator(
    ActionCatalog catalog,
    IStateQuery state,
    ICrewDirectory crew,
    ExecutedCommands executed,
    IRemoteControlExecutor remote,
    ICrewTaskStarter crewTasks,
    IImpactAnalyzer? impactAnalyzer = null,
    IMetaCommandExecutor? meta = null)
{
    /// <summary>The state path holding the current mission phase id.</summary>
    public const string PhasePath = StatePaths.Mission + "phase";

    /// <param name="now">The MET the command would execute at; crew-task checks run against it.</param>
    public CommandValidation Validate(CommandRequest request, MissionTime now)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Id.Value)) return Refuse(CommandReasons.InvalidCommandId);
        if (executed.Contains(request.Id)) return Reject(ValidationResult.Duplicate());
        if (!catalog.TryGet(request.Action, out var action)) return Refuse(CommandReasons.UnknownAction, request.Action.Value);
        if (action.Definition.Meta == MetaCommandKind.RegisterRepairDesign && request.Source is not (CommandSource.Workspace or CommandSource.Replay))
            return Refuse(CommandReasons.WorkspaceOnly, request.Action.Value);

        CrewId? assignee = null;
        if (action.UsesCrew && ResolveAssignee(action, request.Assignee, out assignee) is { } refusal) return refusal;

        var phase = state.GetText(PhasePath);
        if (!action.IsAllowedIn(phase)) return Refuse(CommandReasons.NotAllowedInPhase, phase);

        var parsed = ActionArguments.Parse(action, request.Args);
        if (parsed.Arguments is not { } args) return Refuse(parsed.ReasonCode!, parsed.Detail);

        if (action.FirstFailedPrecondition(state, args) is { } reason) return Refuse(reason);

        if (action.Kind == ActionKind.Remote)
        {
            foreach (var effect in action.BuildEffects(args))
            {
                if (!remote.CanApply(effect)) return Refuse(CommandReasons.RemoteControlUnavailable, effect.Target);
                var value = effect.Value.Evaluate(state);
                if (value.Kind == StateValueKind.Number && !double.IsFinite(value.AsNumber()))
                    return Refuse(CommandReasons.InvalidParameterValue, effect.Target);
            }
        }

        if (action.Kind == ActionKind.CrewTask)
        {
            if (crew.ActiveTask is { } active) return Reject(ValidationResult.Busy(CommandReasons.CrewBusy, active.Value));
            var task = new CrewTaskRequest(request.Id, action.Definition.TaskId!.Value, assignee!.Value, args.Values, now);
            if (crewTasks.Check(task) is { } taskRefusal) return Refuse(taskRefusal, task.Task.Value);
        }

        if (action.Kind == ActionKind.Meta && meta is not null &&
            meta.Check(new MetaCommand(request.Id, action.Definition.Meta!.Value, assignee, now, args.Values)) is { } metaRefusal)
            return Refuse(metaRefusal, request.Action.Value);

        var command = new ResolvedCommand(request, action, assignee, args, action.ImpactFor(args));
        if (command.Impact == ActionImpact.Low) return new CommandValidation(ValidationResult.Ok(), command);

        var summary = Summarize(command);
        var result = request.Confirmed ? ValidationResult.Ok(summary) : ValidationResult.NeedsConfirmation(summary);
        return new CommandValidation(result, command);
    }

    private CommandValidation? ResolveAssignee(CatalogAction action, CrewId? requested, out CrewId? assignee)
    {
        assignee = null;
        if (requested is not { } named || named == CommandRequest.AnyCrew)
        {
            assignee = crew.BestAssignee(action.Definition.TaskId);
            return assignee is null ? Refuse(CommandReasons.NoCrewAvailable) : null;
        }

        if (!crew.IsMember(named)) return Refuse(CommandReasons.UnknownCrewMember, named.Value);
        if (!crew.IsAlive(named)) return Refuse(CommandReasons.CrewMemberDead, named.Value);
        if (!crew.CanWork(named)) return Refuse(CommandReasons.CrewMemberIncapacitated, named.Value);
        assignee = named;
        return null;
    }

    private ImpactSummary Summarize(ResolvedCommand command) => new(
        command.Impact,
        command.Action.EffectTextKeysFor(command.Args),
        command.Action.AffectedComponents(command.Args),
        impactAnalyzer?.PredictObjectiveChanges(command) ?? []);

    private static CommandValidation Refuse(string reasonCode, string? detail = null) =>
        Reject(ValidationResult.Refused(reasonCode, detail));

    private static CommandValidation Reject(ValidationResult result) => new(result, null);
}
