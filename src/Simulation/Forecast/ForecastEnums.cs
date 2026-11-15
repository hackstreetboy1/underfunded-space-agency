namespace Usa.Simulation.Forecast;

/// <summary>How well the ground knows an input; maps from the Knowledge area's fact states.</summary>
/// <remarks>
/// Mapping from <c>KnowledgeState</c>: Known → <see cref="Fresh"/>; Stale and Estimated → <see cref="Stale"/> (a value
/// exists but is not a fresh measurement); Unknown and Conflicting → <see cref="Unknown"/> (a value the player sees
/// flagged CONFLICT must never give a confident forecast).
/// </remarks>
public enum InputKnowledge
{
    Fresh,
    Stale,
    Unknown,
}

/// <summary>Which ration setting scales a resource's per-crew consumption.</summary>
public enum RationKind
{
    None,
    Food,
    Water,
}

public enum ForecastConfidence
{
    Low,
    Medium,
    High,
}

/// <summary>Whether the safe autonomy range covers the planned end (crew recovered).</summary>
public enum PlanCoverage
{
    /// <summary>The input has no planned end.</summary>
    NoPlanEnd,

    /// <summary>Even the pessimistic bound reaches the planned end.</summary>
    Covered,

    /// <summary>Only the optimistic bound reaches the planned end.</summary>
    AtRisk,

    NotCovered,
}

public enum PlanChangeKind
{
    SetQuantity,
    AddQuantity,
    SetProductionPerH,
    ScaleProduction,

    /// <summary>Sets the crew-independent consumption.</summary>
    SetConsumptionPerH,

    AddConsumptionPerH,

    /// <summary>Scales the crew-independent consumption (e.g. powering down loads).</summary>
    ScaleConsumption,
}
