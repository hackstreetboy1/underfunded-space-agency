using Usa.Simulation.Data;

namespace Usa.Simulation.LifeSupport.Config;

/// <summary>Vehicle-independent life-support tuning (<c>data/config/life_support.json</c>, schema <c>life_support/1</c>).</summary>
public sealed record LifeSupportConfig(
    string Schema,
    MetabolicConfig Metabolic,
    RegulatorConfig Regulator,
    ScrubberConfig Scrubber,
    RationConfig Rations,
    DeficitConfig Deficits,
    ThermalSolverConfig Thermal) : IVersionedDefinition
{
    public const string SchemaTag = "life_support/1";
    public const string RelativePath = "config/life_support.json";
}

/// <summary>Per-crew metabolic reference at rest; activity levels multiply it.</summary>
public sealed record MetabolicConfig(
    double O2KgPerDayAtRest,
    double Co2KgPerDayAtRest,
    double HeatWAtRest,
    IReadOnlyList<ActivityLevel> Activities);

public sealed record ActivityLevel(CrewActivity Activity, double O2Multiplier, double Co2Multiplier, double HeatMultiplier);

/// <param name="PpO2SetpointMmhg">The cabin O2 regulator tops ppO2 up to this value.</param>
public sealed record RegulatorConfig(double PpO2SetpointMmhg, double MaxFlowKgPerHour);

/// <param name="Efficiency">Fraction of the CO2 in the air passing through a fresh canister that is absorbed.</param>
/// <param name="BreakthroughFraction">
/// Remaining-capacity fraction below which removal falls off linearly to zero (1.0 = proportional to remaining capacity).
/// </param>
/// <param name="PassiveAirflowFraction">Convective airflow through the canister with the fan stopped, as a fraction of fan flow.</param>
public sealed record ScrubberConfig(double Efficiency, double BreakthroughFraction, double PassiveAirflowFraction);

/// <param name="FoodTiersPct">Allowed food ration levels, descending, starting at 100.</param>
/// <param name="HydrationTiersPct">Allowed drinking-water ration levels, descending, starting at 100. Hygiene water is not rationed.</param>
public sealed record RationConfig(
    double ReferenceKcalPerDay,
    double ReferenceHydrationLPerDay,
    double HygieneLPerDay,
    IReadOnlyList<double> FoodTiersPct,
    IReadOnlyList<double> HydrationTiersPct);

public sealed record DeficitConfig(NutritionDeficitRule Nutrition, HydrationDeficitRule Hydration);

/// <param name="AccumulationFactor">Deficit kcal added per kcal of shortfall against the reference intake.</param>
/// <param name="RecoveryDelayS">Time on a full, supplied ration before the deficit starts to recover.</param>
public sealed record NutritionDeficitRule(double AccumulationFactor, double RecoveryKcalPerDay, double RecoveryDelayS);

/// <param name="AccumulationFactor">Deficit litres added per litre of shortfall against the reference hydration.</param>
/// <param name="RecoveryDelayS">Time on a full, supplied ration before the deficit starts to recover.</param>
public sealed record HydrationDeficitRule(double AccumulationFactor, double RecoveryLPerDay, double RecoveryDelayS);

/// <param name="DefaultSinkTempK">Effective radiative environment temperature used until an input overrides it.</param>
/// <param name="StabilityFraction">Explicit-Euler sub-step as a fraction of the smallest node time constant.</param>
public sealed record ThermalSolverConfig(double DefaultSinkTempK, double StabilityFraction, int MaxSubsteps);
