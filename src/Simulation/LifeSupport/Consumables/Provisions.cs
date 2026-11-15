using Usa.Simulation.Data;
using Usa.Simulation.Kernel;
using Usa.Simulation.LifeSupport.Config;

namespace Usa.Simulation.LifeSupport.Consumables;

/// <summary>
/// Food and potable-water inventories with per-crew ration plans. Any shortfall against the reference intake
/// (from rationing or empty stores) accumulates as a per-crew deficit; a deficit recovers slowly, and only after
/// the crew member has been on a full, supplied ration for the configured delay. Crew are indexed in id order.
/// When stores run short, food and drinking water are shared pro rata to each ration, and every crew member drinks
/// before any hygiene water is used.
/// </summary>
public sealed class Provisions : IHashContributor
{
    private const double Epsilon = 1e-12;

    private readonly RationConfig _rations;
    private readonly DeficitConfig _deficits;
    private readonly CrewId[] _crew;
    private readonly RationPlan[] _plans;
    private readonly double[] _nutritionDeficitKcal;
    private readonly double[] _hydrationDeficitL;
    private readonly double[] _fullFoodS;
    private readonly double[] _fullWaterS;

    public Provisions(ProvisionsSpec spec, IEnumerable<CrewId> crew, RationConfig rations, DeficitConfig deficits)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(crew);
        ArgumentNullException.ThrowIfNull(rations);
        ArgumentNullException.ThrowIfNull(deficits);
        var errors = new DefinitionErrors();
        spec.Validate(errors, "provisions");
        errors.ThrowIfAny();

