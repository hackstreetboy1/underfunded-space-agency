using Usa.Simulation.Flight;

namespace Usa.Game.Presentation.Map.Planning;

/// <param name="MaxDurationS">Longest manual burn (mission <c>planning.maxBurnDurationS</c>).</param>
/// <param name="DurationStepS">Durations snap to this step (the integrator's 0.1 s grid).</param>
/// <param name="PreviewIntervalS">Previews run at most this often (UI.md 6.4: 10 Hz).</param>
/// <param name="ProposalsEnabled">Easy difficulty: "Propose burn" is offered.</param>
public sealed record BurnPlannerSettings(
    double MaxDurationS = 600,
    double DurationStepS = 0.1,
    double PreviewIntervalS = 0.1,
    bool ProposalsEnabled = false)
{
    public static BurnPlannerSettings From(PlanningSettings planning, bool proposalsEnabled)
    {
        ArgumentNullException.ThrowIfNull(planning);
        return new BurnPlannerSettings(planning.MaxBurnDurationS, ProposalsEnabled: proposalsEnabled);
    }
}
