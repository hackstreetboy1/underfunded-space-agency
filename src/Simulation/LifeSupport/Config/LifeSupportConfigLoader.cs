using Usa.Simulation.Data;

namespace Usa.Simulation.LifeSupport.Config;

public static class LifeSupportConfigLoader
{
    public static LifeSupportConfig Load(IDataSource source)
    {
        var config = new DefinitionLoader(source).Load<LifeSupportConfig>(LifeSupportConfig.RelativePath, LifeSupportConfig.SchemaTag);
        var errors = new DefinitionErrors();
        Validate(config, LifeSupportConfig.RelativePath, errors);
        errors.ThrowIfAny();
        return config;
    }

    public static void Validate(LifeSupportConfig config, string file, DefinitionErrors errors)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(errors);
        var check = new DataChecks(errors, file);

        var metabolic = config.Metabolic;
        check.Positive("$.metabolic.o2KgPerDayAtRest", metabolic.O2KgPerDayAtRest);
        check.Positive("$.metabolic.co2KgPerDayAtRest", metabolic.Co2KgPerDayAtRest);
        check.NotNegative("$.metabolic.heatWAtRest", metabolic.HeatWAtRest);
        var seen = new HashSet<CrewActivity>();
        for (var i = 0; i < metabolic.Activities.Count; i++)
        {
            var level = metabolic.Activities[i];
            var path = $"$.metabolic.activities[{i}]";
            if (!seen.Add(level.Activity)) errors.Add(file, path, $"Activity '{level.Activity}' is listed twice.");
            check.Positive(path + ".o2Multiplier", level.O2Multiplier);
            check.Positive(path + ".co2Multiplier", level.Co2Multiplier);
            check.Positive(path + ".heatMultiplier", level.HeatMultiplier);
        }
        foreach (var activity in Enum.GetValues<CrewActivity>())
        {
            if (!seen.Contains(activity)) errors.Add(file, "$.metabolic.activities", $"Activity '{activity}' has no entry.");
        }

        check.Positive("$.regulator.ppO2SetpointMmhg", config.Regulator.PpO2SetpointMmhg);
        check.Positive("$.regulator.maxFlowKgPerHour", config.Regulator.MaxFlowKgPerHour);

        check.InRange("$.scrubber.efficiency", config.Scrubber.Efficiency, 0, 1);
        check.InRange("$.scrubber.breakthroughFraction", config.Scrubber.BreakthroughFraction, 1e-6, 1);
        check.InRange("$.scrubber.passiveAirflowFraction", config.Scrubber.PassiveAirflowFraction, 0, 1);

        var rations = config.Rations;
        check.Positive("$.rations.referenceKcalPerDay", rations.ReferenceKcalPerDay);
        check.Positive("$.rations.referenceHydrationLPerDay", rations.ReferenceHydrationLPerDay);
        check.NotNegative("$.rations.hygieneLPerDay", rations.HygieneLPerDay);
        CheckTiers(check, errors, file, "$.rations.foodTiersPct", rations.FoodTiersPct);
        CheckTiers(check, errors, file, "$.rations.hydrationTiersPct", rations.HydrationTiersPct);

        check.NotNegative("$.deficits.nutrition.accumulationFactor", config.Deficits.Nutrition.AccumulationFactor);
        check.NotNegative("$.deficits.nutrition.recoveryKcalPerDay", config.Deficits.Nutrition.RecoveryKcalPerDay);
        check.NotNegative("$.deficits.nutrition.recoveryDelayS", config.Deficits.Nutrition.RecoveryDelayS);
        check.NotNegative("$.deficits.hydration.accumulationFactor", config.Deficits.Hydration.AccumulationFactor);
        check.NotNegative("$.deficits.hydration.recoveryLPerDay", config.Deficits.Hydration.RecoveryLPerDay);
        check.NotNegative("$.deficits.hydration.recoveryDelayS", config.Deficits.Hydration.RecoveryDelayS);

        check.Positive("$.thermal.defaultSinkTempK", config.Thermal.DefaultSinkTempK);
        check.InRange("$.thermal.stabilityFraction", config.Thermal.StabilityFraction, 1e-3, 1);
        if (config.Thermal.MaxSubsteps < 1) errors.Add(file, "$.thermal.maxSubsteps", "Must be at least 1.");
    }

    private static void CheckTiers(DataChecks check, DefinitionErrors errors, string file, string path, IReadOnlyList<double> tiers)
    {
        if (tiers.Count == 0)
        {
            errors.Add(file, path, "At least one tier is required.");
            return;
        }
        if (tiers[0] != 100) errors.Add(file, path + "[0]", "The first tier must be 100 (full ration).");
        for (var i = 0; i < tiers.Count; i++)
        {
            check.InRange($"{path}[{i}]", tiers[i], 1e-6, 100);
            if (i > 0 && tiers[i] >= tiers[i - 1]) errors.Add(file, $"{path}[{i}]", "Tiers must be strictly descending.");
        }
    }
}