        _rations = rations;
        _deficits = deficits;
        _crew = crew.Distinct().Order().ToArray();
        _plans = Enumerable.Repeat(RationPlan.Full, _crew.Length).ToArray();
        _nutritionDeficitKcal = new double[_crew.Length];
        _hydrationDeficitL = new double[_crew.Length];
        _fullFoodS = new double[_crew.Length];
        _fullWaterS = new double[_crew.Length];
        FoodKcal = InitialFoodKcal = spec.FoodKcal;
        WaterL = InitialWaterL = spec.WaterL;
        WaterCapacityL = spec.WaterCapacityL;
    }

    public double FoodKcal { get; private set; }

    public double WaterL { get; private set; }

    public double InitialFoodKcal { get; }

    public double InitialWaterL { get; }

    public double WaterCapacityL { get; }

    public int CrewCount => _crew.Length;

    /// <summary>Food eaten per second over the last step (all crew).</summary>
    public double FoodUseKcalPerS { get; private set; }

    /// <summary>Drinking plus hygiene water used per second over the last step.</summary>
    public double WaterUseLPerS { get; private set; }

    public double WaterIncomeLPerS { get; private set; }

    public CrewId CrewAt(int index) => _crew[index];

    /// <returns>The crew index, or -1.</returns>
    public int IndexOf(CrewId crew)
    {
        var index = Array.BinarySearch(_crew, crew);
        return index >= 0 ? index : -1;
    }

    public RationPlan RationOf(int crew) => _plans[crew];

    /// <summary>Sets a plan, snapping each percentage to the nearest configured tier.</summary>
    public void SetRation(int crew, RationPlan plan) =>
        _plans[crew] = new RationPlan(
            SnapToTier(_rations.FoodTiersPct, Finite.Require(plan.FoodPct, nameof(plan.FoodPct))),
            SnapToTier(_rations.HydrationTiersPct, Finite.Require(plan.WaterPct, nameof(plan.WaterPct))));

    public double NutritionDeficitKcal(int crew) => _nutritionDeficitKcal[crew];

    public double HydrationDeficitL(int crew) => _hydrationDeficitL[crew];

    /// <summary>Nutrition deficit in days of reference intake (dimensionless input for the health model).</summary>
    public double NutritionDeficitDays(int crew) => _nutritionDeficitKcal[crew] / _rations.ReferenceKcalPerDay;

    /// <summary>Hydration deficit in days of reference drinking water.</summary>
    public double HydrationDeficitDays(int crew) => _hydrationDeficitL[crew] / _rations.ReferenceHydrationLPerDay;

    public void SetFoodKcal(double kcal) => FoodKcal = Math.Max(0, Finite.Require(kcal, nameof(kcal)));

    public void SetWaterL(double litres) => WaterL = Math.Clamp(Finite.Require(litres, nameof(litres)), 0, WaterCapacityL);

    /// <param name="crewConsuming">Per crew index: true while alive and eating/drinking from the vehicle stores.</param>
    /// <param name="waterIncomeL">Potable water produced this step (fuel cells).</param>
    public ProvisionsStepResult Step(double deltaSeconds, ReadOnlySpan<bool> crewConsuming, double waterIncomeL)
    {
        if (crewConsuming.Length != _crew.Length)
            throw new ArgumentException($"Expected {_crew.Length} crew flags.", nameof(crewConsuming));
        if (deltaSeconds <= 0) return default;

        var referenceKcal = _rations.ReferenceKcalPerDay / GasConstants.SecondsPerDay * deltaSeconds;
        var referenceDrinkL = _rations.ReferenceHydrationLPerDay / GasConstants.SecondsPerDay * deltaSeconds;
        var hygieneL = _rations.HygieneLPerDay / GasConstants.SecondsPerDay * deltaSeconds;
        var nutrition = _deficits.Nutrition;
        var hydration = _deficits.Hydration;
        var nutritionRecoveryKcal = nutrition.RecoveryKcalPerDay / GasConstants.SecondsPerDay * deltaSeconds;
        var hydrationRecoveryL = hydration.RecoveryLPerDay / GasConstants.SecondsPerDay * deltaSeconds;
        // A shortfall is shared pro rata, never first-come by crew id, and every crew member drinks before anyone washes.
        double foodWanted = 0, drinkWanted = 0, hygieneWanted = 0;
        for (var i = 0; i < _crew.Length; i++)
        {
            if (!crewConsuming[i]) continue;
            foodWanted += referenceKcal * _plans[i].FoodPct / 100.0;
            drinkWanted += referenceDrinkL * _plans[i].WaterPct / 100.0;
            hygieneWanted += hygieneL;
        }

        var foodServed = ServedFraction(foodWanted, FoodKcal);
        var eatenTotal = foodWanted * foodServed;
        FoodKcal = Math.Max(0, FoodKcal - eatenTotal);
        var drinkServed = ServedFraction(drinkWanted, WaterL);
        var drunkTotal = drinkWanted * drinkServed;
        WaterL = Math.Max(0, WaterL - drunkTotal);
        var hygieneServed = ServedFraction(hygieneWanted, WaterL);
        var washedTotal = hygieneWanted * hygieneServed;
        WaterL = Math.Max(0, WaterL - washedTotal);
        var waterUsedTotal = drunkTotal + washedTotal;

        for (var i = 0; i < _crew.Length; i++)
        {
            if (!crewConsuming[i]) continue;
            var eaten = referenceKcal * _plans[i].FoodPct / 100.0 * foodServed;
            UpdateDeficit(
                ref _nutritionDeficitKcal[i], ref _fullFoodS[i], referenceKcal - eaten, referenceKcal,
                nutrition.AccumulationFactor, nutritionRecoveryKcal, nutrition.RecoveryDelayS, deltaSeconds);

            var drunk = referenceDrinkL * _plans[i].WaterPct / 100.0 * drinkServed;
            UpdateDeficit(
                ref _hydrationDeficitL[i], ref _fullWaterS[i], referenceDrinkL - drunk, referenceDrinkL,
                hydration.AccumulationFactor, hydrationRecoveryL, hydration.RecoveryDelayS, deltaSeconds);
        }

        var income = Math.Max(0, Finite.Require(waterIncomeL, nameof(waterIncomeL)));
        var added = Math.Min(income, WaterCapacityL - WaterL);
        WaterL += added;

        FoodUseKcalPerS = eatenTotal / deltaSeconds;
        WaterUseLPerS = waterUsedTotal / deltaSeconds;
        WaterIncomeLPerS = income / deltaSeconds;
        return new ProvisionsStepResult(eatenTotal, waterUsedTotal, added, income - added);
    }

    public void WriteHash(StateHashWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(FoodKcal);
        writer.Write(WaterL);
        for (var i = 0; i < _crew.Length; i++)
        {
            writer.Write(_crew[i].Value);
            writer.Write(_plans[i].FoodPct);
            writer.Write(_plans[i].WaterPct);
            writer.Write(_nutritionDeficitKcal[i]);
            writer.Write(_hydrationDeficitL[i]);
            writer.Write(_fullFoodS[i]);
            writer.Write(_fullWaterS[i]);
        }
    }

    private static double ServedFraction(double wanted, double available) =>
        wanted > available ? Math.Max(0, available) / wanted : 1;

    private static void UpdateDeficit(
        ref double deficit, ref double fullRationS, double shortfall, double reference,
        double accumulationFactor, double recovery, double recoveryDelayS, double deltaSeconds)
    {
        if (shortfall > reference * 1e-9 + Epsilon)
        {
            deficit += shortfall * accumulationFactor;
            fullRationS = 0;
            return;
        }

        fullRationS += deltaSeconds;
        if (fullRationS >= recoveryDelayS) deficit = Math.Max(0, deficit - recovery);
    }

    private static double SnapToTier(IReadOnlyList<double> tiers, double pct)
    {
        var best = tiers[0];
        for (var i = 1; i < tiers.Count; i++)
        {
            if (Math.Abs(tiers[i] - pct) < Math.Abs(best - pct)) best = tiers[i];
        }
        return best;
    }
}
