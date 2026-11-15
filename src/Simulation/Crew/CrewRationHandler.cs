using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Crew;

/// <summary>
/// Accepts effects on <c>crew.ration.food_pct</c> and <c>crew.ration.water_pct</c> (set, add, multiply),
/// clamped to 0-100.
/// </summary>
/// <remarks>
/// Provisions does not read these values; the session copies them into the provisions ration plan.
/// Data writes <c>provisions.*_ration_pct</c> directly, so this store is slated for removal once the session
/// stops copying it.
/// </remarks>
public sealed class CrewRationHandler(CrewRoster roster) : IEffectHandler
{
    private const string FoodPath = "crew.ration.food_pct";
    private const string WaterPath = "crew.ration.water_pct";

    public string Prefix => "crew.ration.";

    public bool CanApply(string target, EffectOperation operation) =>
        target is FoodPath or WaterPath && Enum.IsDefined(operation);

    /// <exception cref="ArgumentOutOfRangeException">The value or the result is NaN or infinite.</exception>
    public void Apply(string target, EffectOperation operation, StateValue value, MissionTime now)
    {
        var current = target switch
        {
            FoodPath => roster.FoodRationPct,
            WaterPath => roster.WaterRationPct,
            _ => throw new UnknownStatePathException(target),
        };
        var operand = value.AsNumber();
        if (!double.IsFinite(operand))
            throw new ArgumentOutOfRangeException(nameof(value), operand, $"Effect on '{target}' must be a finite number.");
        var next = operation switch
        {
            EffectOperation.Set => operand,
            EffectOperation.Add => current + operand,
            EffectOperation.Multiply => current * operand,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };
        if (!double.IsFinite(next))
            throw new ArgumentOutOfRangeException(nameof(value), next, $"Effect on '{target}' gives a non-finite result.");
        next = Math.Clamp(next, 0.0, CrewRoster.FullRationPct);
        if (target == FoodPath) roster.FoodRationPct = next;
        else roster.WaterRationPct = next;
    }
}
