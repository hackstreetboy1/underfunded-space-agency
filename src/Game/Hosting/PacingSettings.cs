using Usa.Simulation.Kernel;

namespace Usa.Game.Hosting;

/// <summary>Frame-budget tuning for <see cref="SimulationPacer"/> (SCRIPTING 12.2, 17).</summary>
/// <param name="FrameBudget">Real time the simulation may use per frame.</param>
/// <param name="TicksPerBudgetCheck">Ticks run between clock reads (and compression re-checks).</param>
/// <param name="MaxBacklogMs">MET the host may fall behind its target before dropping the excess, so an
/// over-budget frame lowers the effective rate instead of piling up work for later frames.</param>
/// <param name="EffectiveRateSmoothingRealS">Time constant of the measured-rate average shown to the player.</param>
/// <param name="RecoveryWaitFrameBudget">
/// Real time the simulation may use per frame during the recovery wait (<see cref="Usa.Simulation.Time.TimeCompressionDirector.IsRecoveryWait"/>):
/// the crew is only waiting, so the frame can give the simulation more of itself to finish the wait in a few seconds. The same
/// budget applies while a task or another wait is fast-forwarded (<see cref="Usa.Simulation.Time.TimeCompressionDirector.IsFastForward"/>).
/// </param>
public sealed record PacingSettings(
    TimeSpan FrameBudget,
    int TicksPerBudgetCheck,
    long MaxBacklogMs,
    double EffectiveRateSmoothingRealS,
    TimeSpan RecoveryWaitFrameBudget)
{
    public static readonly PacingSettings Default = new(TimeSpan.FromMilliseconds(2), 16, 1000, 1.0, TimeSpan.FromMilliseconds(10));

    public void Validate()
    {
        if (FrameBudget <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(FrameBudget), FrameBudget, "The frame budget must be positive.");
        if (RecoveryWaitFrameBudget <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(RecoveryWaitFrameBudget), RecoveryWaitFrameBudget, "The recovery-wait frame budget must be positive.");
        if (TicksPerBudgetCheck < 1)
            throw new ArgumentOutOfRangeException(nameof(TicksPerBudgetCheck), TicksPerBudgetCheck, "At least one tick per budget check.");
        if (MaxBacklogMs < SimulationClock.BaseTickMs)
            throw new ArgumentOutOfRangeException(nameof(MaxBacklogMs), MaxBacklogMs, "The backlog must allow at least one tick.");
        if (!(EffectiveRateSmoothingRealS >= 0) || double.IsInfinity(EffectiveRateSmoothingRealS))
            throw new ArgumentOutOfRangeException(nameof(EffectiveRateSmoothingRealS), EffectiveRateSmoothingRealS, "Must be finite and non-negative.");
    }
}
