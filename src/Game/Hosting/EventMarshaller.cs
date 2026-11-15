using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Kernel;

namespace Usa.Game.Hosting;

/// <summary>
/// Drains simulation events once per frame and hands them to typed handlers (SCRIPTING 12.2).
/// </summary>
/// <remarks>
/// <para>Order is deterministic: events in publish order; for each event, every handler whose registered type the
/// event is assignable to, in registration order. Throttled handlers receive the latest event per key at most once
/// per interval, delivered at the end of <see cref="Flush"/> in registration order, keys in the order they first
/// became pending.</para>
/// <para>Dispatch does not allocate once each event type has been seen: handler arrays are resolved per runtime type
/// and cached until registrations change. A throwing handler does not stop the others; failures go to the error
/// callback, or are rethrown together after the drain completes so the session's buffer is still cleared.</para>
/// </remarks>
public sealed class EventMarshaller
{
    private readonly IMonotonicClock _clock;
    private readonly Action<Exception, ISimEvent>? _onHandlerError;
    private readonly List<Registration> _registrations = [];
    private readonly List<ThrottledRegistration> _throttled = [];
    private readonly Dictionary<Type, Registration[]> _resolved = [];
    private readonly Action<ISimEvent> _dispatch;
    private List<Exception>? _failures;
    private long _nextSequence;

    /// <param name="clock">Real time for throttled handlers.</param>
    /// <param name="onHandlerError">Receives handler exceptions (with the event being handled);
    /// when null they are collected and rethrown as an <see cref="AggregateException"/> after the flush.</param>
    public EventMarshaller(IMonotonicClock clock, Action<Exception, ISimEvent>? onHandlerError = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
        _onHandlerError = onHandlerError;
        _dispatch = Dispatch;
    }

    /// <summary>Events dispatched since the last <see cref="Flush"/> started.</summary>
    public int LastFlushEventCount { get; private set; }

