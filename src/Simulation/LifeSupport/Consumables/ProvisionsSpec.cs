using Usa.Simulation.Data;
using Usa.Simulation.LifeSupport.Config;

namespace Usa.Simulation.LifeSupport.Consumables;

/// <summary>Starting food and potable water, filled by integration from vehicle data.</summary>
public sealed record ProvisionsSpec(double FoodKcal, double WaterL, double WaterCapacityL)
{
    /// <summary>Applies the difficulty key <c>resource_margin</c> (e.g. 0.20 → 20 % more stowed).</summary>
    public ProvisionsSpec WithResourceMargin(double margin)
    {
        var scale = 1.0 + margin;
        return new ProvisionsSpec(FoodKcal * scale, WaterL * scale, WaterCapacityL * scale);
    }

    public void Validate(DefinitionErrors errors, string file)
    {
        var check = new DataChecks(errors, file);
        check.NotNegative("$.provisions.foodKcal", FoodKcal);
        check.NotNegative("$.provisions.waterL", WaterL);
        check.Positive("$.provisions.waterCapacityL", WaterCapacityL);
        if (WaterL > WaterCapacityL) errors.Add(file, "$.provisions.waterL", "Exceeds waterCapacityL.");
    }
}

/// <summary>A crew member's ration as percentages of the reference intake (one of the configured tiers).</summary>
public readonly record struct RationPlan(double FoodPct, double WaterPct)
{
    public static RationPlan Full { get; } = new(100, 100);
}

/// <summary>Flows of one provisions step.</summary>
/// <param name="WaterVentedL">Fuel-cell water that did not fit in the tank.</param>
public readonly record struct ProvisionsStepResult(double FoodEatenKcal, double WaterUsedL, double WaterAddedL, double WaterVentedL);
