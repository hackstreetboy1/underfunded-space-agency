using Usa.Simulation.Kernel;

namespace Usa.Simulation.Forecast;

/// <summary>
/// <see cref="ForecastInput.PlanEnd"/> for a predicted landing (X8): the crew is recovered after the worst recovery wait
/// anywhere inside the landing arc, not the wait at its centre, so a landing near a zone edge forecasts the open-sea
/// wait. Flight supplies the worst wait (<c>LandingDispersion.WorstRecoveryWaitHours</c>, or the zone's own wait for a
/// preview without dispersion).
/// </summary>
public static class RecoveryPlanEnd
{
    public static MissionTime After(MissionTime landing, double worstRecoveryWaitHours)
    {
        if (!(worstRecoveryWaitHours >= 0) || double.IsInfinity(worstRecoveryWaitHours))
            throw new ArgumentOutOfRangeException(nameof(worstRecoveryWaitHours), worstRecoveryWaitHours, "Must be finite and not negative.");
        return landing + (long)Math.Round(worstRecoveryWaitHours * 3_600_000);
    }
}
