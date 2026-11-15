using Usa.Simulation.Commands;
using Usa.Simulation.Guidance;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Session;

/// <summary>
/// Runs <see cref="TutorialCommands"/> for <see cref="MissionSession.Submit"/>: logged and deduplicated like every request (the
/// tutorial state is hashed, so a replay must repeat them), but outside the action catalog, so no command events are published
/// and neither the AI, the crew nor the event log sees them.
/// </summary>
internal sealed class TutorialCommandHandler(TutorialDirector tutorial, CommandLog log, ExecutedCommands executed)
{
    public ValidationResult Submit(CommandRequest request, MissionTime now)
    {
        if (string.IsNullOrWhiteSpace(request.Id.Value)) return ValidationResult.Refused(CommandReasons.InvalidCommandId);
        if (executed.Contains(request.Id)) return ValidationResult.Duplicate();

        log.Append(now, request);
        var result = Execute(request, now);
        if (result.IsValid) executed.Add(request.Id);
        return result;
    }

    private ValidationResult Execute(CommandRequest request, MissionTime now)
    {
        if (request.Action.Value == TutorialCommands.DismissBeat)
        {
            if (!request.Args.TryGetValue(TutorialCommands.BeatArg, out var beat) || beat.Kind != CommandArgKind.Text)
                return ValidationResult.Refused(CommandReasons.MissingParameter, TutorialCommands.BeatArg);
            return tutorial.Dismiss(beat.AsText(), now)
                ? ValidationResult.Ok()
                : ValidationResult.Refused(TutorialCommands.NotShownReason, beat.AsText());
        }

        if (!request.Args.TryGetValue(TutorialCommands.EnabledArg, out var enabled) || enabled.Kind != CommandArgKind.Boolean)
            return ValidationResult.Refused(CommandReasons.MissingParameter, TutorialCommands.EnabledArg);
        tutorial.SetEnabled(enabled.AsBool(), now);
        return ValidationResult.Ok();
    }
}
