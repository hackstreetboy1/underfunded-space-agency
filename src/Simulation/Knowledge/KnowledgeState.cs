namespace Usa.Simulation.Knowledge;

/// <summary>What the player can rely on for a fact (GDD 4.1).</summary>
public enum KnowledgeState
{
    Unknown,
    Known,
    Stale,
    Conflicting,
    Estimated,
}

/// <summary>What is known about a component's physical condition. Only inspections and component tests confirm.</summary>
public enum ComponentKnowledgeState
{
    Unknown,
    Suspected,
    ConfirmedNominal,
    ConfirmedDamaged,
}
