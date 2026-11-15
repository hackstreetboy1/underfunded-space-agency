namespace Usa.Simulation.Forecast;

/// <summary>Stable keys of <see cref="ForecastAssumption"/>; the UI maps each to localized text.</summary>
public static class ForecastAssumptionKeys
{
    /// <summary>Value: crew aboard now.</summary>
    public const string CrewCount = "crew_count";

    /// <summary>Value: food ration percent (only listed below 100).</summary>
    public const string FoodRation = "food_ration_pct";

    /// <summary>Value: drinking-water ration percent (only listed below 100).</summary>
    public const string WaterRation = "water_ration_pct";

    /// <summary>Subject: resource id. The quantity reading is old.</summary>
    public const string QuantityStale = "quantity_stale";

    public const string QuantityUnknown = "quantity_unknown";

    public const string ProductionStale = "production_stale";

    public const string ProductionUnknown = "production_unknown";

    public const string ConsumptionStale = "consumption_stale";

    public const string ConsumptionUnknown = "consumption_unknown";

    /// <summary>Subject: plan segment id; value: hours from now.</summary>
    public const string PlanSegment = "plan_segment";

    /// <summary>Value: hours from now until the planned recovery.</summary>
    public const string PlanEnd = "plan_end";

    /// <summary>Value: horizon hours; nothing runs out before it.</summary>
    public const string BeyondHorizon = "beyond_horizon";
}
