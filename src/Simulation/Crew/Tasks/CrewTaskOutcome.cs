using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Crew.Tasks;

public enum CrewTaskResult
{
    Succeeded,
    Failed,
    Stopped,
}

/// <summary>How a task in the crew slot ended, handed to <see cref="ICrewTaskOutcomeSink"/>.</summary>
/// <param name="Effects">onSuccess (plus part consumption), onFailure or onStop effects, in data order.</param>
/// <param name="Observations">Observations to record; empty unless the task succeeded.</param>
public sealed record CrewTaskOutcome(
    BoundTask Task,
    CrewId Assignee,
    CommandId Origin,
    CrewTaskResult Result,
    MissionTime Time,
    IReadOnlyList<EffectDefinition> Effects,
    IReadOnlyList<TaskObservation> Observations);
