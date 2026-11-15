namespace Usa.Simulation.Kernel;

/// <summary>A simulation system stepped by <see cref="SimulationClock"/> at a fixed period.</summary>
public interface ISimSystem
{
    /// <summary>Run period in base ticks (1 = every 50 ms, 20 = every second).</summary>
    int PeriodTicks { get; }

    void Step(in StepContext context);
}

/// <param name="Now">Mission time at the end of this step.</param>
/// <param name="DeltaSeconds">Time covered by this step for this system (its period).</param>
public readonly record struct StepContext(MissionTime Now, double DeltaSeconds, long TickIndex);

[Flags]
public enum FreezeReason
{
    None = 0,
    Menu = 1,
    ServiceOutage = 2,
    Loading = 4,
}

/// <summary>
/// Fixed-step scheduler. Every system runs on whole multiples of <see cref="BaseTickMs"/>, so the
/// result is identical no matter how many ticks the host runs per frame (time compression).
/// </summary>
public sealed class SimulationClock
{
    public const long BaseTickMs = 50;
    public const int TicksPerSecond = 20;

    private readonly ISimSystem[] _systems;
    private long _tick;
    private int _checkedSpan;

    public SimulationClock(IEnumerable<ISimSystem> systemsInOrder, MissionTime start)
    {
        _systems = systemsInOrder.ToArray();
        foreach (var system in _systems)
        {
            if (system.PeriodTicks < 1)
                throw new ArgumentException($"{system.GetType().Name} has a period below one tick.", nameof(systemsInOrder));
        }
        Now = start;
    }

    public MissionTime Now { get; private set; }

    public long TickIndex => _tick;

    /// <summary>Decides when ticks may be merged into one coarse step; null runs every tick.</summary>
    public ICoarseStepPolicy? CoarseSteps { get; set; }

    public bool IsFrozen => FreezeReasons != FreezeReason.None;

    public FreezeReason FreezeReasons { get; private set; }

    public void Freeze(FreezeReason reason) => FreezeReasons |= reason;

    public void Unfreeze(FreezeReason reason) => FreezeReasons &= ~reason;

    /// <summary>Runs whole ticks up to <paramref name="target"/>, at most <paramref name="maxTicks"/>.</summary>
    /// <returns>Ticks actually run. Fewer than requested means the target was reached or the cap hit.</returns>
    /// <remarks>
    /// With a <see cref="CoarseSteps"/> policy, a coarse step covers its whole span (aligned to multiples of it) even past
    /// <paramref name="target"/> or <paramref name="maxTicks"/>, so the steps never depend on how the host chunks them.
    /// </remarks>
    public int AdvanceTo(MissionTime target, int maxTicks)
    {
        if (IsFrozen) return 0;

        var run = 0;
        while (run < maxTicks && Now.Milliseconds + BaseTickMs <= target.Milliseconds)
        {
            var span = CoarseSpan();
            if (span > 1)
            {
                StepCoarse(span);
                run += span;
                continue;
            }

            _tick++;
            Now += BaseTickMs;
            for (var i = 0; i < _systems.Length; i++)
            {
                var system = _systems[i];
                if (_tick % system.PeriodTicks != 0) continue;
                system.Step(new StepContext(Now, system.PeriodTicks * BaseTickMs / 1000.0, _tick));
            }
            run++;
        }
        return run;
    }

    private int CoarseSpan()
    {
        if (CoarseSteps is null) return 1;
        var span = CoarseSteps.CoarseTicks(Now);
        if (span <= 1 || _tick % span != 0) return 1;
        if (span != _checkedSpan)
        {
            foreach (var system in _systems)
            {
                var period = system.PeriodTicks;
                if (period <= span ? span % period != 0 : period % span != 0)
                    throw new InvalidOperationException($"Coarse span {span} does not fit {system.GetType().Name}'s period {period}.");
            }
            _checkedSpan = span;
        }
        return span;
    }

    /// <summary>One step of <paramref name="span"/> ticks: every system with a shorter period runs once over the whole span.</summary>
    private void StepCoarse(int span)
    {
        _tick += span;
        Now += span * BaseTickMs;
        var spanS = span * BaseTickMs / 1000.0;
        for (var i = 0; i < _systems.Length; i++)
        {
            var system = _systems[i];
            if (system.PeriodTicks <= span) system.Step(new StepContext(Now, spanS, _tick));
            else if (_tick % system.PeriodTicks == 0) system.Step(new StepContext(Now, system.PeriodTicks * BaseTickMs / 1000.0, _tick));
        }
    }
}
