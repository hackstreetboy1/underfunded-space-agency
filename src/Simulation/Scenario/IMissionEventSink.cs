using Usa.Simulation.Kernel;

namespace Usa.Simulation.Scenario;

/// <summary>Receives mission events (contract section 6) from tasks, flight, incidents and the integration layer.</summary>
public interface IMissionEventSink
{
    /// <summary>Records the first occurrence of <paramref name="eventId"/>.</summary>
    /// <returns>False when the event had already fired or the mission has ended.</returns>
    /// <exception cref="ArgumentException">The event is not in the mission's event registry.</exception>
    bool RaiseEvent(string eventId, MissionTime now);
}
