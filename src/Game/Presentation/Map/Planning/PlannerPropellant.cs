using Usa.Simulation.Flight;

namespace Usa.Game.Presentation.Map.Planning;

/// <summary>
/// Knowledge-derived propellant figures for the planner (X8): the preview runs on the propellant the ground knows,
/// and the remaining Δv is computed from it. Only the vehicle's mass without this engine's tank comes from the
/// simulation, as preview physics.
/// </summary>
internal static class PlannerPropellant
{
    private const double NegligibleKg = 1e-6;

    /// <summary>Propellant the planned burn needs at full duration, from the engine's specification.</summary>
    public static double PlannedUseKg(BurnEngine engine, double effectiveDurationS) => engine switch
    {
        ConstantThrustEngine constant => constant.MassFlowKgPerS * Math.Max(0, effectiveDurationS),
        _ => engine.AvailablePropellantKg,
    };

    /// <summary>
    /// The engine the preview and proposals fly: the known amount (the low end of a band); with nothing known, enough
    /// for the whole burn, so the true tank never shapes the preview.
    /// </summary>
    public static BurnEngine ForPlanning(PlannerEngineOption option, double effectiveDurationS)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (option.Engine is not ConstantThrustEngine constant || option.KnownPropellant is not { } known) return option.Engine!;
        var kg = known.HasValue ? known.PlanningKg : PlannedUseKg(constant, effectiveDurationS) + 1;
        return constant with { PropellantKg = kg };
    }

    /// <summary>Δv the propellant left after the burn could still give.</summary>
    /// <param name="massWithoutTankKg">Vehicle mass without this engine's propellant.</param>
    public static double RemainingDeltaVMps(double exhaustVelocityMps, double massWithoutTankKg, double onBoardKg, double usedKg)
    {
        var left = Math.Max(0, onBoardKg - usedKg);
        if (left <= NegligibleKg || massWithoutTankKg <= 0 || !double.IsFinite(massWithoutTankKg)) return 0;
        return exhaustVelocityMps * Math.Log((massWithoutTankKg + left) / massWithoutTankKg);
    }
}
