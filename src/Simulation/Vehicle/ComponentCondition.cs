namespace Usa.Simulation.Vehicle;

/// <summary>Actual component condition. <see cref="Isolated"/> means deliberately disconnected; "suspected" exists only in knowledge.</summary>
public enum ComponentCondition
{
    Nominal,
    Degraded,
    Failed,
    Isolated,
}
