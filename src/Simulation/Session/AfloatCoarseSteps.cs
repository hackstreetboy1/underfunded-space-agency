using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Session;

/// <summary>
/// While the crew is afloat waiting for recovery and nothing is in progress (no crew task, burn or pending readout), the clock
/// steps <see cref="SpanTicks"/> ticks at a time: life support, health and the rest integrate over 10 s instead of 50 ms, so the
/// long wait costs a fraction of the CPU and the host can run it in a few real seconds. The last span before recovery is
/// stepped normally, so recovery lands on its exact MET.
/// </summary>
internal sealed class AfloatCoarseSteps(SessionParts parts) : ICoarseStepPolicy
{
    /// <summary>10 s: a multiple of every per-tick and per-second period, and a divisor of the 30 s and 60 s ones.</summary>
    public const int SpanTicks = 200;

    private const long SpanMs = SpanTicks * SimulationClock.BaseTickMs;

    public int CoarseTicks(MissionTime now)
    {
        if (parts.MissionEvents.RecoveryDue is not { } due || now.Milliseconds + SpanMs > due.Milliseconds) return 1;
        if (parts.Flight.Kinematics.Phase != FlightPhase.Landed) return 1;
        if (parts.Runner.Active is not null || parts.Burns.AnyBurn || parts.Flight.ScheduledBurn is not null || parts.Glances.Pending.Count > 0) return 1;
        return SpanTicks;
    }
}
