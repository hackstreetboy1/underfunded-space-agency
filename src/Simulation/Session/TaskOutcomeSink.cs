using Usa.Simulation.Crew.Tasks;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Session;

/// <summary>
/// Applies a finished crew task: its effects go through <see cref="EffectRouter.ApplyAll"/> as one atomic list (an effect no
/// handler accepts is skipped and reported as <see cref="SessionEffectRefused"/> <c>effect_unroutable</c>; a non-finite value
/// applies none of them, <c>effect_non_finite</c>), and each observation is read
/// by the assignee (<see cref="FactValueReader"/>) and submitted to the knowledge base.
/// </summary>
internal sealed class TaskOutcomeSink(EffectRouter router, KnowledgeBase knowledge, FactValueReader reader, EventBus events) : ICrewTaskOutcomeSink
{
    public const string EffectUnroutable = "effect_unroutable";

    /// <summary>An effect value or result was not finite, so none of the outcome's effects were applied.</summary>
    public const string EffectNonFinite = "effect_non_finite";

    public void OnTaskEnded(CrewTaskOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var applied = router.ApplyAll(outcome.Effects, outcome.Time, skipUnroutable: true,
            effect => events.Publish(new SessionEffectRefused(outcome.Time, effect.Target, EffectUnroutable)));
        if (applied.Failed is { } failed) events.Publish(new SessionEffectRefused(outcome.Time, failed.Target, EffectNonFinite));

        foreach (var observation in outcome.Observations)
        {
            var report = reader.Read(observation.Fact, outcome.Assignee, MethodOf(observation.Method), outcome.Time, observation.Scope);
            if (report is not null) knowledge.Submit(report with { Origin = outcome.Origin });
        }
    }

    public static ObservationMethod MethodOf(TaskObservationMethod method) => method switch
    {
        TaskObservationMethod.Glance => ObservationMethod.Glance,
        TaskObservationMethod.Instrument => ObservationMethod.Instrument,
        TaskObservationMethod.Inspection => ObservationMethod.Inspection,
        _ => ObservationMethod.Test,
    };
}
