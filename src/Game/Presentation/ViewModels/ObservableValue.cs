using System.Globalization;

namespace Usa.Game.Presentation.ViewModels;

/// <summary>
/// A view-model value that notifies subscribers when it changes. Setting an equal value (per the comparer) is a
/// no-op. With a throttle, notifications are published at most once per <see cref="MinInterval"/>: the first change
/// after a quiet period goes out at once, later ones are coalesced and the latest value is published by
/// <see cref="UpdateThrottle.Pump"/> when the interval ends.
/// </summary>
public sealed class ObservableValue<T> : IReadOnlyObservable<T>, IDeferredNotification
{
    private readonly IEqualityComparer<T> _comparer;
    private readonly UpdateThrottle? _throttle;
    private readonly SubscriberList<T> _subscribers = new();
    private T _value;
    private T _published;
    private TimeSpan? _lastPublishedAt;
    private bool _deferred;
    private bool _subscribersDiverged;

    public ObservableValue(T initial = default!, IEqualityComparer<T>? comparer = null)
    {
        _value = initial;
        _published = initial;
        _comparer = comparer ?? EqualityComparer<T>.Default;
    }

    /// <param name="throttle">Clock and pump for held-back notifications.</param>
    /// <param name="minInterval">Shortest real time between two notifications; zero disables throttling.</param>
    public ObservableValue(T initial, UpdateThrottle throttle, TimeSpan minInterval, IEqualityComparer<T>? comparer = null)
        : this(initial, comparer)
    {
        ArgumentNullException.ThrowIfNull(throttle);
        ArgumentOutOfRangeException.ThrowIfLessThan(minInterval, TimeSpan.Zero);
        _throttle = throttle;
        MinInterval = minInterval;
    }

    public TimeSpan MinInterval { get; }

    public T Value
    {
        get => _value;
        set => Set(value);
    }

    /// <summary>True while a change is held back by the throttle.</summary>
    public bool HasPendingNotification => _deferred;

    /// <returns>True when the value changed.</returns>
    public bool Set(T value)
    {
        if (_comparer.Equals(_value, value)) return false;
        _value = value;

        if (_throttle is null || MinInterval <= TimeSpan.Zero)
        {
            Publish(value, null);
            return true;
        }

        if (_deferred) return true;
        var now = _throttle.Clock.Elapsed;
        if (IsDue(now))
        {
            Publish(value, now);
        }
        else
        {
            _deferred = true;
            _throttle.Defer(this);
        }
        return true;
    }

    public IDisposable Subscribe(Action<T> onChanged, bool invokeImmediately = true)
    {
        ArgumentNullException.ThrowIfNull(onChanged);
        var subscription = _subscribers.Add(onChanged);
        if (invokeImmediately)
        {
            // The new subscriber now shows a value the others have not been sent yet, so the held notification
            // must go out even if the value returns to the published one before the interval ends.
            if (_deferred && !_comparer.Equals(_published, _value)) _subscribersDiverged = true;
            onChanged(_value);
        }
        return subscription;
    }

    bool IDeferredNotification.TryDeliver(TimeSpan now)
    {
        if (!IsDue(now)) return false;
        _deferred = false;
        // Changed and changed back inside the interval: subscribers already show this value.
        if (!_subscribersDiverged && _comparer.Equals(_published, _value)) return true;
        Publish(_value, now);
        return true;
    }

    private bool IsDue(TimeSpan now) => _lastPublishedAt is not { } last || now - last >= MinInterval;

    private void Publish(T value, TimeSpan? now)
    {
        _published = value;
        _lastPublishedAt = now;
        _subscribersDiverged = false;
        _subscribers.Notify(value);
    }

    public override string ToString() => _value switch
    {
        null => string.Empty,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => _value.ToString() ?? string.Empty,
    };
}