    /// <summary>Calls <paramref name="handler"/> for every event assignable to <typeparamref name="TEvent"/>.</summary>
    /// <returns>Dispose to unregister; safe during dispatch (the handler is skipped from then on).</returns>
    public IDisposable Register<TEvent>(Action<TEvent> handler)
        where TEvent : ISimEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Add(new DirectRegistration<TEvent>(this, typeof(TEvent), _nextSequence++, handler));
    }

    /// <summary>Delivers only the latest <typeparamref name="TEvent"/>, at most once per <paramref name="minInterval"/>.</summary>
    public IDisposable RegisterThrottled<TEvent>(Action<TEvent> handler, TimeSpan minInterval)
        where TEvent : ISimEvent =>
        RegisterThrottled(handler, static _ => 0, minInterval);

    /// <summary>
    /// Delivers the latest <typeparamref name="TEvent"/> per key, all pending keys at most once per
    /// <paramref name="minInterval"/> (e.g. one resource update per component every 100 ms).
    /// </summary>
    public IDisposable RegisterThrottled<TEvent, TKey>(Action<TEvent> handler, Func<TEvent, TKey> keySelector, TimeSpan minInterval)
        where TEvent : ISimEvent
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentOutOfRangeException.ThrowIfLessThan(minInterval, TimeSpan.Zero);
        var registration = new ThrottledRegistration<TEvent, TKey>(this, typeof(TEvent), _nextSequence++, handler, keySelector, minInterval);
        _throttled.Add(registration);
        return Add(registration);
    }

    /// <summary>Drains the session's events, dispatches them, then delivers due throttled handlers.</summary>
    /// <returns>Events drained.</returns>
    public int Flush(ISimulationSessionPort session)
    {
        ArgumentNullException.ThrowIfNull(session);
        LastFlushEventCount = 0;
        session.DrainEvents(_dispatch);
        PumpThrottled();
        ThrowCollectedFailures();
        return LastFlushEventCount;
    }

    /// <summary>Dispatches one event immediately (replays, tests). Throttled handlers deliver on the next flush.</summary>
    public void Dispatch(ISimEvent simEvent)
    {
        ArgumentNullException.ThrowIfNull(simEvent);
        LastFlushEventCount++;
        var handlers = Resolve(simEvent.GetType());
        for (var i = 0; i < handlers.Length; i++)
        {
            var registration = handlers[i];
            if (!registration.IsActive) continue;
            try
            {
                registration.Deliver(simEvent);
            }
            catch (Exception exception)
            {
                Fail(exception, simEvent);
            }
        }
    }

    private void PumpThrottled()
    {
        var now = _clock.Elapsed;
        for (var i = 0; i < _throttled.Count; i++)
        {
            var registration = _throttled[i];
            if (!registration.IsActive) continue;
            registration.Pump(now);
        }
    }

    private Registration Add(Registration registration)
    {
        _registrations.Add(registration);
        _resolved.Clear();
        return registration;
    }

    private void Remove(Registration registration)
    {
        _registrations.Remove(registration);
        if (registration is ThrottledRegistration throttled) _throttled.Remove(throttled);
        _resolved.Clear();
    }

    private Registration[] Resolve(Type eventType)
    {
        if (_resolved.TryGetValue(eventType, out var cached)) return cached;
        var matches = new List<Registration>();
        foreach (var registration in _registrations)
        {
            if (registration.EventType.IsAssignableFrom(eventType)) matches.Add(registration);
        }
        matches.Sort(static (a, b) => a.Sequence.CompareTo(b.Sequence));
        var resolved = matches.ToArray();
        _resolved[eventType] = resolved;
        return resolved;
    }

    private void Fail(Exception exception, ISimEvent simEvent)
    {
        if (_onHandlerError is not null)
        {
            _onHandlerError(exception, simEvent);
            return;
        }
        (_failures ??= []).Add(exception);
    }

    private void ThrowCollectedFailures()
    {
        if (_failures is not { Count: > 0 } failures) return;
        _failures = null;
        throw new AggregateException("One or more simulation event handlers failed.", failures);
    }

    private abstract class Registration(EventMarshaller owner, Type eventType, long sequence) : IDisposable
    {
        public Type EventType { get; } = eventType;

        public long Sequence { get; } = sequence;

        public bool IsActive { get; private set; } = true;

        protected EventMarshaller Owner { get; } = owner;

        public abstract void Deliver(ISimEvent simEvent);

        public void Dispose()
        {
            if (!IsActive) return;
            IsActive = false;
            Owner.Remove(this);
        }
    }

    private sealed class DirectRegistration<TEvent>(EventMarshaller owner, Type eventType, long sequence, Action<TEvent> handler)
        : Registration(owner, eventType, sequence)
        where TEvent : ISimEvent
    {
        public override void Deliver(ISimEvent simEvent) => handler((TEvent)simEvent);
    }

    private abstract class ThrottledRegistration(EventMarshaller owner, Type eventType, long sequence)
        : Registration(owner, eventType, sequence)
    {
        public abstract void Pump(TimeSpan now);
    }

    private sealed class ThrottledRegistration<TEvent, TKey>(
        EventMarshaller owner,
        Type eventType,
        long sequence,
        Action<TEvent> handler,
        Func<TEvent, TKey> keySelector,
        TimeSpan minInterval)
        : ThrottledRegistration(owner, eventType, sequence)
        where TEvent : ISimEvent
        where TKey : notnull
    {
        private readonly Dictionary<TKey, TEvent> _latest = [];
        private readonly List<TKey> _order = [];
        private TimeSpan? _lastDeliveredAt;

        public override void Deliver(ISimEvent simEvent)
        {
            var typed = (TEvent)simEvent;
            var key = keySelector(typed);
            if (!_latest.ContainsKey(key)) _order.Add(key);
            _latest[key] = typed;
        }

        public override void Pump(TimeSpan now)
        {
            if (_order.Count == 0) return;
            if (_lastDeliveredAt is { } last && now - last < minInterval) return;
            _lastDeliveredAt = now;

            for (var i = 0; i < _order.Count && IsActive; i++)
            {
                var simEvent = _latest[_order[i]];
                try
                {
                    handler(simEvent);
                }
                catch (Exception exception)
                {
                    Owner.Fail(exception, simEvent);
                }
            }
            _order.Clear();
            _latest.Clear();
        }
    }
}
