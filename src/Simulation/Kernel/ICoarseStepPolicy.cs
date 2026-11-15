namespace Usa.Simulation.Kernel;

/// <summary>
/// Lets <see cref="SimulationClock"/> run several base ticks as one step while nothing fast is happening (the crew afloat waiting
/// for recovery). The answer must depend on simulation state only, never on the host, so every run of a session takes the same
/// steps however its host chunks the ticks.
/// </summary>
public interface ICoarseStepPolicy
{
    /// <summary>Base ticks the next step may cover: 1 for a normal tick; otherwise a span every period divides or is a multiple of.</summary>
    int CoarseTicks(MissionTime now);
}
