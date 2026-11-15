using Usa.Simulation.Ports;

namespace Usa.Simulation.Crew.Tasks;

/// <summary>
/// Receives every task ending. The runner never mutates other areas' state; integration applies
/// <see cref="CrewTaskOutcome.Effects"/> through the <see cref="EffectRouter"/> and records
/// <see cref="CrewTaskOutcome.Observations"/> with the assignee as observer.
/// </summary>
public interface ICrewTaskOutcomeSink
{
    void OnTaskEnded(CrewTaskOutcome outcome);
}
