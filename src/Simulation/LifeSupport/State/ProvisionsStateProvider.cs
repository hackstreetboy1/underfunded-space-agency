using Usa.Simulation.Kernel;
using Usa.Simulation.LifeSupport.Consumables;
using Usa.Simulation.Ports;

namespace Usa.Simulation.LifeSupport.State;

/// <summary>
/// Food and water at <c>provisions.&lt;field&gt;</c> and per crew at <c>provisions.&lt;crewId&gt;.&lt;field&gt;</c>.
/// <para>Stores:</para>
/// <list type="table">
/// <item><term>food_kcal, water_l</term><description>Inventories. Effects: set/add/multiply.</description></item>
/// <item><term>food_pct, water_pct</term><description>Percent of the starting inventory.</description></item>
/// <item><term>food_use_kcal_per_day, water_use_l_per_day, water_income_l_per_day</term><description>Rates over the last step.</description></item>
/// <item><term>food_ration_pct, water_ration_pct</term><description>Effect only (set): ration for every crew member, snapped to a tier.</description></item>
/// </list>
/// <para>Per crew:</para>
/// <list type="table">
/// <item><term>nutrition_deficit, hydration_deficit</term><description>Deficit in days of reference intake (health model input).</description></item>
/// <item><term>nutrition_deficit_kcal, hydration_deficit_l</term><description>The same in physical units.</description></item>
/// <item><term>food_ration_pct, water_ration_pct</term><description>Current ration tier. Effects: set (snapped to a tier).</description></item>
/// </list>
/// </summary>
public sealed class ProvisionsStateProvider(Provisions provisions) : IStateProvider, IEffectHandler
{
    public const string PathPrefix = "provisions.";

    private readonly Provisions _provisions = provisions ?? throw new ArgumentNullException(nameof(provisions));

    public string Prefix => PathPrefix;

    public bool IsKnownPath(string path) => TryGet(path, out _);

    public bool TryGet(string path, out StateValue value)
    {
        value = StateValue.None;
        if (!StatePathParts.TrySplit(path, PathPrefix, out var owner, out var field)) return false;

        if (owner.IsEmpty)
        {
            var p = _provisions;
            switch (field)
            {
                case "food_kcal": value = p.FoodKcal; return true;
                case "water_l": value = p.WaterL; return true;
                case "food_pct": value = p.InitialFoodKcal > 0 ? p.FoodKcal / p.InitialFoodKcal * 100.0 : 0.0; return true;
                case "water_pct": value = p.InitialWaterL > 0 ? p.WaterL / p.InitialWaterL * 100.0 : 0.0; return true;
                case "food_use_kcal_per_day": value = p.FoodUseKcalPerS * 86_400.0; return true;
                case "water_use_l_per_day": value = p.WaterUseLPerS * 86_400.0; return true;
                case "water_income_l_per_day": value = p.WaterIncomeLPerS * 86_400.0; return true;
                default: return false;
            }
        }

        var crew = FindCrew(owner);
        if (crew < 0) return false;
        switch (field)
        {
            case "nutrition_deficit": value = _provisions.NutritionDeficitDays(crew); return true;
            case "hydration_deficit": value = _provisions.HydrationDeficitDays(crew); return true;
            case "nutrition_deficit_kcal": value = _provisions.NutritionDeficitKcal(crew); return true;
            case "hydration_deficit_l": value = _provisions.HydrationDeficitL(crew); return true;
            case "food_ration_pct": value = _provisions.RationOf(crew).FoodPct; return true;
            case "water_ration_pct": value = _provisions.RationOf(crew).WaterPct; return true;
            default: return false;
        }
    }

    public bool CanApply(string target, EffectOperation operation)
    {
        if (!StatePathParts.TrySplit(target, PathPrefix, out var owner, out var field)) return false;
        var isRation = field.SequenceEqual("food_ration_pct") || field.SequenceEqual("water_ration_pct");
        if (owner.IsEmpty)
        {
            return isRation
                ? operation == EffectOperation.Set
                : field.SequenceEqual("food_kcal") || field.SequenceEqual("water_l");
        }
        return isRation && operation == EffectOperation.Set && FindCrew(owner) >= 0;
    }

    public void Apply(string target, EffectOperation operation, StateValue value, MissionTime now)
    {
        if (!CanApply(target, operation)) throw new UnknownStatePathException(target);
        StatePathParts.TrySplit(target, PathPrefix, out var owner, out var field);
        var number = value.AsNumber();
        if (!double.IsFinite(number))
            throw new ArgumentOutOfRangeException(nameof(value), number, $"Effect on '{target}' needs a finite number, got {number}.");

        if (field.SequenceEqual("food_kcal"))
        {
            _provisions.SetFoodKcal(StatePathParts.Combine(target, operation, _provisions.FoodKcal, number));
            return;
        }
        if (field.SequenceEqual("water_l"))
        {
            _provisions.SetWaterL(StatePathParts.Combine(target, operation, _provisions.WaterL, number));
            return;
        }

        var food = field.SequenceEqual("food_ration_pct");
        if (!owner.IsEmpty)
        {
            SetRationPart(FindCrew(owner), food, number);
            return;
        }
        for (var i = 0; i < _provisions.CrewCount; i++) SetRationPart(i, food, number);
    }

    private void SetRationPart(int crew, bool food, double pct)
    {
        var plan = _provisions.RationOf(crew);
        _provisions.SetRation(crew, food ? plan with { FoodPct = pct } : plan with { WaterPct = pct });
    }

    private int FindCrew(ReadOnlySpan<char> id)
    {
        for (var i = 0; i < _provisions.CrewCount; i++)
        {
            if (id.SequenceEqual(_provisions.CrewAt(i).Value)) return i;
        }
        return -1;
    }
}
