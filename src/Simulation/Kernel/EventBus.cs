namespace Usa.Simulation.Kernel;

/// <summary>An immutable fact about a state change, published during a step.</summary>
/// <remarks>Each area defines its own event records next to the code that raises them.</remarks>
public interface ISimEvent
{
    MissionTime Time { get; }
}

/// <summary>
/// Buffers events published during simulation steps. Subscribers inside the simulation see events
/// immediately; the host drains the buffer once per frame for presentation.
/// </summary>
public sealed class EventBus
{
    private readonly List<ISimEvent> _pending = new(256);
    private readonly List<Action<ISimEvent>> _subscribers = [];

    /// <summary>Registers an in-simulation listener, called synchronously in publish order.</summary>
    public void Subscribe(Action<ISimEvent> subscriber) => _subscribers.Add(subscriber);

    public void Publish(ISimEvent simEvent)
    {
        ArgumentNullException.ThrowIfNull(simEvent);
        _pending.Add(simEvent);
        for (var i = 0; i < _subscribers.Count; i++) _subscribers[i](simEvent);
    }

    /// <summary>Hands buffered events to <paramref name="sink"/> in publish order and clears the buffer.</summary>
    public void Drain(Action<ISimEvent> sink)
    {
        for (var i = 0; i < _pending.Count; i++) sink(_pending[i]);
        _pending.Clear();
    }

    public int PendingCount => _pending.Count;
}
