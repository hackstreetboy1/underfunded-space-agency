using Usa.Simulation.Kernel;

namespace Usa.Simulation.Knowledge;

/// <summary>Snapshot of what is known about one component.</summary>
/// <param name="Condition">Confirmed condition word, e.g. <c>nominal</c> or <c>failed</c>.</param>
/// <param name="Variant">Damage variant after the ':' in the report, e.g. <c>stuck_open</c>.</param>
/// <param name="ConfirmedBy">The last confirming inspection or test.</param>
/// <param name="RepairUnverified">A repair was installed after the last confirmation and not yet verified.</param>
/// <param name="Suspicions">Clues that pointed at it (alarms, reasoning); they never confirm.</param>
public sealed record ComponentKnowledge(
    ComponentId Component,
    ComponentKnowledgeState State,
    string? Condition,
    string? Variant,
    Observation? ConfirmedBy,
    bool IsStale,
    bool RepairUnverified,
    IReadOnlyList<string> Suspicions)
{
    public static ComponentKnowledge Unknown(ComponentId component) =>
        new(component, ComponentKnowledgeState.Unknown, null, null, null, false, false, []);

    /// <summary>True when an inspection or test confirmed the condition (the 3D view shows it in colour).</summary>
    public bool IsRevealed => State is ComponentKnowledgeState.ConfirmedNominal or ComponentKnowledgeState.ConfirmedDamaged;
}
