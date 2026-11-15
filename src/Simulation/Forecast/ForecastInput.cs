using Usa.Simulation.Kernel;

namespace Usa.Simulation.Forecast;

/// <summary>
/// One consumable as the forecast sees it, e.g. <c>o2_kg</c>, <c>battery_kwh</c>, <c>lioh_kg_co2</c>, <c>water_l</c>.
/// Integration fills it from what the ground knows (knowledge values), not from hidden truth.
/// </summary>
public sealed record ForecastResource
{
    public required string Id { get; init; }

    public required double Quantity { get; init; }

    public InputKnowledge QuantityKnowledge { get; init; }

    /// <summary>The resource stops keeping the crew safe at this level (unusable residual, minimum battery charge).</summary>
    public double ReserveQuantity { get; init; }

    public double ProductionPerH { get; init; }

    public InputKnowledge ProductionKnowledge { get; init; }

    /// <summary>Crew-independent consumption: loads, leaks, boil-off.</summary>
    public double ConsumptionPerH { get; init; }

    /// <summary>Per crew member at a full ration; scaled by the <see cref="Ration"/> setting.</summary>
    public double PerCrewConsumptionPerH { get; init; }

    /// <summary>Per crew member and never rationed (hygiene water).</summary>
    public double PerCrewOverheadPerH { get; init; }

    public RationKind Ration { get; init; }

    /// <summary>Applies to every consumption term.</summary>
    public InputKnowledge ConsumptionKnowledge { get; init; }
}

/// <summary>A future plan step that changes generation or consumption, e.g. <c>equipment_sep</c> removing the fuel cells.</summary>
public sealed record PlanSegment(string Id, MissionTime Start, IReadOnlyList<PlanChange> Changes)
{
    /// <summary>Crew aboard from this segment on, when it changes.</summary>
    public int? CrewCount { get; init; }
}

public sealed record PlanChange(string ResourceId, PlanChangeKind Kind, double Value);

public sealed record ForecastInput(MissionTime Now, IReadOnlyList<ForecastResource> Resources)
{
    public int CrewCount { get; init; }

    public double FoodRationPct { get; init; } = 100;

    public double WaterRationPct { get; init; } = 100;

    /// <summary>Pending plan steps. Segments that start before <see cref="Now"/> are ignored as already executed; one at
    /// exactly <see cref="Now"/> is still applied, so drop a segment from the plan once it has executed.</summary>
    public IReadOnlyList<PlanSegment> Plan { get; init; } = [];

    /// <summary>When the plan ends with the crew recovered; null when there is no planned end.</summary>
    public MissionTime? PlanEnd { get; init; }
}
