using Usa.Simulation.Kernel;

namespace Usa.Simulation.Crew.Tasks;

/// <summary>
/// Owns the single crew task slot shared by the whole crew (GDD 5.3). Preconditions are checked
/// before <see cref="TryStart"/> (see <see cref="BoundTask.FirstUnmetPrecondition"/>). Outcomes are
/// drawn from the "crew_errors" stream and reported to an <see cref="ICrewTaskOutcomeSink"/>.
/// </summary>
/// <param name="errors">The "crew_errors" RNG stream.</param>
public sealed class CrewTaskRunner(
    EventBus events, Pcg32 errors, CrewRoster roster, CrewPerformance performance, ICrewTaskOutcomeSink outcomes)
    : ISimSystem, IHashContributor
{
    public const string ErrorStreamName = "crew_errors";

    public int PeriodTicks => SimulationClock.TicksPerSecond;

    public ActiveCrewTask? Active { get; private set; }

    /// <summary>Starts <paramref name="task"/> if the slot is free.</summary>
    /// <param name="assignee">A named crew member, or null for "crew" (best available, see <see cref="CrewRoster.BestAvailable"/>).</param>
    public TaskStartStatus TryStart(BoundTask task, CrewId? assignee, CommandId origin, MissionTime now)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (Active is not null) return TaskStartStatus.SlotBusy;

        CrewMember? member;
        if (assignee is { } id)
        {
            if (!roster.TryFind(id, out member)) return TaskStartStatus.UnknownAssignee;
            if (!member.IsAlive) return TaskStartStatus.AssigneeUnavailable;
            if (!CrewRoster.IsWhereRequired(member, task.Definition)) return TaskStartStatus.WrongLocation;
        }
        else
        {
            member = roster.BestAvailable(task.Definition);
            if (member is null) return TaskStartStatus.AssigneeUnavailable;
        }

        var durationS = performance.DurationSeconds(task.Definition, member);
        var endsAt = now + ToMilliseconds(durationS);
        roster.BeginActivity(member, task.Definition);
        Active = new ActiveCrewTask(
            task, member.Id, origin, now, endsAt, durationS, 0.0, now, member.Band, performance.IsRateImpaired(member.AngularRateDps));
        events.Publish(new CrewTaskStarted(now, origin, task.Id, task.Arguments, member.Id, durationS, endsAt));
        return TaskStartStatus.Started;
    }

    public TaskStopStatus Stop(MissionTime now, TaskStopReason reason = TaskStopReason.Player)
    {
        if (Active is not { } active) return TaskStopStatus.NoActiveTask;
        if (reason == TaskStopReason.Player && !active.Task.Definition.Interruptible) return TaskStopStatus.NotInterruptible;

        var progress = active.ProgressAt(now);
        Release(active);
        outcomes.OnTaskEnded(new CrewTaskOutcome(active.Task, active.Assignee, active.Origin, CrewTaskResult.Stopped, now, active.Task.OnStop, []));
        events.Publish(new CrewTaskStopped(now, active.Origin, active.Task.Id, active.Task.Arguments, active.Assignee, reason, progress));
        return TaskStopStatus.Stopped;
    }

    public void Step(in StepContext context)
    {
        if (Active is not { } active) return;
        var member = roster[active.Assignee];
        if (!member.IsAlive)
        {
            Stop(context.Now, TaskStopReason.AssigneeDied);
            return;
        }
        if (member.MovedDuringTask && !CrewRoster.IsWhereRequired(member, active.Task.Definition))
        {
            Stop(context.Now, TaskStopReason.Relocated);
            return;
        }

        var rateImpaired = performance.IsRateImpaired(member.AngularRateDps);
        if (member.Band != active.PlannedBand || rateImpaired != active.PlannedRateImpaired)
            active = Reschedule(active, member, rateImpaired, context.Now);
        if (context.Now < active.EndsAt) return;

        var errorProbability = performance.ErrorProbability(active.Task.Definition, member);
        var succeeded = errors.NextDouble() >= errorProbability;
        Release(active, succeeded ? active.Task.Definition.MovesTo : null);
        var task = active.Task;
        if (succeeded)
        {
            outcomes.OnTaskEnded(new CrewTaskOutcome(
                task, active.Assignee, active.Origin, CrewTaskResult.Succeeded, context.Now, task.OnSuccess, task.Observations));
            events.Publish(new CrewTaskCompleted(context.Now, active.Origin, task.Id, task.Arguments, active.Assignee));
        }
        else
        {
            outcomes.OnTaskEnded(new CrewTaskOutcome(
                task, active.Assignee, active.Origin, CrewTaskResult.Failed, context.Now, task.OnFailure, []));
            events.Publish(new CrewTaskFailed(context.Now, active.Origin, task.Id, task.Arguments, active.Assignee, errorProbability));
        }
    }

    public void WriteHash(StateHashWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(unchecked((long)errors.State));
        writer.Write(Active is not null);
        if (Active is not { } active) return;
        writer.Write(active.Task.Id.Value);
        writer.Write(active.Task.Arguments.ToString());
        writer.Write(active.Assignee.Value);
        writer.Write(active.Origin.Value ?? "");
        writer.Write(active.StartedAt.Milliseconds);
        writer.Write(active.EndsAt.Milliseconds);
        writer.Write(active.DurationS);
        writer.Write(active.SegmentProgress);
        writer.Write(active.SegmentStart.Milliseconds);
        writer.Write((long)active.PlannedBand);
        writer.Write(active.PlannedRateImpaired);
    }

    private ActiveCrewTask Reschedule(ActiveCrewTask active, CrewMember member, bool rateImpaired, MissionTime now)
    {
        var progress = active.ProgressAt(now);
        var durationS = performance.DurationSeconds(active.Task.Definition, member);
        var endsAt = now + ToMilliseconds((1.0 - progress) * durationS);
        var rescheduled = active with
        {
            EndsAt = endsAt,
            DurationS = durationS,
            SegmentProgress = progress,
            SegmentStart = now,
            PlannedBand = member.Band,
            PlannedRateImpaired = rateImpaired,
        };
        Active = rescheduled;
        events.Publish(new CrewTaskRescheduled(now, active.Origin, active.Task.Id, active.Assignee, endsAt));
        return rescheduled;
    }

    private void Release(ActiveCrewTask active, string? movesTo = null)
    {
        Active = null;
        roster.EndActivity(roster[active.Assignee], movesTo);
    }

    private static long ToMilliseconds(double seconds) => (long)Math.Round(seconds * 1000.0);
}
