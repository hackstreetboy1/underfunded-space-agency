namespace Usa.Game.Presentation.ViewModels;

/// <summary>
/// Delivers notifications that throttled observables held back. The host calls <see cref="Pump"/> once per
/// frame (after draining simulation events), so a value that changed inside its interval is still shown once
/// the interval ends instead of waiting for the next change.
/// </summary>
public sealed class UpdateThrottle
{
    private List<IDeferredNotification> _pending = new(64);
    private List<IDeferredNotification> _working = new(64);

    public UpdateThrottle(IMonotonicClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Clock = clock;
    }

    public IMonotonicClock Clock { get; }

    public int PendingCount => _pending.Count;

    /// <summary>Publishes every held-back notification whose interval has passed, in the order they were held.</summary>
    /// <returns>Notifications delivered.</returns>
    public int Pump()
    {
        if (_pending.Count == 0) return 0;

        (_working, _pending) = (_pending, _working);
        var now = Clock.Elapsed;
        var delivered = 0;
        var i = 0;
        try
        {
            for (; i < _working.Count; i++)
            {
                var notification = _working[i];
                if (notification.TryDeliver(now)) delivered++;
                else _pending.Add(notification);
            }
        }
        finally
        {
            // A throwing subscriber must not lose the notifications queued behind it.
            for (var rest = i + 1; rest < _working.Count; rest++) _pending.Add(_working[rest]);
            _working.Clear();
        }
        return delivered;
    }

    internal void Defer(IDeferredNotification notification) => _pending.Add(notification);
}

/// <summary>A held-back change owned by a throttled observable.</summary>
internal interface IDeferredNotification
{
    /// <summary>Publishes the held change if its interval has passed at <paramref name="now"/>.</summary>
    /// <returns>False to stay queued.</returns>
    bool TryDeliver(TimeSpan now);
}
