using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge.Catalog;

namespace Usa.Simulation.Knowledge;

/// <summary>Mutable per-fact knowledge owned by <see cref="KnowledgeBase"/>.</summary>
internal sealed class FactRecord(FactDefinition definition, IReadOnlyList<FactId> conflictGroup, int methodCount)
{
    public FactDefinition Definition { get; } = definition;

    public IReadOnlyList<FactId> ConflictGroup { get; } = conflictGroup;

    public List<Observation> History { get; } = [];

    /// <summary>Latest measurement per <see cref="ObservationMethod"/>; repeats of one method supersede each other.</summary>
    public Observation?[] LatestByMethod { get; } = new Observation?[methodCount];

    public Observation? LatestMeasurement { get; set; }

    public Observation? LatestEstimate { get; set; }

    public Observation? LatestBand { get; set; }

    public KnowledgeState State { get; set; }
}

/// <summary>Mutable per-component knowledge owned by <see cref="KnowledgeBase"/>.</summary>
internal sealed class ComponentRecord(ComponentId component, FactDefinition conditionFact)
{
    public ComponentId Component { get; } = component;

    public FactDefinition ConditionFact { get; } = conditionFact;

    public ComponentKnowledgeState State { get; set; }

    public string? Condition { get; set; }

    public string? Variant { get; set; }

    public Observation? ConfirmedBy { get; set; }

    public bool IsStale { get; set; }

    public bool RepairUnverified { get; set; }

    public MissionTime? RepairTime { get; set; }

    public List<string> Suspicions { get; } = [];

    public ComponentKnowledge Snapshot() =>
        new(Component, State, Condition, Variant, ConfirmedBy, IsStale, RepairUnverified, Suspicions.ToArray());
}
