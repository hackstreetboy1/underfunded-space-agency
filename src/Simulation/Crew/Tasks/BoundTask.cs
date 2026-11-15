using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Crew.Tasks;

/// <summary>A task with its arguments substituted and its expressions compiled, ready to start.</summary>
public sealed record BoundTask(
    TaskDefinition Definition,
    TaskArguments Arguments,
    IReadOnlyList<BoundPrecondition> Preconditions,
    IReadOnlyList<EffectDefinition> OnSuccess,
    IReadOnlyList<EffectDefinition> OnFailure,
    IReadOnlyList<EffectDefinition> OnStop,
    IReadOnlyList<TaskObservation> Observations)
{
    public TaskId Id => Definition.Id;

    /// <summary>Reason code of the first precondition that does not hold, or null when the task may start.</summary>
    public string? FirstUnmetPrecondition(IStateQuery state)
    {
        foreach (var precondition in Preconditions)
        {
            if (!precondition.Condition.IsTrue(state)) return precondition.Reason;
        }
        return null;
    }
}

public sealed record BoundPrecondition(IExpression Condition, string Reason);

/// <summary>A fact the task establishes on success, and the components it covers.</summary>
public sealed record TaskObservation(FactId Fact, TaskObservationMethod Method, IReadOnlyList<ComponentId> Scope);
