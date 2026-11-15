using Usa.Simulation.Crew.Health;
using Usa.Simulation.Crew.Tasks;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Crew;

public sealed record CrewTaskStarted(
    MissionTime Time, CommandId Origin, TaskId Task, TaskArguments Arguments, CrewId Assignee, double DurationS, MissionTime EndsAt) : ISimEvent;

/// <summary>The running task's end time moved because the assignee's band or rate impairment changed.</summary>
public sealed record CrewTaskRescheduled(
    MissionTime Time, CommandId Origin, TaskId Task, CrewId Assignee, MissionTime EndsAt) : ISimEvent;

public sealed record CrewTaskCompleted(
    MissionTime Time, CommandId Origin, TaskId Task, TaskArguments Arguments, CrewId Assignee) : ISimEvent;

/// <summary>The task ran to its end but the error draw failed; the onFailure effects apply.</summary>
public sealed record CrewTaskFailed(
    MissionTime Time, CommandId Origin, TaskId Task, TaskArguments Arguments, CrewId Assignee, double ErrorProbability) : ISimEvent;

public sealed record CrewTaskStopped(
    MissionTime Time, CommandId Origin, TaskId Task, TaskArguments Arguments, CrewId Assignee, TaskStopReason Reason, double Progress) : ISimEvent;

public sealed record CrewHealthBandChanged(
    MissionTime Time, CrewId Crew, HealthBand Previous, HealthBand Current, double Health) : ISimEvent;

/// <summary>A crew member died. The mission continues; whole-crew loss is judged from <c>crew.alive_count</c>.</summary>
public sealed record CrewMemberDied(MissionTime Time, CrewId Crew, HealthCause PrimaryCause) : ISimEvent;
