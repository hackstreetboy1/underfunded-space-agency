namespace Usa.Simulation.Crew.Tasks;

/// <summary>How a task establishes a fact; only inspection and component-specific test can confirm a component.</summary>
public enum TaskObservationMethod
{
    Glance,
    Instrument,
    Inspection,
    Test,
}
