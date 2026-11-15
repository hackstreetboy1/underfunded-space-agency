using Usa.Simulation.Kernel;

namespace Usa.Simulation.Flight;

/// <summary>
/// Previews of one burn at many durations (0.1 s steps) for the proposer's duration searches. The longest burn is flown
/// once with a checkpoint every <see cref="CheckpointSteps"/> steps; a shorter one resumes from the checkpoint below it.
/// Up to its end a shorter burn samples the same thrust at the same times from the same state as the longest, so the
/// result is exactly <see cref="ManeuverPlanner.Fly"/> of that duration, for a few steps instead of all of them.
/// </summary>
internal sealed class BurnLadder
{
    private const long CheckpointSteps = 50;

    private readonly ManeuverPlanner _planner;
    private readonly OrbitPropagator _origin;
    private readonly Func<long, ManeuverPlan> _planFor;
    private readonly List<(OrbitPropagator Sim, double PropellantUsedKg)> _checkpoints = [];
    private readonly TimedState _preBurn;

    /// <param name="origin">The vehicle, coasting at or before the burn start; never advanced.</param>
    /// <param name="planFor">The plan for a duration in steps; only the duration may differ between steps.</param>
    public BurnLadder(ManeuverPlanner planner, OrbitPropagator origin, Func<long, ManeuverPlan> planFor, long maxSteps)
    {
        _planner = planner;
        _origin = origin;
        _planFor = planFor;
        var longest = planFor(maxSteps);
        if (longest.Engine.FixedDurationS is not null || longest.StartTime < origin.Time || !ManeuverPlanner.CanPreview(origin)) return;

        var sim = origin.Clone();
        if (!sim.BeginBurn(longest.StartTime, sim.MassKg)) return;
        _preBurn = new TimedState(sim.Time, sim.State);
        var burn = new ProgrammedBurn(longest);
        for (var steps = 0L; steps < maxSteps && sim.Phase == FlightPhase.Powered; steps += CheckpointSteps)
        {
            sim.AdvancePowered(longest.StartTime + steps * OrbitPropagator.IntegrationStepMs, burn, allowPartialStep: false);
            if (sim.Phase != FlightPhase.Powered) break;
            _checkpoints.Add((sim.Clone(), burn.PropellantUsedKg));
        }
    }

    public ManeuverPreview Fly(long steps, PreviewExtent extent)
    {
        var plan = _planFor(steps);
        if (_checkpoints.Count == 0) return _planner.Fly(_origin, plan, extent, out _);
        var (checkpoint, propellantUsedKg) = _checkpoints[(int)Math.Min(steps / CheckpointSteps, _checkpoints.Count - 1)];
        var sim = checkpoint.Clone();
        var burn = new ProgrammedBurn(plan, propellantUsedKg);
        sim.AdvancePowered(plan.EndTime, burn, allowPartialStep: true);
        if (sim.Phase == FlightPhase.Powered) sim.EndBurn();
        return _planner.Finish(sim, plan, burn, burned: true, _preBurn, extent, out _);
    }
}
